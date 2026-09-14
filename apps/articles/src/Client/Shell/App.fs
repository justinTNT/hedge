module Articles.Client.Shell.App

// The Justat shell (unified shell, Stage 1): one path-mode router, the shell-owned
// identity authority, the Justat chrome, and the articles content module hosted via its
// Stage-0 hosted surface. NDCT keeps the standalone Articles/Main; blog keeps its own
// /blog bundle. See notes/UNIFIED-SHELL.md.

module A = Articles.Client.Types
module Articles = Articles.Client.App

open Fable.Core
open Feliz
open Feliz.Router
open Elmish
open Articles.Client.Shell.Types

// -- The hosted articles instance's context: articles is PRIMARY on Justat (mount []),
//    routing based at the deployment base, navigation via the shell's single router. --

[<Emit("window.BASE_PATH || ''")>]
let private basePathGlobal : string = jsNative

let private baseSegs =
    basePathGlobal.Split('/') |> Array.filter (fun s -> s <> "") |> Array.toList

let private shellNavigate (segments: string list) =
    Router.navigatePath (List.toArray (baseSegs @ segments))

let private shellNavigateToPath (path: string) =
    shellNavigate (path.Split('/') |> Array.filter (fun s -> s <> "") |> Array.toList)

/// The articles content module runs at the root here, so its context mirrors the
/// standalone default (mount []), but navigates through the shell router.
let private ctx : Content.HostContext =
    { Content.HostContext.standalone shellNavigate with InstanceId = 1 }

// -- Route + OAuth-claim parsing (the shell owns routing; content routes are stripped
//    to module-local segments, claim routes are handled here, not by the child). --

[<Emit("window.location.pathname")>]
let private locationPathname : string = jsNative

let private currentSegments () =
    locationPathname.Split('/') |> Array.filter (fun s -> s <> "") |> Array.toList

[<Emit("new URLSearchParams(window.location.search).get($0)")>]
let private getQueryParam (name: string) : string = jsNative

/// OAuth return: /auth/claim?identity=...&returnTo=...
let private parseClaimFromRoute () : (string option * string) =
    let identity = getQueryParam "identity"
    let returnTo = getQueryParam "returnTo"
    let identity = if isNull identity || identity = "" then None else Some identity
    let returnTo = if isNull returnTo || returnTo = "" then "/" else returnTo
    identity, returnTo

let private (|ClaimRoute|_|) route =
    match route with
    | [ "auth"; "claim" ] | [ "auth"; "claim"; _ ] -> Some ()
    | _ -> None

[<Emit("window.location.assign($0)")>]
let private documentNavigate (url: string) : unit = jsNative

/// Is this OAuth return target the separately-bundled blog document (not an articles
/// route the shell can host)? Checks the content route's leading segment.
let private isBlogDestination (returnTo: string) =
    match Content.HostContext.routeOf ctx (returnTo.Split('/') |> Array.filter (fun s -> s <> "") |> Array.toList) with
    | "blog" :: _ -> true
    | _ -> false

/// The navigation effect for an OAuth return. A BLOG destination can't be SPA-routed
/// into the articles shell (it's a separate bundle), so hand the claimed identity to it
/// (one-use, per-tab) and DOCUMENT-navigate; the blog bundle opens the switcher on
/// arrival. An articles destination navigates in-SPA; the switcher opens via
/// PendingClaimFocus once the route lands.
let private claimEffect (guestId: string) (claimFocus: string option) (returnTo: string) : Cmd<Msg> =
    if isBlogDestination returnTo then
        Cmd.ofEffect (fun _ ->
            (match claimFocus with
             | Some identityId -> Content.ClaimHandoff.write returnTo identityId guestId
             | None -> ())
            documentNavigate returnTo)
    else
        Cmd.ofEffect (fun _ -> shellNavigateToPath returnTo)

let init () : Model * Cmd<Msg> =
    let route = Content.HostContext.routeOf ctx (currentSegments ())
    match route with
    | ClaimRoute ->
        // Handle the OAuth return once, in the shell, then navigate back to the return
        // route — in-SPA for an articles destination (the switcher opens via
        // PendingClaimFocus on arrival), or as a document navigation with an identity
        // handoff for a blog destination. The articles child stays idle until then.
        let claimFocus, returnTo = parseClaimFromRoute ()
        let idModel, idCmd = Identity.init claimFocus
        { Route = route; Articles = Articles.emptyHosted idModel.GuestSession; Identity = idModel },
        Cmd.batch [
            Cmd.map IdentityMsg idCmd
            claimEffect idModel.GuestSession.GuestId claimFocus returnTo
        ]
    | _ ->
        let idModel, idCmd = Identity.init None
        let articles, aCmd = Articles.enterHosted ctx route (Articles.emptyHosted idModel.GuestSession)
        { Route = route; Articles = articles; Identity = idModel },
        Cmd.batch [ Cmd.map IdentityMsg idCmd; Cmd.map ArticlesMsg aCmd ]

/// A content read that targets a route we have already left. The shell drops these so a
/// late article/feed response cannot replace the active content or title (the A -> B
/// navigation that resolves B then A). The route is the request identity here; full
/// per-request / activation IDs arrive with Stage 2.
let private isStaleRead (route: string list) (msg: A.Msg) : bool =
    match msg with
    | A.GotItem (Ok response) ->
        match route with
        | [ idOrSlug ] -> not (response.Post.Id = idOrSlug || response.Post.Slug = Some idOrSlug)
        | _ -> true
    | A.GotItem (Error _) ->
        (match route with [ _ ] -> false | _ -> true)
    | A.GotFeed _ | A.GotMoreFeed _ ->
        (match route with [] -> false | _ -> true)
    | _ -> false

let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | UrlChanged segments ->
        match Content.HostContext.routeOf ctx segments with
        | ClaimRoute as route ->
            let claimFocus, returnTo = parseClaimFromRoute ()
            if isBlogDestination returnTo then
                // Leaving to the blog document — hand off the claim and navigate; no local
                // switcher focus (this document is being replaced).
                model, claimEffect model.Identity.GuestSession.GuestId claimFocus returnTo
            else
                { model with Route = route; Identity = { model.Identity with PendingClaimFocus = claimFocus } },
                Cmd.batch [
                    Cmd.map IdentityMsg Identity.loadIdentitiesCmd
                    claimEffect model.Identity.GuestSession.GuestId claimFocus returnTo
                ]
        | route ->
            // Consume any pending claim focus (opens the switcher pre-selected); an
            // ordinary navigation otherwise closes the switcher, matching the modules.
            let idModel = Identity.consumeClaimFocus model.Identity
            let articles, aCmd = Articles.enterHosted ctx route model.Articles
            { model with Route = route; Articles = articles; Identity = idModel },
            Cmd.map ArticlesMsg aCmd

    | ArticlesMsg m when isStaleRead model.Route m ->
        // Obsolete read for a route we've left — ignore it (no content/title change).
        model, Cmd.none

    | ArticlesMsg m ->
        let articles, aCmd = Articles.updateHosted ctx m model.Articles
        { model with Articles = articles }, Cmd.map ArticlesMsg aCmd

    | IdentityMsg m ->
        let idModel, idCmd, signal = Identity.update m model.Identity
        let baseModel = { model with Identity = idModel }
        let model', extraCmd =
            match signal with
            | Identity.SessionChanged session ->
                // Push the authoritative session snapshot into the hosted child.
                { baseModel with Articles = Articles.withSession session baseModel.Articles }, Cmd.none
            | Identity.ReloadContent ->
                // A merge/disconnect re-attributed content server-side — reload the route.
                let articles, aCmd = Articles.enterHosted ctx model.Route baseModel.Articles
                { baseModel with Articles = articles }, Cmd.map ArticlesMsg aCmd
            | Identity.Failed err ->
                // Surface the failure where the content module already renders errors.
                { baseModel with Articles = { baseModel.Articles with Error = Some err } }, Cmd.none
            | Identity.NoSignal ->
                baseModel, Cmd.none
        model', Cmd.batch [ Cmd.map IdentityMsg idCmd; extraCmd ]

let view (model: Model) (dispatch: Msg -> unit) =
    let content = Articles.contentView ctx model.Articles (ArticlesMsg >> dispatch)
    React.router [
        router.pathMode
        router.onUrlChanged (fun segments -> dispatch (UrlChanged segments))
        router.children [
            Chrome.shell model.Identity (IdentityMsg >> dispatch) (fun () -> shellNavigate []) content
        ]
    ]
