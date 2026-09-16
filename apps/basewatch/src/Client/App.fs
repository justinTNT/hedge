module Client.App

open Feliz
open Feliz.Router
open Elmish
open Fable.Core
open Browser.Types
open Browser.Dom
open Thoth.Json
open Models.Api

// -- model --

type PageState =
    | PageLoading
    | PageLoaded of GetPage.PageView
    | PageMissing

/// A story from the usba.se feed, surfaced on the home page. Its full article
/// (and comments) live on usba.se — Href links across.
type NewsItem = { Title: string; Href: string; Image: string option; Teaser: string }

type Model = {
    Site: GetSite.Response option
    Route: string list
    Page: PageState
    Feed: Result<NewsItem list, string> option   // None = loading
    MenuOpen: bool
    Error: string option
}

type Msg =
    | GotSite of Result<GetSite.Response, string>
    | GotPage of string * Result<GetPage.Response, string>
    | GotFeed of Result<NewsItem list, string>
    | UrlChanged of string list
    | ToggleMenu

/// Feliz.Router appends a query string as its own segment, so drop it — page
/// names never contain one (mirrors microblog's Shared.routeOf).
let private routeOf (segments: string list) =
    segments |> List.filter (fun s -> not (s.StartsWith "?"))

/// The page name for a route: `[]` is home (server tells us which page that is),
/// anything else is the slug.
let private currentPageName (site: GetSite.Response) (route: string list) =
    match route with
    | [] -> site.Home
    | segs -> String.concat "/" segs

// C5: the generated client over the browser transport (rendering the typed ApiError back to the
// string this app's Msgs carry). getPage/getSite were this app's last uses of the bare ClientGen
// functions; the cross-origin news fetch below stays a direct Client.Api.fetchJson.
let private apiClient = Client.ClientGen.createClient Client.Api.browserTransport
let private getPage name = promise { let! r = apiClient.getPage name in return Result.mapError Hedge.Http.renderError r }
let private getSite () = promise { let! r = apiClient.getSite () in return Result.mapError Hedge.Http.renderError r }

let private loadPageCmd (site: GetSite.Response) (route: string list) : Cmd<Msg> =
    let name = currentPageName site route
    Cmd.OfPromise.either getPage name
        (fun r -> GotPage(name, r))
        (fun ex -> GotPage(name, Error ex.Message))

// -- usba.se news feed (home page). Fetched cross-origin; the feed API sets
//    Access-Control-Allow-Origin *, so the browser can read it directly. --

/// Plain text from an Extract (ProseMirror JSON) for the teaser — a tiny JSON
/// walker, so we don't pull the TipTap bundle onto the home page.
[<Emit("""(function (s) { try { return (function walk(n){ if(!n) return ''; if(n.type==='text') return n.text||''; if(Array.isArray(n.content)) return n.content.map(walk).join(' '); return ''; })(JSON.parse(s)).replace(/\s+/g,' ').trim(); } catch (e) { return (s||'').replace(/\s+/g,' ').trim(); } })($0)""")>]
let private plainText (json: string) : string = jsNative

/// Hide an <img> whose (external) URL failed to load, so a dead hotlink
/// collapses cleanly instead of showing a broken-image icon.
[<Emit("$0.target.style.display = 'none'")>]
let private hideBrokenImg (e: Browser.Types.Event) : unit = jsNative

// usba.se runs on the composed blog module, so its feed lives under the module's
// /api/blog route prefix (the bare /api/feed path now falls through to the SPA).
// First page omits ?cursor; a trailing /start path segment is NOT a route — it
// falls through to the SPA (returns HTML), so keep the URL as the bare endpoint.
let private newsUrl = "https://usba.se/api/blog/feed"

let private decodeNews : Decoder<NewsItem list> =
    let item =
        Decode.object (fun get ->
            let id = get.Required.Field "id" Decode.string
            let slug = get.Optional.Field "slug" Decode.string
            let extract = get.Optional.Field "extract" Decode.string
            let t = extract |> Option.map plainText |> Option.defaultValue ""
            { Title = get.Required.Field "title" Decode.string
              Href = "https://usba.se/" + (slug |> Option.defaultValue id)
              Image = get.Optional.Field "image" Decode.string
              Teaser = if t.Length > 200 then t.[..199].TrimEnd() + "…" else t })
    Decode.field "items" (Decode.list item)

let private fetchNewsCmd : Cmd<Msg> =
    Cmd.OfPromise.either (fun () -> Client.Api.fetchJson newsUrl decodeNews) () GotFeed
        (fun ex -> GotFeed (Error ex.Message))

/// Home ([]) shows the news feed; any other route is a page.
let private loadForRoute (site: GetSite.Response) (route: string list) : Cmd<Msg> =
    match route with
    | [] -> Cmd.ofEffect (fun _ -> document.title <- "BaseWatch")
    | _ -> loadPageCmd site route

let init () =
    { Site = None; Route = routeOf (Router.currentUrl ()); Page = PageLoading; Feed = None; MenuOpen = false; Error = None },
    Cmd.batch [
        Cmd.OfPromise.either getSite () GotSite (fun ex -> GotSite(Error ex.Message))
        fetchNewsCmd
    ]

let private setTitle (t: string) = Cmd.ofEffect (fun _ -> document.title <- t + " — BaseWatch")
let private scrollTop = Cmd.ofEffect (fun _ -> window.scrollTo (0.0, 0.0))

let update msg model =
    match msg with
    | GotSite (Ok site) ->
        let m = { model with Site = Some site }
        m, loadForRoute site m.Route
    | GotSite (Error e) ->
        { model with Error = Some e }, Cmd.none
    | GotPage (name, result) ->
        // Ignore a stale response for a page we've since navigated away from.
        let stillCurrent = model.Site |> Option.exists (fun s -> currentPageName s model.Route = name)
        if not stillCurrent then model, Cmd.none
        else
            match result with
            | Ok resp -> { model with Page = PageLoaded resp.Page }, setTitle resp.Page.Title
            | Error _ -> { model with Page = PageMissing }, setTitle "Not found"
    | GotFeed result ->
        { model with Feed = Some result }, Cmd.none
    | UrlChanged route ->
        match model.Site with
        | Some site ->
            { model with Route = route; Page = (if route = [] then model.Page else PageLoading); MenuOpen = false },
            Cmd.batch [ loadForRoute site route; scrollTop ]
        | None ->
            { model with Route = route; MenuOpen = false }, scrollTop
    | ToggleMenu ->
        { model with MenuOpen = not model.MenuOpen }, Cmd.none

// -- navigation helper: real hrefs, but intercept plain left-clicks for SPA nav --

let private navigate (segs: string list) = Router.navigatePath (List.toArray segs)

let private internalLink (href: string) (segs: string list) : IReactProperty list =
    [ prop.href href
      prop.onClick (fun (e: MouseEvent) ->
          if e.button = 0 && not e.ctrlKey && not e.metaKey && not e.shiftKey && not e.altKey then
              e.preventDefault ()
              navigate segs) ]

// -- views --

let private navTree (site: GetSite.Response) (current: string) =
    let byParent =
        site.Menu
        |> List.groupBy (fun m -> m.Parent)
        |> List.map (fun (k, v) -> k, v |> List.sortBy (fun m -> m.Ordinal))
        |> Map.ofList
    let childrenOf p = Map.tryFind p byParent |> Option.defaultValue []
    let hrefFor (link: string) = if link = site.Home then "/" else "/" + link
    let segsFor (link: string) = if link = site.Home then [] else [ link ]
    let rec branch (parent: string) (depth: int) =
        match childrenOf parent with
        | [] -> Html.none
        | kids ->
            Html.ul [
                prop.children [
                    for k in kids ->
                        let hasKids = not (List.isEmpty (childrenOf k.Item))
                        Html.li [
                            prop.children [
                                Html.a (
                                    internalLink (hrefFor k.Link) (segsFor k.Link)
                                    @ [ if k.Link = current then prop.custom ("aria-current", "page")
                                        if hasKids then prop.custom ("aria-haspopup", "true")
                                        prop.children [
                                            Html.span [ prop.text k.Title ]
                                            if hasKids then
                                                Html.span [
                                                    prop.className "caret"
                                                    prop.ariaHidden true
                                                    prop.text (if depth = 0 then "▾" else "▸")
                                                ]
                                        ] ])
                                branch k.Item (depth + 1)
                            ]
                        ]
                ]
            ]
    branch "" 0

let private headerView (menuOpen: bool) dispatch =
    Html.header [
        prop.className "site"
        prop.children [
            Html.div [
                prop.className "wrap head-in"
                prop.children [
                    Html.a (
                        internalLink "/" []
                        @ [ prop.className "brand"
                            prop.children [
                                Html.img [ prop.src "/images/layout/bwhead.jpg"; prop.alt "BaseWatch" ]
                                Html.span [ prop.className "tag"; prop.text "Community watch on the US military base in Darwin" ]
                            ] ])
                    Html.button [
                        prop.className "menu-toggle"
                        prop.id "menuToggle"
                        prop.ariaExpanded menuOpen
                        prop.custom ("aria-controls", "menu")
                        prop.text "Menu"
                        prop.onClick (fun _ -> dispatch ToggleMenu)
                    ]
                ]
            ]
        ]
    ]

let private menubarView (site: GetSite.Response) (current: string) (menuOpen: bool) =
    Html.div [
        prop.className "menubar"
        prop.children [
            Html.div [
                prop.className "wrap"
                prop.children [
                    Html.nav [
                        prop.className (if menuOpen then "menu open" else "menu")
                        prop.id "menu"
                        prop.custom ("aria-label", "Site navigation")
                        prop.children [ navTree site current ]
                    ]
                ]
            ]
        ]
    ]

let private pageView (page: GetPage.PageView) =
    Html.div [
        prop.className "wrap page"
        prop.children [
            Html.h1 page.Title
            // Body is stored as rich-text (ProseMirror JSON); render it to HTML.
            Html.div [ prop.className "body hamlet-rt-viewer"; prop.dangerouslySetInnerHTML (RichText.toHtml page.Body) ]
        ]
    ]

let private notFoundView =
    Html.div [
        prop.className "wrap page"
        prop.children [
            Html.h1 "Not found"
            Html.p [
                prop.className "body"
                prop.children [
                    Html.text "That page doesn’t exist. "
                    Html.a (internalLink "/" [] @ [ prop.text "Return home" ])
                    Html.text "."
                ]
            ]
        ]
    ]

let private loadingMain =
    Html.main [
        prop.id "content"
        prop.tabIndex -1
        prop.children [ Html.div [ prop.className "wrap"; prop.children [ Html.div [ prop.className "loading"; prop.text "Loading…" ] ] ] ]
    ]

let private newsCard (it: NewsItem) =
    Html.a [
        prop.key it.Href
        prop.className "news-card"
        prop.href it.Href
        prop.children [
            match it.Image with
            | Some src -> Html.img [ prop.className "news-img"; prop.src src; prop.onError (fun (e: Browser.Types.Event) -> hideBrokenImg e) ]
            | None -> Html.none
            Html.div [
                prop.className "news-body"
                prop.children [
                    Html.h2 [ prop.className "news-title"; prop.text it.Title ]
                    if it.Teaser <> "" then Html.p [ prop.className "news-teaser"; prop.text it.Teaser ] else Html.none
                ]
            ]
        ]
    ]

/// Home page: the current news feed from usba.se. Full articles + comments live
/// there; each card links across.
let private feedView (model: Model) =
    Html.div [
        prop.className "wrap feed"
        prop.children [
            match model.Feed with
            | None -> yield Html.div [ prop.className "loading"; prop.text "Loading news…" ]
            | Some (Error _) ->
                yield Html.p [ prop.className "loading"; prop.text "News is unavailable right now — try " ]
                yield Html.a [ prop.href "https://usba.se/"; prop.text "usba.se" ]
            | Some (Ok []) -> yield Html.p [ prop.className "loading"; prop.text "No news yet." ]
            | Some (Ok items) ->
                for it in items do yield newsCard it
                yield Html.a [ prop.className "news-more"; prop.href "https://usba.se/"; prop.text "More at usba.se →" ]
        ]
    ]

let private mainView (model: Model) =
    Html.main [
        prop.id "content"
        prop.tabIndex -1
        prop.children [
            match model.Route with
            | [] -> feedView model
            | _ ->
                match model.Page with
                | PageLoading -> Html.div [ prop.className "wrap"; prop.children [ Html.div [ prop.className "loading"; prop.text "Loading…" ] ] ]
                | PageLoaded pg -> pageView pg
                | PageMissing -> notFoundView
        ]
    ]

let private footerView =
    Html.footer [
        prop.className "site"
        prop.children [
            Html.div [
                prop.className "wrap foot-in"
                prop.children [
                    Html.strong "BaseWatch"
                    Html.text " — a community response to the US military presence in Darwin. Contact "
                    Html.a [ prop.href "mailto:contact@basewatch.org"; prop.text "contact@basewatch.org" ]
                    Html.text ". "
                    Html.span [ prop.className "foot-note"; prop.children [ Html.text "News from "; Html.a [ prop.href "https://usba.se/"; prop.text "usba.se" ]; Html.text "." ] ]
                ]
            ]
        ]
    ]

let private appView (model: Model) dispatch =
    React.fragment [
        Html.a [ prop.className "skip"; prop.href "#content"; prop.text "Skip to content" ]
        match model.Error with
        | Some err -> Html.div [ prop.className "error"; prop.text err ]
        | None -> Html.none
        headerView model.MenuOpen dispatch
        match model.Site with
        | Some site -> menubarView site (currentPageName site model.Route) model.MenuOpen
        | None -> Html.none
        match model.Site with
        | Some _ -> mainView model
        | None -> loadingMain
    ]

let view model dispatch =
    React.router [
        router.pathMode
        router.onUrlChanged (routeOf >> UrlChanged >> dispatch)
        router.children [ appView model dispatch ]
    ]

open Elmish.React

Program.mkProgram init update view
|> Program.withReactSynchronous "app"
|> Program.run
