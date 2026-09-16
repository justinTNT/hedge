module Blog.Client.Shared

// The shared guest-session accessor is host-provided (packages/hedge/src/Client);
// alias it locally, the same way Client.RichText is aliased.
module GuestSession = Client.GuestSession

// The shared rich-text module lives in the host's Client.RichText namespace.
module RichText = Client.RichText

open Feliz
open Feliz.Router
open Fable.Core
open Hedge.Interface
open Blog.Api
open Blog.Client.Types

// -- Transport-neutral API client (C2 migration) --
// Blog's page updates call these instead of the ambient-transport bare ClientGen functions:
// the generated Client record wired to the browser transport, with the typed ApiError
// rendered back to the string the existing Msgs / Model.Error already carry (so Msg and
// model shapes are unchanged). This is a compatibility shim binding the default browser
// transport; consumers move to an injected client in C5. Bonus: Http.sendDecode checks the
// status before decoding, so a 4xx now surfaces the server's message (e.g. "slug: already
// taken") instead of a decode-failure string.
module Api =
    let private client = Blog.ClientGen.createClient Client.Api.browserTransport
    let private asString (p: JS.Promise<Result<'T, Hedge.Http.ApiError>>) : JS.Promise<Result<'T, string>> =
        promise { let! r = p in return Result.mapError Hedge.Http.renderError r }
    let blogGetFeed query = asString (client.blogGetFeed query)
    let blogGetItem id = asString (client.blogGetItem id)
    let blogGetItemsByTag tag query = asString (client.blogGetItemsByTag tag query)
    let blogGetTags () = asString (client.blogGetTags ())
    let blogSubmitComment req = asString (client.blogSubmitComment req)
    let blogSubmitItem req = asString (client.blogSubmitItem req)

// -- Deployment configuration --
// Injected into the page at build time (see vite.config.js). Defaults keep a
// root-mounted darwin.news build behaving exactly as before.

/// Sub-path this deployment is served under, e.g. "/st". Empty when at the root.
/// Used for API + asset URLs.
[<Emit("window.BASE_PATH || ''")>]
let basePath : string = jsNative

/// The path this module is mounted at: "/blog" when a secondary mount, "" when it's
/// the site's primary module (served at the naked URL, e.g. darwin.news). ROUTING
/// (nav + route-strip) is based here; API + assets keep the deployment base above.
/// The mount's shell sets `window.MOUNT_BASE`; absent ⇒ "" (primary).
[<Emit("window.MOUNT_BASE || ''")>]
let private mountBase : string = jsNative

/// Tenant logo from the framework accessor. The per-site default lives in the
/// app's vite config (SITE_LOGO), not here — a shared module names no site's asset.
let private siteLogo : string = Hedge.Tenant.config.Logo

/// Short human date from a Unix-seconds timestamp (created_at is stored in
/// seconds), in the tenant's locale ("" -> the viewer's own).
[<Emit("new Date($0 * 1000).toLocaleDateString($1 || undefined, { year: 'numeric', month: 'short', day: 'numeric' })")>]
let private formatDateIn (ts: int) (locale: string) : string = jsNative
let formatDate (ts: int) : string = formatDateIn ts Hedge.Tenant.config.Locale

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
    (basePath + mountBase).Split('/') |> Array.filter (fun s -> s <> "") |> Array.toList

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

let tagPill (ctx: Content.HostContext) (tag: string) =
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
            ctx.Navigate [ "tag"; tag ]
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

let feedItem (ctx: Content.HostContext) (item: GetFeed.FeedItem) =
    let itemPath = item.Slug |> Option.defaultValue item.Id
    Html.article [
        prop.key item.Id
        prop.className "feed-item"
        prop.style [ style.cursor.pointer ]
        prop.onClick (fun _ -> ctx.Navigate [ itemPath ])
        prop.children [
            Html.h2 [ prop.text item.Title ]
            let imageNode =
                match item.Image with
                | Some url -> Html.img [ prop.src url; prop.onError (fun (e: Browser.Types.Event) -> hideBrokenImg e) ]
                | None -> Html.none
            // Show the image whenever it's set: beside the teaser when there's an extract,
            // otherwise as a standalone thumbnail (previously an item with an image but no
            // extract showed no image at all).
            match item.Extract with
            | Some (RichContent text) ->
                Html.p [ prop.className "extract";
                         prop.children [
                                   Html.span [ prop.text (RichText.extractPlainText text) ];
                                   imageNode
                               ]
                ]
            | None ->
                match item.Image with
                | Some _ -> Html.div [ prop.className "feed-item-thumb"; prop.children [ imageNode ] ]
                | None -> Html.none
        ]
    ]

/// Group consecutive items by calendar day (lists are date-descending, so
/// same-day items are contiguous). Grouping over the full accumulated list means
/// a day straddling a pagination boundary still yields a single heading. Shared
/// by the feed and tag pages.
let groupByDay (items: GetFeed.FeedItem list) : (string * GetFeed.FeedItem list) list =
    ([], items)
    ||> List.fold (fun groups item ->
        let day = formatDate item.Timestamp
        match groups with
        | (d, dayItems) :: rest when d = day -> (d, dayItems @ [ item ]) :: rest
        | _ -> (day, [ item ]) :: groups)
    |> List.rev

let dayDivider (day: string) =
    Html.div [
        prop.key ("day-" + day)
        prop.className "feed-day"
        prop.children [ Html.span [ prop.className "feed-day-label"; prop.text day ] ]
    ]

let avatar (url: string) =
    Html.img [
        prop.className "avatar"
        prop.src url
    ]

