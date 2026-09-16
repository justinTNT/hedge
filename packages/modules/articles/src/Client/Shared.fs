module Articles.Client.Shared

// The shared guest-session accessor is host-provided (packages/hedge/src/Client);
// alias it locally, the same way Client.RichText is aliased.
module GuestSession = Client.GuestSession

// The shared rich-text module lives in the host's Client.RichText namespace.
module RichText = Client.RichText

open Feliz
open Feliz.Router
open Fable.Core
open Hedge.Interface
open Articles.Api
open Articles.Client.Types

// -- Transport-neutral API client (C2 migration) --
// Articles' page updates call these instead of the ambient-transport bare ClientGen
// functions: the generated Client record wired to the browser transport, with the typed
// ApiError rendered back to the string the existing Msgs / Model.Error already carry (so Msg
// and model shapes are unchanged). Compatibility shim binding the default browser transport;
// consumers move to an injected client in C5. Mirrors Blog.Client.Shared.Api. Bonus:
// Http.sendDecode checks status before decoding, so a 4xx surfaces the server's message.
module Api =
    let private client = Articles.ClientGen.createClient Client.Api.browserTransport
    let private asString (p: JS.Promise<Result<'T, Hedge.Http.ApiError>>) : JS.Promise<Result<'T, string>> =
        promise { let! r = p in return Result.mapError Hedge.Http.renderError r }
    let articlesGetFeed query = asString (client.articlesGetFeed query)
    let articlesGetPost id = asString (client.articlesGetPost id)
    let articlesSubmitComment req = asString (client.articlesSubmitComment req)

// -- Deployment configuration (injected at build time; see vite.config.js) --

/// Sub-path this deployment is served under, e.g. "/st". Empty when at the root.
/// Used for API + asset URLs.
[<Emit("window.BASE_PATH || ''")>]
let basePath : string = jsNative

/// The path this module is mounted at: "" when it's the site's primary module
/// (served at the naked URL — articles is primary on justat/ndct). ROUTING (nav +
/// route-strip) is based here; API + assets keep the deployment base above. The
/// mount's shell sets window.MOUNT_BASE; absent => "" (primary).
[<Emit("window.MOUNT_BASE || ''")>]
let private mountBase : string = jsNative

/// Tenant logo from the framework accessor. The per-site default lives in the
/// app's vite config (SITE_LOGO), not here — a shared module names no site's asset.
let private siteLogo : string = Hedge.Tenant.config.Logo

/// Set the browser tab title: "<post> · <site>" for a post, or just the site
/// title when passed "". Uses SITE_TITLE as the base (the server pre-sets <title>
/// to the post name for SEO, so we can't read it off document.title).
[<Emit("(function(t){var b=window.SITE_TITLE||'';document.title=t?(t+' · '+b):b;})($0)")>]
let setDocTitle (postTitle: string) : unit = jsNative

/// First image src inside a RichContent (TipTap) doc, or "" if none. Lets the feed
/// derive a thumbnail from the teaser itself, so an authored post shows its image
/// without a separately-set hero.
[<Emit("(function(s){try{var d=JSON.parse(s);var f=function(n){if(!n)return null;if(n.type==='image'&&n.attrs&&n.attrs.src)return n.attrs.src;var c=n.content;if(c)for(var i=0;i<c.length;i++){var r=f(c[i]);if(r)return r;}return null;};return f(d)||'';}catch(e){return '';}})($0)")>]
let firstImageSrc (richJson: string) : string = jsNative

/// Short human date from a Unix-seconds timestamp, in the tenant's locale
/// ("" -> the viewer's own).
[<Emit("new Date($0 * 1000).toLocaleDateString($1 || undefined, { year: 'numeric', month: 'short', day: 'numeric' })")>]
let private formatDateIn (ts: int) (locale: string) : string = jsNative
let formatDate (ts: int) : string = formatDateIn ts Hedge.Tenant.config.Locale

/// Month abbreviation, for the two-row date badge, in the tenant's locale.
[<Emit("new Date($0 * 1000).toLocaleDateString($1 || undefined, { month: 'short' })")>]
let private formatMonthIn (ts: int) (locale: string) : string = jsNative
let formatMonth (ts: int) : string = formatMonthIn ts Hedge.Tenant.config.Locale

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
    (basePath + mountBase).Split('/') |> Array.filter (fun s -> s <> "") |> Array.toList

/// Drop the deployment/mount prefix from router segments, so route matching is
/// written as though the app were always mounted at the root.
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

let feedItem (ctx: Content.HostContext) (item: GetFeed.FeedItem) =
    let itemPath = item.Slug |> Option.defaultValue item.Id
    Html.article [
        prop.key item.Id
        prop.className "feed-item"
        prop.style [ style.cursor.pointer ]
        prop.onClick (fun _ -> ctx.Navigate [ itemPath ])
        prop.children [
            Html.h2 [ prop.text item.Title ]
            match item.Teaser with
            | Some (RichContent text) ->
                // Thumbnail: prefer the teaser's own first image (works for authored
                // posts too), fall back to the Image field.
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
let groupByDay (items: GetFeed.FeedItem list) : (int * GetFeed.FeedItem list) list =
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
                // A real link to home (so it's right-clickable / shows a URL); the
                // onClick keeps navigation in-SPA. justat's theme hides the img and
                // labels this "Home" via CSS ::before.
                prop.href (basePath + "/")
                prop.style [ style.cursor.pointer ]
                prop.onClick (fun (e: Browser.Types.MouseEvent) -> e.preventDefault(); navigateTo [])
                prop.children [
                  Html.img [ prop.src (basePath + siteLogo) ]
                ]
            ]
            // The blog module's path-mount is a separate bundle, so this is a real
            // navigation (href), not SPA routing. Shown only where the blog is
            // mounted (the "blog" feature flag, set on the site's build), matching
            // the worker's HEDGE_SITE blog-composition gate.
            if Hedge.Tenant.hasFeature "blog" then
                Html.a [
                    prop.className "nav-blog"
                    prop.href (basePath + "/blog")
                    prop.text "Web Log"
                ]
            // Prominent link to the tenant's external info/companion page, when set.
            if Hedge.Tenant.config.InfoUrl <> "" then
                Html.a [
                    prop.className "nav-info"
                    prop.href Hedge.Tenant.config.InfoUrl
                    prop.text (if Hedge.Tenant.config.InfoLabel <> "" then Hedge.Tenant.config.InfoLabel else "Info")
                ]
            identityView model dispatch
        ]
    ]

/// ndct's full-screen intro banner (HTML5UP "Massively" look): the contrails hero
/// with the title + subtitle. Shown on the home feed only.
let ndctHero =
    Html.section [
        prop.className "ndct-hero"
        prop.children [
            Html.div [
                prop.className "ndct-hero-inner"
                prop.children [
                    Html.h1 [ prop.className "ndct-hero-title"; prop.text "Now Do Chemtrails" ]
                    Html.p [ prop.className "ndct-hero-sub"; prop.text "theories > news" ]
                ]
            ]
            Html.a [ prop.className "ndct-hero-more"; prop.href "#feed"; prop.text "↓" ]
        ]
    ]

/// justat.at's static sidebar, ported verbatim from the old app's baseplate.
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
                                    Html.text "For some years now I have maintained "
                                    sidebarLink "http://darwin.news" "a "
                                    sidebarLink "http://usba.se" "few "
                                    sidebarLink "http://wt.fail" "web "
                                    sidebarLink "http://mtmu.se" "logs, "
                                    Html.text "I have kept alive an old nuclear news "
                                    sidebarLink "http://ntne.ws/" "archive"
                                    Html.text " : and I have a couple of "
                                    sidebarLink "https://dont.saymay.be" "music "
                                    sidebarLink "https://dont.saymay.be" "sites."
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
