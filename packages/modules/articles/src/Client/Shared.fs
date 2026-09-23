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

// CP-C: the API client is now host-injected (Articles.Client.Types.Deps.Api, built once by the
// host from its chosen transport) and its typed Hedge.Http.ApiError flows through the page updates
// and Model.Error unchanged. The old browser-transport shim (module Api rendering ApiError ->
// string) is gone; this module keeps only view/content helpers.

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

/// Navigate to a path that may be app-relative ("/" or "/some-slug") OR already carry the deployment
/// prefix (e.g. an OAuth `returnTo` captured from window.location.pathname). `routeOf` strips the base
/// if present, so `navigateTo` never double-applies it (idempotent w.r.t. the prefix).
let navigateToPath (path: string) =
    navigateTo (routeOf (path.Split('/') |> Array.filter (fun s -> s <> "") |> Array.toList))

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

