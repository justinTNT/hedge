module Articles.Client.Shell.App

// The Justat shell (unified shell, Stage 2): one path-mode router, the shell-owned
// identity authority, the Justat chrome, and BOTH content modules hosted via their
// Stage-0 hosted surfaces — articles at the root, blog at /blog — with seamless SPA
// navigation between them. NDCT keeps the standalone Articles/Main. See
// notes/UNIFIED-SHELL.md.

module A = Articles.Client.Types
module B = Blog.Client.Types
module ArtApp = Articles.Client.App
module BlogApp = Blog.Client.App
module Identity = Content.Identity   // the shared identity subsystem (Stage 3)

open Fable.Core
open Feliz
open Feliz.Router
open Elmish
open Articles.Client.Shell.Types

// -- Deployment base + the shell's single navigator. Articles is PRIMARY (mount []),
//    blog is mounted at /blog; both navigate through this one router. --

[<Emit("window.BASE_PATH || ''")>]
let private basePathGlobal : string = jsNative

let private baseSegs =
    basePathGlobal.Split('/') |> Array.filter (fun s -> s <> "") |> Array.toList

let private shellNavigate (segments: string list) =
    Router.navigatePath (List.toArray (baseSegs @ segments))

let private shellNavigateToPath (path: string) =
    shellNavigate (path.Split('/') |> Array.filter (fun s -> s <> "") |> Array.toList)

/// Articles content runs at the root; its context navigates through the shell router.
let private articlesCtx : Content.HostContext =
    { Content.HostContext.standalone shellNavigate with InstanceId = 1 }

/// Blog content is mounted at /blog; its navigation prepends that segment, and its
/// context reports the mount so hrefs/routes compose correctly.
let private blogCtx : Content.HostContext =
    { Content.HostContext.standalone (fun segments -> shellNavigate ("blog" :: segments)) with
        MountSegments = [ "blog" ]
        InstanceId = 2 }

// CP-C: the shell builds each hosted module's typed API client once (browser transport) and
// injects it, with that module's host context, into its content update path. The modules keep the
// typed Hedge.Http.ApiError through to the view.
let private articlesDeps : A.Deps =
    { Ctx = articlesCtx; Api = Articles.ClientGen.createClient Client.Api.browserTransport }
let private blogDeps : B.Deps =
    { Ctx = blogCtx; Api = Blog.ClientGen.createClient Client.Api.browserTransport }

// -- Route + OAuth-claim parsing (the shell owns routing). --

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

/// Which module a content route targets, and the module-local route within it. Whole-
/// segment mount: "blog" peels to the blog module; "blogger" is NOT a blog mount.
let private targetOf (route: string list) : ModuleId * string list =
    match route with
    | "blog" :: rest -> Blog, rest
    | _ -> Articles, route

// -- Activation-tagged command mapping: child commands carry the activation they were
//    issued under, so a late read for a route we've left is recognised as stale. --

let private mapArticles (activation: int) (cmd: Cmd<A.Msg>) : Cmd<Msg> =
    cmd |> Cmd.map (fun m -> ArticlesMsg (activation, m))

let private mapBlog (activation: int) (cmd: Cmd<B.Msg>) : Cmd<Msg> =
    cmd |> Cmd.map (fun m -> BlogMsg (activation, m))

// C1: the shell no longer enumerates child result messages to decide staleness. Each
// content module now validates every read/event/write against its own current target
// (route / shown item / query) inside its update — a stale result cannot apply to the wrong
// view by construction, whatever the message type, and a mutation FAILURE still surfaces
// (the child keeps its Error). The shell just forwards messages to the retained child.
// (`Activation` remains, but only as the LeaveCompleted transition barrier, not a filter.)

let init () : Model * Cmd<Msg> =
    let route = Content.HostContext.routeOf articlesCtx (currentSegments ())
    match route with
    | ClaimRoute ->
        // OAuth return handled entirely in the shell now (both modules hosted): load
        // identities and SPA-navigate back; the switcher opens via PendingClaimFocus.
        let claimFocus, returnTo = parseClaimFromRoute ()
        let idModel, idCmd = Identity.init claimFocus
        { Active = Articles; Route = []; Activation = 0; Pending = None
          Articles = ArtApp.emptyHosted idModel.GuestSession; Blog = None; Identity = idModel },
        Cmd.batch [ Cmd.map IdentityMsg idCmd; Cmd.ofEffect (fun _ -> shellNavigateToPath returnTo) ]
    | _ ->
        let idModel, idCmd = Identity.init None
        match targetOf route with
        | Articles, subRoute ->
            let articles, cmd = ArtApp.enterHosted articlesCtx subRoute (ArtApp.emptyHosted idModel.GuestSession)
            { Active = Articles; Route = subRoute; Activation = 0; Pending = None
              Articles = articles; Blog = None; Identity = idModel },
            Cmd.batch [ Cmd.map IdentityMsg idCmd; mapArticles 0 cmd ]
        | Blog, subRoute ->
            let blog, cmd = BlogApp.enterHosted blogCtx subRoute (BlogApp.emptyHosted idModel.GuestSession)
            { Active = Blog; Route = subRoute; Activation = 0; Pending = None
              Articles = ArtApp.emptyHosted idModel.GuestSession; Blog = Some blog; Identity = idModel },
            Cmd.batch [ Cmd.map IdentityMsg idCmd; mapBlog 0 cmd ]

let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | UrlChanged segments ->
        match Content.HostContext.routeOf articlesCtx segments with
        | ClaimRoute ->
            let claimFocus, returnTo = parseClaimFromRoute ()
            { model with Identity = { model.Identity with PendingClaimFocus = claimFocus } },
            Cmd.batch [
                Cmd.map IdentityMsg Identity.loadIdentitiesCmd
                Cmd.ofEffect (fun _ -> shellNavigateToPath returnTo)
            ]
        | route ->
            // Navigation: bump the activation (invalidating the outgoing one), dispose
            // the outgoing module's live resources synchronously, THEN enter the incoming
            // route via LeaveCompleted. A newer navigation supersedes a pending one.
            let target, subRoute = targetOf route
            let newAct = model.Activation + 1
            let idModel = Identity.consumeClaimFocus model.Identity
            // CP-A finding 1: invalidate the OUTGOING module's in-flight reads (bump its LoadGen,
            // clear its loading flags) so a read that completes after we've left is dropped by the
            // child rather than reopening its socket / rewriting the tab title. disposeHosted below
            // tears down its live resources; this stops the pending fetch from resurrecting them.
            let model' =
                match model.Active with
                | Articles -> { model with Articles = ArtApp.invalidateInFlight model.Articles }
                | Blog -> { model with Blog = model.Blog |> Option.map BlogApp.invalidateInFlight }
            let disposeOutgoing () =
                match model.Active with
                | Articles -> ArtApp.disposeHosted ()
                | Blog -> BlogApp.disposeHosted ()
            { model' with Activation = newAct; Pending = Some (newAct, target, subRoute); Identity = idModel },
            Cmd.ofEffect (fun dispatch ->
                disposeOutgoing ()
                dispatch (LeaveCompleted newAct))

    | LeaveCompleted t ->
        match model.Pending with
        | Some (pid, target, subRoute) when pid = t && t = model.Activation ->
            match target with
            | Articles ->
                let articles, cmd = ArtApp.enterHosted articlesCtx subRoute model.Articles
                { model with Active = Articles; Route = subRoute; Pending = None; Articles = articles },
                mapArticles model.Activation cmd
            | Blog ->
                // Create the blog child lazily on first visit, seeded from the current session.
                let blog0 = model.Blog |> Option.defaultValue (BlogApp.emptyHosted model.Identity.GuestSession)
                let blog, cmd = BlogApp.enterHosted blogCtx subRoute blog0
                { model with Active = Blog; Route = subRoute; Pending = None; Blog = Some blog },
                mapBlog model.Activation cmd
        | _ ->
            model, Cmd.none   // superseded by a newer navigation

    | ArticlesMsg (_act, m) ->
        // Forward to the retained child; it validates the result against its own target.
        let articles, cmd = ArtApp.updateHosted articlesDeps m model.Articles
        { model with Articles = articles }, mapArticles model.Activation cmd

    | BlogMsg (_act, m) ->
        match model.Blog with
        | None -> model, Cmd.none
        | Some blog0 ->
            let blog, cmd = BlogApp.updateHosted blogDeps m blog0
            { model with Blog = Some blog }, mapBlog model.Activation cmd

    | IdentityMsg m ->
        let idModel, idCmd, signal = Identity.update m model.Identity
        let baseModel = { model with Identity = idModel }
        let model', extraCmd =
            match signal with
            | Identity.SessionChanged session ->
                // Fan the authoritative session snapshot into BOTH retained children.
                { baseModel with
                    Articles = ArtApp.withSession session baseModel.Articles
                    Blog = baseModel.Blog |> Option.map (BlogApp.withSession session) }, Cmd.none
            | Identity.ReloadContent ->
                // C1: attribution changed globally, so invalidate BOTH modules' content
                // caches first — then re-enter the ACTIVE route (now a real refetch, not a
                // cache reuse), while the INACTIVE module refetches on its next entry rather
                // than silently showing a feed/item with obsolete authorship.
                let articles' = ArtApp.invalidateContent baseModel.Articles
                let blog' = baseModel.Blog |> Option.map BlogApp.invalidateContent
                match baseModel.Active with
                | Articles ->
                    let articles, cmd = ArtApp.enterHosted articlesCtx baseModel.Route articles'
                    { baseModel with Articles = articles; Blog = blog' }, mapArticles baseModel.Activation cmd
                | Blog ->
                    match blog' with
                    | Some blog0 ->
                        let blog, cmd = BlogApp.enterHosted blogCtx baseModel.Route blog0
                        { baseModel with Blog = Some blog; Articles = articles' }, mapBlog baseModel.Activation cmd
                    | None -> { baseModel with Articles = articles'; Blog = blog' }, Cmd.none
            | Identity.Failed err ->
                // Surface the failure in the active module's error area. CP-C: the content models'
                // Error is a typed ApiError; wrap the identity subsystem's string failure.
                let apiErr = Hedge.Http.HttpFailure (0, err)
                match baseModel.Active with
                | Articles -> { baseModel with Articles = { baseModel.Articles with Error = Some apiErr } }, Cmd.none
                | Blog ->
                    match baseModel.Blog with
                    | Some blog0 -> { baseModel with Blog = Some { blog0 with Error = Some apiErr } }, Cmd.none
                    | None -> baseModel, Cmd.none
            | Identity.NoSignal ->
                baseModel, Cmd.none
        model', Cmd.batch [ Cmd.map IdentityMsg idCmd; extraCmd ]

let view (model: Model) (dispatch: Msg -> unit) =
    let content =
        match model.Active with
        | Articles ->
            ArtApp.contentView articlesCtx model.Articles (fun m -> dispatch (ArticlesMsg (model.Activation, m)))
        | Blog ->
            match model.Blog with
            | Some blog -> BlogApp.contentView blogCtx blog (fun m -> dispatch (BlogMsg (model.Activation, m)))
            | None -> Html.none
    React.router [
        router.pathMode
        router.onUrlChanged (fun segments -> dispatch (UrlChanged segments))
        router.children [
            Chrome.shell
                model.Identity
                (IdentityMsg >> dispatch)
                (fun () -> shellNavigate [])          // Home
                (fun () -> shellNavigate [ "blog" ])  // Web Log (in-SPA now)
                (model.Active = Articles)             // Justat sidebar on articles routes only
                content
        ]
    ]
