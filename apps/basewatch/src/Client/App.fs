module Client.App

open Feliz
open Feliz.Router
open Elmish
open Browser.Types
open Browser.Dom
open Models.Api

// -- model --

type PageState =
    | PageLoading
    | PageLoaded of GetPage.PageView
    | PageMissing

type Model = {
    Site: GetSite.Response option
    Route: string list
    Page: PageState
    MenuOpen: bool
    Error: string option
}

type Msg =
    | GotSite of Result<GetSite.Response, string>
    | GotPage of string * Result<GetPage.Response, string>
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

let private loadPageCmd (site: GetSite.Response) (route: string list) : Cmd<Msg> =
    let name = currentPageName site route
    Cmd.OfPromise.either Client.ClientGen.getPage name
        (fun r -> GotPage(name, r))
        (fun ex -> GotPage(name, Error ex.Message))

let init () =
    { Site = None; Route = routeOf (Router.currentUrl ()); Page = PageLoading; MenuOpen = false; Error = None },
    Cmd.OfPromise.either Client.ClientGen.getSite () GotSite (fun ex -> GotSite(Error ex.Message))

let private setTitle (t: string) = Cmd.ofEffect (fun _ -> document.title <- t + " — BaseWatch")
let private scrollTop = Cmd.ofEffect (fun _ -> window.scrollTo (0.0, 0.0))

let update msg model =
    match msg with
    | GotSite (Ok site) ->
        let m = { model with Site = Some site }
        m, loadPageCmd site m.Route
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
    | UrlChanged route ->
        match model.Site with
        | Some site ->
            { model with Route = route; Page = PageLoading; MenuOpen = false },
            Cmd.batch [ loadPageCmd site route; scrollTop ]
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
            Html.div [ prop.className "body"; prop.dangerouslySetInnerHTML page.Body ]
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

let private mainView (model: Model) =
    Html.main [
        prop.id "content"
        prop.tabIndex -1
        prop.children [
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
                    Html.span [ prop.className "foot-note"; prop.text "Archived site; content preserved from the original." ]
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
