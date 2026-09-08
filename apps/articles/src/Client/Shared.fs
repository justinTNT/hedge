module Client.Shared

open Feliz
open Feliz.Router
open Fable.Core
open Hedge.Interface
open Models.Api
open Client.Types

// -- Deployment configuration (injected at build time; see vite.config.js) --

/// Sub-path this deployment is served under, e.g. "/st". Empty when at the root.
[<Emit("window.BASE_PATH || ''")>]
let basePath : string = jsNative

[<Emit("window.SITE_LOGO || '/public/darwinnews.png'")>]
let private siteLogo : string = jsNative

[<Emit("window.SITE_SLUG || ''")>]
let siteSlug : string = jsNative

/// Set the browser tab title: "<article> · <site>" for an article, or just the
/// site title when passed "". Uses SITE_TITLE as the base (the server pre-sets
/// <title> to the article name for SEO, so we can't read it off document.title).
[<Emit("(function(t){var b=window.SITE_TITLE||'';document.title=t?(t+' · '+b):b;})($0)")>]
let setDocTitle (articleTitle: string) : unit = jsNative

/// First image src inside a RichContent (TipTap) doc, or "" if none. Lets the feed
/// derive a thumbnail from the teaser itself, so an authored article shows its
/// image without a separately-set hero.
[<Emit("(function(s){try{var d=JSON.parse(s);var f=function(n){if(!n)return null;if(n.type==='image'&&n.attrs&&n.attrs.src)return n.attrs.src;var c=n.content;if(c)for(var i=0;i<c.length;i++){var r=f(c[i]);if(r)return r;}return null;};return f(d)||'';}catch(e){return '';}})($0)")>]
let firstImageSrc (richJson: string) : string = jsNative

/// Short human date from a Unix-seconds timestamp.
[<Emit("new Date($0 * 1000).toLocaleDateString('en-AU', { year: 'numeric', month: 'short', day: 'numeric' })")>]
let formatDate (ts: int) : string = jsNative

/// Month abbreviation + day-of-month, for the two-row date badge.
[<Emit("new Date($0 * 1000).toLocaleDateString('en-AU', { month: 'short' })")>]
let formatMonth (ts: int) : string = jsNative

[<Emit("new Date($0 * 1000).getDate()")>]
let formatDay (ts: int) : int = jsNative

[<Emit("new Date($0 * 1000).getFullYear()")>]
let formatYear (ts: int) : int = jsNative

/// Global scroll/resize watcher (once) firing `onNear` when the sentinel sits
/// within 600px of the viewport bottom. `onNear` self-guards.
[<Emit("""(function(id, cb){
  if(!window.__hedgeWatchers){ window.__hedgeWatchers = {}; }
  if(window.__hedgeWatchers[id]){ return; }
  window.__hedgeWatchers[id] = true;
  function check(){
    var el = document.getElementById(id);
    if(!el){ return; }
    if(el.getBoundingClientRect().top < window.innerHeight + 600){ cb(); }
  }
  window.addEventListener('scroll', check, { passive: true });
  window.addEventListener('resize', check, { passive: true });
})($0, $1)""")>]
let watchScroll (elementId: string) (onNear: unit -> unit) : unit = jsNative

/// After the next paint, if the sentinel is within (viewport + 400px), call
/// `onMore` — drives "fill to viewport" so a short list keeps loading.
[<Emit("""(function(id, cb){
  var tries = 0;
  function go(){
    var el = document.getElementById(id);
    if(!el){ if(tries++ < 20){ setTimeout(go, 50); } return; }
    if(el.getBoundingClientRect().top < window.innerHeight + 400){ cb(); }
  }
  requestAnimationFrame(go);
})($0, $1)""")>]
let loadMoreIfSentinelVisible (elementId: string) (onMore: unit -> unit) : unit = jsNative

/// Hide an <img> that failed to load, so a rotted link collapses cleanly.
[<Emit("$0.target.style.display = 'none'")>]
let private hideBrokenImg (e: obj) : unit = jsNative

let private baseSegments =
    basePath.Split('/') |> Array.filter (fun s -> s <> "") |> Array.toList

let stripBase (segments: string list) =
    let rec strip prefix rest =
        match prefix, rest with
        | [], remaining -> remaining
        | p :: ps, r :: rs when p = r -> strip ps rs
        | _ -> segments
    strip baseSegments segments

/// Route segments as the app should match them (drops a trailing query segment).
let routeOf (segments: string list) =
    segments
    |> List.filter (fun s -> not (s.StartsWith "?"))
    |> stripBase

let navigateTo (segments: string list) =
    Router.navigatePath (List.toArray (baseSegments @ segments))

let navigateToPath (path: string) =
    navigateTo (path.Split('/') |> Array.filter (fun s -> s <> "") |> Array.toList)

let loading =
    Html.div [ prop.className "loading"; prop.text "Loading..." ]

let error (msg: string) dispatch =
    Html.div [
        prop.className "error"
        prop.children [
            Html.span [ prop.text msg ]
            Html.button [
                prop.text "Dismiss"
                prop.onClick (fun _ -> dispatch DismissError)
            ]
        ]
    ]

let feedItem (item: GetArticles.ArticleItem) =
    let itemPath = item.Slug |> Option.defaultValue item.Id
    Html.article [
        prop.key item.Id
        prop.className "feed-item"
        prop.style [ style.cursor.pointer ]
        prop.onClick (fun _ -> navigateTo [ itemPath ])
        prop.children [
            Html.h2 [ prop.text item.Title ]
            match item.Teaser with
            | Some (RichContent text) ->
                // Thumbnail: prefer the teaser's own first image (works for authored
                // articles too), fall back to the Image field.
                let thumb =
                    let t = firstImageSrc text
                    if t <> "" then Some t else item.Image
                Html.p [
                    prop.className "extract"
                    prop.children [
                        Html.span [ prop.text (RichText.extractPlainText text) ]
                        match thumb with
                        | Some url ->
                            Html.img [ prop.src url; prop.onError (fun (e: Browser.Types.Event) -> hideBrokenImg e) ]
                        | None -> Html.none
                    ]
                ]
            | None -> Html.none
        ]
    ]

/// Group consecutive items by calendar day (lists are date-descending). Carries a
/// representative timestamp so the divider can format the badge (month + day).
let groupByDay (items: GetArticles.ArticleItem list) : (int * GetArticles.ArticleItem list) list =
    ([], items)
    ||> List.fold (fun groups item ->
        let day = formatDate item.Timestamp
        match groups with
        | (ts, dayItems) :: rest when formatDate ts = day -> (ts, dayItems @ [ item ]) :: rest
        | _ -> (item.Timestamp, [ item ]) :: groups)
    |> List.rev

/// Date badge: month abbreviation over the day number (two rows, to fit the tab).
let dayDivider (ts: int) =
    Html.div [
        prop.key ("day-" + string ts)
        prop.className "feed-day"
        prop.children [
            Html.span [
                prop.className "feed-day-label"
                prop.children [
                    Html.span [ prop.className "fd-month"; prop.text (formatMonth ts) ]
                    Html.span [ prop.className "fd-day"; prop.text (string (formatDay ts)) ]
                    Html.span [ prop.className "fd-year"; prop.text (string (formatYear ts)) ]
                ]
            ]
        ]
    ]

let avatar (url: string) =
    Html.img [ prop.className "avatar"; prop.src url ]

let private loginButton (provider: string) (label: string) =
    let path = Browser.Dom.window.location.pathname
    let returnTo = if path.StartsWith "/auth/" then "/" else path
    Html.a [
        prop.className (sprintf "login-btn login-%s" provider)
        prop.href (sprintf "/api/auth/%s/login?returnTo=%s" provider (Fable.Core.JS.encodeURIComponent returnTo))
        prop.text label
    ]

let private providerLabel (provider: string) =
    match provider with
    | "google" -> "Google"
    | "github" -> "GitHub"
    | "microsoft" -> "Microsoft"
    | "facebook" -> "Facebook"
    | "anonymous" -> "Anonymous"
    | p -> p

let private identitySwitcher (model: Model) dispatch =
    if not model.ShowIdentitySwitcher then Html.none
    else
        Html.div [
            prop.className "identity-switcher"
            prop.children [
                Html.h4 [ prop.text "Switch identity" ]
                let activeId = model.GuestSession.Identity |> Option.map (fun i -> i.Id)
                yield! model.Identities |> List.map (fun id ->
                    let isActive = Some id.Id = activeId
                    let isSelected = model.SelectedIdentity = Some id.Id
                    Html.div [
                        prop.className (
                            if isActive then "identity-option active"
                            elif isSelected then "identity-option selected"
                            else "identity-option selectable")
                        prop.onClick (fun _ -> dispatch (SelectIdentity id.Id))
                        prop.children [
                            avatar (if id.Picture <> "" then id.Picture else GuestSession.avatarForAuthor id.Name)
                            Html.div [
                                prop.className "identity-info"
                                prop.children [
                                    Html.span [ prop.className "identity-name"; prop.text id.Name ]
                                    Html.span [ prop.className "identity-provider"; prop.text (providerLabel id.Provider) ]
                                ]
                            ]
                            if isSelected then
                                if not isActive then
                                    Html.button [
                                        prop.className "btn-merge"
                                        prop.text "Merge"
                                        prop.title "Bring my comments with me"
                                        prop.onClick (fun e -> e.stopPropagation(); dispatch (RevertIdentity (id.Id, true)))
                                    ]
                                    Html.button [
                                        prop.className "btn-abandon"
                                        prop.text "Fresh"
                                        prop.title "Leave comments where they are"
                                        prop.onClick (fun e -> e.stopPropagation(); dispatch (RevertIdentity (id.Id, false)))
                                    ]
                                if id.Provider <> "anonymous" then
                                    Html.button [
                                        prop.className "btn-disconnect"
                                        prop.text "Disconnect"
                                        prop.title "Abandon this identity — its comments stay with it, and signing in again reclaims them"
                                        prop.onClick (fun e -> e.stopPropagation(); dispatch (DisconnectIdentity id.Id))
                                    ]
                        ]
                    ]
                )

                let unconnected =
                    model.AvailableProviders
                    |> List.filter (fun p -> model.Identities |> List.forall (fun i -> i.Provider <> p))
                if not unconnected.IsEmpty then
                    Html.div [
                        prop.className "switcher-connect"
                        prop.children [
                            Html.h4 [ prop.text "Add a connection" ]
                            Html.div [
                                prop.className "connect-options"
                                prop.children (unconnected |> List.map (fun p -> loginButton p (providerLabel p)))
                            ]
                        ]
                    ]
            ]
        ]

let private identityView (model: Model) dispatch =
    let session = model.GuestSession
    Html.div [
        prop.className "identity-area"
        prop.children [
            Html.div [
                prop.className "identity-badge"
                prop.onClick (fun _ -> dispatch ToggleIdentitySwitcher)
                prop.children [
                    avatar session.AvatarUrl
                    Html.span [
                        prop.text (
                            match session.Identity with
                            | Some identity -> identity.Name
                            | None -> session.DisplayName)
                    ]
                ]
            ]
            identitySwitcher model dispatch
        ]
    ]

let navWithSession (model: Model) dispatch =
    Html.nav [
        prop.children [
            Html.a [
                prop.style [ style.cursor.pointer ]
                prop.onClick (fun _ -> navigateTo [])
                prop.children [
                  Html.img [ prop.src (basePath + siteLogo) ]
                ]
            ]
            identityView model dispatch
        ]
    ]

/// justat.at's static sidebar, ported verbatim from the old app's baseplate
/// (lime "just@justat.at" masthead + About / My sites / Contact / Links).
/// Tenant-specific for now; a later per-tenant config would generalise it.
let private sidebarLink (href: string) (text: string) =
    Html.a [ prop.href href; prop.text text ]

let justatSidebar =
    Html.aside [
        prop.className "js-sidebar"
        prop.children [
            Html.div [
                prop.className "js-masthead"
                prop.children [
                    Html.a [ prop.href "mailto:just@justat.at"; prop.text "just@justat.at" ]
                ]
            ]
            Html.div [
                prop.className "js-sections"
                prop.children [
                    Html.section [
                        prop.children [
                            Html.h5 "About"
                            Html.p [
                                prop.children [
                                    Html.text "Yeah, I admit, it's a vanity blog. I never had one, til "
                                    sidebarLink "http://hipstrider.com" "hipstrider"
                                    Html.text " beat me to it. I do a bit of webdev work, and I've found this a useful place to test out new ideas in the wild."
                                ]
                            ]
                        ]
                    ]
                    Html.section [
                        prop.children [
                            Html.h5 "My sites"
                            Html.p [
                                prop.children [
                                    Html.text "For a few years now I have maintained "
                                    sidebarLink "http://darwin.news" "a"
                                    sidebarLink "http://usba.se" "few"
                                    sidebarLink "http://wt.fail" "web"
                                    sidebarLink "http://mtmu.se" "logs, "
                                    Html.text "I have kept alive an old nuclear news "
                                    sidebarLink "http://ntne.ws/" "archive"
                                    Html.text " : and I have a music site : "
                                    sidebarLink "https://dont.saymay.be" "dont.saymay.be"
                                    Html.text "."
                                ]
                            ]
                        ]
                    ]
                    Html.section [
                        prop.children [
                            Html.h5 "Contact me"
                            Html.p [
                                prop.children [
                                    Html.text "I'm contactable on "
                                    sidebarLink "https://www.linkedin.com/in/justin-tutty-850b76301/" "linkedin"
                                    Html.text "  and "
                                    Html.text "via SMS (0424-028-741) or email (see above)."
                                    Html.text " You can get your own "
                                    sidebarLink "https://darwin.email" "darwin.email"
                                ]
                            ]
                        ]
                    ]
                    Html.section [
                        prop.children [
                            Html.h5 "Links"
                            Html.p [
                                prop.children [
                                    sidebarLink "https://nonewgasnt.org.au/" "nonewgasnt.org.au"
                                    Html.text " | "
                                    sidebarLink "https://nowdochemtrails.net/" "nowdochemtrails.net"
                                    Html.text " | "
                                    sidebarLink "https://edarwin.au/" "edarwin.au"
                                    Html.text " | "
                                    sidebarLink "https://ausbases.au/" "ausbases.au"
                                ]
                            ]
                        ]
                    ]
                ]
            ]
        ]
    ]
