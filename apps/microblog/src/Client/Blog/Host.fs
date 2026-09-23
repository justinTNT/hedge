module Blog.Client.Host.App

// darwin.news's single-module host (CP-B convergence). The blog module is this app's PRIMARY
// module; this host owns the one router, the shared identity authority (Content.Identity), and
// the darwin.news chrome, and drives the blog content module purely through its Stage-0 hosted
// surface (enterHosted/updateHosted/withSession/invalidateContent/contentView + ordered
// disposal). It mirrors the Justat shell, minus the second module and cross-module switching:
// same-module navigation goes straight through enterHosted (which disposes the outgoing route's
// resources and bumps the read generation), so no activation/LeaveCompleted barrier is needed.

module Shared = Blog.Client.Shared
module BlogApp = Blog.Client.App
module Identity = Content.Identity

open Fable.Core
open Feliz
open Feliz.Router
open Elmish
open Content.ClaimGlue   // shared OAuth claim-return glue (parseClaimFromRoute / ClaimRoute / returnNavCmd)

// This host's context for the hosted blog module: routing via the module's Feliz.Router-backed
// navigator, title set directly. Blog is primary (MOUNT_BASE=""), so it routes at the root.
let private ctx : Content.HostContext = Content.HostContext.standalone Shared.navigateTo

// CP-C: the host constructs the typed API client once (browser transport) and injects it, with
// the host context, into the content update path. The module keeps the typed Hedge.Http.ApiError.
let private api = Blog.ClientGen.createClient Client.Api.browserTransport
let private deps : Blog.Client.Types.Deps = { Ctx = ctx; Api = api }

type Model =
    { /// The blog module's own content route.
      Route: string list
      /// The hosted blog content model.
      Content: Blog.Client.Types.Model
      /// The single, host-owned identity authority.
      Identity: Identity.Model }

type Msg =
    | UrlChanged of string list
    | ContentMsg of Blog.Client.Types.Msg
    | IdentityMsg of Identity.Msg

let init () : Model * Cmd<Msg> =
    let route = Shared.routeOf (Router.currentUrl ())
    match route with
    | ClaimRoute ->
        // OAuth return handled by the host: boot identity with the claim focus and SPA-navigate
        // back; the switcher opens pre-selected once we land (consumeClaimFocus).
        let claimFocus, returnTo = parseClaimFromRoute ()
        let idModel, idCmd = Identity.init claimFocus
        { Route = []; Content = BlogApp.emptyHosted idModel.GuestSession; Identity = idModel },
        Cmd.batch [ Cmd.map IdentityMsg idCmd; returnNavCmd Shared.navigateToPath returnTo ]
    | _ ->
        let idModel, idCmd = Identity.init None
        let content, cmd = BlogApp.enterHosted ctx route (BlogApp.emptyHosted idModel.GuestSession)
        { Route = route; Content = content; Identity = idModel },
        Cmd.batch [ Cmd.map IdentityMsg idCmd; Cmd.map ContentMsg cmd ]

let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | UrlChanged route ->
        match route with
        | ClaimRoute ->
            let claimFocus, returnTo = parseClaimFromRoute ()
            { model with Identity = { model.Identity with PendingClaimFocus = claimFocus } },
            Cmd.batch [
                Cmd.map IdentityMsg Identity.loadIdentitiesCmd
                returnNavCmd Shared.navigateToPath returnTo
            ]
        | _ ->
            // enterHosted disposes the outgoing route's resources and bumps the read generation,
            // so a late read from the route we're leaving is dropped (CP-A). consumeClaimFocus
            // opens the switcher if we've just returned from an OAuth claim.
            let idModel = Identity.consumeClaimFocus model.Identity
            let content, cmd = BlogApp.enterHosted ctx route model.Content
            { model with Route = route; Content = content; Identity = idModel },
            Cmd.map ContentMsg cmd

    | ContentMsg m ->
        let content, cmd = BlogApp.updateHosted deps m model.Content
        { model with Content = content }, Cmd.map ContentMsg cmd

    | IdentityMsg m ->
        let idModel, idCmd, signal = Identity.update m model.Identity
        let baseModel = { model with Identity = idModel }
        let model', extraCmd =
            match signal with
            | Identity.SessionChanged session ->
                { baseModel with Content = BlogApp.withSession session baseModel.Content }, Cmd.none
            | Identity.ReloadContent ->
                // Attribution changed globally: invalidate the cached content, then re-enter the
                // current route (a real refetch, not a cache reuse).
                let content' = BlogApp.invalidateContent baseModel.Content
                let content, cmd = BlogApp.enterHosted ctx baseModel.Route content'
                { baseModel with Content = content }, Cmd.map ContentMsg cmd
            | Identity.Failed err ->
                // CP-C: the content model's Error is a typed ApiError; wrap the identity
                // subsystem's string failure so one error type flows to the view.
                { baseModel with Content = { baseModel.Content with Error = Some (Hedge.Http.HttpFailure (0, err)) } }, Cmd.none
            | Identity.NoSignal ->
                baseModel, Cmd.none
        model', Cmd.batch [ Cmd.map IdentityMsg idCmd; extraCmd ]

let view (model: Model) (dispatch: Msg -> unit) =
    let content = BlogApp.contentView ctx model.Content (ContentMsg >> dispatch)
    React.router [
        router.pathMode
        router.onUrlChanged (Shared.routeOf >> UrlChanged >> dispatch)
        router.children [
            Chrome.frame
                model.Identity
                (IdentityMsg >> dispatch)
                (fun () -> Shared.navigateTo [])   // Home (blog is primary — the module root)
                content
        ]
    ]
