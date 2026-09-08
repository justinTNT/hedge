module Client.Shared

open Feliz
open Feliz.Router
open Fable.Core
open Hedge.Interface
open Models.Api
open Client.Types

// -- Deployment configuration --
// Injected into the page at build time (see vite.config.js). Defaults keep a
// root-mounted darwin.news build behaving exactly as before.

/// Sub-path this deployment is served under, e.g. "/st". Empty when at the root.
[<Emit("window.BASE_PATH || ''")>]
let basePath : string = jsNative

[<Emit("window.SITE_LOGO || '/public/darwinnews.png'")>]
let private siteLogo : string = jsNative

/// Short human date from a Unix-seconds timestamp (created_at is stored in seconds).
[<Emit("new Date($0 * 1000).toLocaleDateString('en-AU', { year: 'numeric', month: 'short', day: 'numeric' })")>]
let formatDate (ts: int) : string = jsNative

/// Install a global scroll/resize watcher (once) that calls `onNear` whenever the
/// sentinel element sits within 600px of the viewport bottom. A scroll listener
/// (unlike IntersectionObserver, which only fires on enter/exit transitions)
/// re-checks on every scroll, so it keeps loading reliably even when a freshly
/// appended page is still short. `onNear` (LoadMoreFeed) self-guards, so firing
/// often is harmless. Idempotent via a window flag.
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

/// After the next paint, if the sentinel sits within (viewport + 400px), call
/// `onMore`. Drives "fill to viewport" so a short feed keeps loading pages
/// without a scroll gesture (the IntersectionObserver above then takes over for
/// subsequent scrolls). Runs post-paint so it measures the freshly-rendered DOM.
[<Emit("""(function(id, cb){
  var tries = 0;
  function go(){
    var el = document.getElementById(id);
    if(!el){ if(tries++ < 20){ setTimeout(go, 50); } return; }  // wait out React commit
    if(el.getBoundingClientRect().top < window.innerHeight + 400){ cb(); }
  }
  requestAnimationFrame(go);
})($0, $1)""")>]
let loadMoreIfSentinelVisible (elementId: string) (onMore: unit -> unit) : unit = jsNative

/// Hide an <img> that failed to load (dead external hotlink), so a rotted link
/// collapses cleanly instead of showing a broken-image icon.
[<Emit("$0.target.style.display = 'none'")>]
let private hideBrokenImg (e: obj) : unit = jsNative

/// BigText-style headline fitting (usba.se only): scale each feed headline so it
/// fills the column width, the way the old usba.se did with the jQuery BigText
/// plugin. Re-fits all headlines (idempotent) and installs a one-time resize
/// listener; a title too long to fit at the floor size is left to wrap.
[<Emit("""(function(){
  function fitOne(h){
    h.style.whiteSpace='nowrap'; h.style.fontSize='';
    var cw=h.clientWidth, base=parseFloat(getComputedStyle(h).fontSize)||32, tw=h.scrollWidth;
    if(cw>0 && tw>0){ h.style.fontSize=Math.max(22, Math.min(base*cw/tw, 110))+'px'; }
    if(h.scrollWidth > cw+2){ h.style.whiteSpace='normal'; }   // too long even scaled -> wrap
  }
  function fit(){
    if(!document.body.classList.contains('tenant-usbase')) return;
    var hs=document.querySelectorAll('.feed-item h2');
    if(!hs.length) return;
    if(hs[0].clientWidth===0){ setTimeout(fit,60); return; }   // wait for layout
    hs.forEach(fitOne);
  }
  requestAnimationFrame(fit);
  if(document.fonts && document.fonts.ready){ document.fonts.ready.then(fit); }  // re-fit once League Gothic loads
  if(!window.__hedgeFitResize){ window.__hedgeFitResize=true; window.addEventListener('resize', fit, {passive:true}); }
})()""")>]
let fitHeadlines () : unit = jsNative

let private baseSegments =
    basePath.Split('/') |> Array.filter (fun s -> s <> "") |> Array.toList

/// Drop the deployment prefix from router segments, so route matching is
/// written as though the app were always mounted at the root.
let stripBase (segments: string list) =
    let rec strip prefix rest =
        match prefix, rest with
        | [], remaining -> remaining
        | p :: ps, r :: rs when p = r -> strip ps rs
        | _ -> segments
    strip baseSegments segments

/// Route segments as the app should match them.
///
/// Feliz.Router appends a query string as its own segment, so
/// `/some-slug?fbclid=...` arrives as [ "some-slug"; "?fbclid=..." ] and misses
/// every single-segment route — shared links land on the feed instead of the
/// item. Query params are read from window.location.search, never from the
/// segments, so dropping it here is lossless.
let routeOf (segments: string list) =
    segments
    |> List.filter (fun s -> not (s.StartsWith "?"))
    |> stripBase

/// Navigate to app-relative segments, re-applying the deployment prefix.
let navigateTo (segments: string list) =
    Router.navigatePath (List.toArray (baseSegments @ segments))

/// Navigate to an app-relative path string, e.g. "/" or "/some-slug".
let navigateToPath (path: string) =
    navigateTo (path.Split('/') |> Array.filter (fun s -> s <> "") |> Array.toList)

let private tagColors = [| "#e74c3c"; "#3498db"; "#2ecc71"; "#9b59b6"; "#f39c12"; "#1abc9c"; "#e91e63"; "#00bcd4" |]

let private tagColor (name: string) =
    let hash = name.ToCharArray() |> Array.fold (fun acc c -> int c + acc * 31) 0
    tagColors.[abs hash % tagColors.Length]

let tagPill (tag: string) =
    Html.span [
        prop.className "tag"
        prop.style [
            style.backgroundColor (tagColor tag)
            style.color "#fff"
            style.cursor.pointer
        ]
        prop.text tag
        prop.onClick (fun e ->
            e.stopPropagation ()
            navigateTo [ "tag"; tag ]
        )
    ]

let loading =
    Html.div [
        prop.className "loading"
        prop.text "Loading..."
    ]

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

let feedItem (item: GetFeed.FeedItem) =
    let itemPath = item.Slug |> Option.defaultValue item.Id
    Html.article [
        prop.key item.Id
        prop.className "feed-item"
        prop.style [ style.cursor.pointer ]
        prop.onClick (fun _ -> navigateTo [ itemPath ])
        prop.children [
            Html.h2 [ prop.text item.Title ]
            match item.Extract with
            | Some (RichContent text) ->
                Html.p [ prop.className "extract";
                         prop.children [
                                   Html.span [ prop.text (RichText.extractPlainText text) ];
                                   match item.Image with
                                   | Some url ->
                                       Html.img [ prop.src url; prop.onError (fun (e: Browser.Types.Event) -> hideBrokenImg e) ]
                                   | None -> Html.none
                               ]
                ]
            | None -> Html.none
        ]
    ]

let avatar (url: string) =
    Html.img [
        prop.className "avatar"
        prop.src url
    ]

let private loginButton (provider: string) (label: string) =
    // Never round-trip back to an /auth/* page — after claiming it would dead-end
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
                    // ActivatedAt is history (most recent wins) — only the session's
                    // current identity is active, not everything ever activated
                    let isActive = Some id.Id = activeId
                    let isSelected = model.SelectedIdentity = Some id.Id
                    Html.div [
                        prop.className (
                            if isActive then "identity-option active"
                            elif isSelected then "identity-option selected"
                            else "identity-option selectable")
                        // Every row is selectable: the active one still has an
                        // action (disconnect), it just can't be switched to.
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
                                // Actions appear once a row is chosen
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

                // Providers you haven't linked. Connected ones aren't repeated
                // here — they're already above as identities, with avatars.
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
            // One control, one panel. Even with no identity yet the badge opens
            // it, so signing in is reachable without a second button.
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
                  Html.img [
                    prop.src (basePath + siteLogo)
                  ]
                ]
            ]
            identityView model dispatch
        ]
    ]

let nav =
    Html.nav [
        prop.children [
            Html.a [
                prop.text "Hedge"
                prop.style [ style.cursor.pointer ]
                prop.onClick (fun _ -> navigateTo [])
                prop.children [
                  Html.img [
                    prop.src (basePath + siteLogo)
                  ]
                ]
            ]
        ]
    ]

