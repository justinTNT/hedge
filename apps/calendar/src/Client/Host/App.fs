module Calendar.Client.Host.App

// This app's single-module host. The calendar module is PRIMARY; this host owns the one router, the
// shared identity authority (Content.Identity), and the chrome, and drives the calendar content
// module purely through its hosted surface (enterHosted / updateHosted / withSession /
// invalidateContent / contentView). Mirrors the articles (ndct) and microblog hosts.

module CalApp = Calendar.Client.App
module Identity = Content.Identity

open Feliz
open Feliz.Router
open Elmish
open Content.ClaimGlue   // shared OAuth claim-return glue (parseClaimFromRoute / ClaimRoute / returnNavCmd)

// Calendar is primary (no MOUNT_BASE), so it routes at the deployment root.
let private ctx : Content.HostContext = Content.HostContext.standalone Chrome.navigateTo

// The host constructs the typed API client once and injects it, with the host context, into the content
// update path. appTransport is the browser transport on web (and the native/bearer transport in a bundled
// mobile build); reads and writes both carry the session cookie.
let private api = Calendar.ClientGen.createClient Client.Api.appTransport
let private deps : Calendar.Client.Types.Deps = { Ctx = ctx; Api = api }

type Model =
    { Route: string list
      Content: Calendar.Client.Types.Model
      Identity: Identity.Model }

type Msg =
    | UrlChanged of string list
    | ContentMsg of Calendar.Client.Types.Msg
    | IdentityMsg of Identity.Msg

let init () : Model * Cmd<Msg> =
    let route = Chrome.routeOf (Router.currentUrl ())
    match route with
    | ClaimRoute ->
        let claimFocus, returnTo = parseClaimFromRoute ()
        let idModel, idCmd = Identity.init claimFocus
        { Route = []; Content = CalApp.emptyHosted idModel.GuestSession; Identity = idModel },
        Cmd.batch [ Cmd.map IdentityMsg idCmd; returnNavCmd Chrome.navigateToPath returnTo ]
    | _ ->
        let idModel, idCmd = Identity.init None
        let content, cmd = CalApp.enterHosted ctx route (CalApp.emptyHosted idModel.GuestSession)
        { Route = route; Content = content; Identity = idModel },
        Cmd.batch [ Cmd.map IdentityMsg idCmd; Cmd.map ContentMsg cmd ]

let private reload (model: Model) : Model * Cmd<Msg> =
    let content' = CalApp.invalidateContent model.Content
    let content, cmd = CalApp.enterHosted ctx model.Route content'
    { model with Content = content }, Cmd.map ContentMsg cmd

let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | UrlChanged route ->
        match route with
        | ClaimRoute ->
            let claimFocus, returnTo = parseClaimFromRoute ()
            { model with Identity = { model.Identity with PendingClaimFocus = claimFocus } },
            Cmd.batch [
                Cmd.map IdentityMsg Identity.loadIdentitiesCmd
                returnNavCmd Chrome.navigateToPath returnTo
            ]
        | _ ->
            let idModel = Identity.consumeClaimFocus model.Identity
            let content, cmd = CalApp.enterHosted ctx route model.Content
            { model with Route = route; Content = content; Identity = idModel },
            Cmd.map ContentMsg cmd

    | ContentMsg m ->
        let content, cmd = CalApp.updateHosted deps m model.Content
        { model with Content = content }, Cmd.map ContentMsg cmd

    | IdentityMsg m ->
        let idModel, idCmd, signal = Identity.update m model.Identity
        let baseModel = { model with Identity = idModel }
        let model', extraCmd =
            match signal with
            | Identity.SessionChanged session ->
                { baseModel with Content = CalApp.withSession session baseModel.Content }, Cmd.none
            | Identity.ReloadContent ->
                // A switch/merge/disconnect changes which personal entries are visible: re-read the window.
                reload baseModel
            | Identity.Failed err ->
                { baseModel with Content = { baseModel.Content with Error = Some err } }, Cmd.none
            | Identity.NoSignal ->
                baseModel, Cmd.none
        model', Cmd.batch [ Cmd.map IdentityMsg idCmd; extraCmd ]

let view (model: Model) (dispatch: Msg -> unit) =
    let content = CalApp.contentView ctx model.Content (ContentMsg >> dispatch)
    React.router [
        router.pathMode
        router.onUrlChanged (Chrome.routeOf >> UrlChanged >> dispatch)
        router.children [ Chrome.frame model.Identity (IdentityMsg >> dispatch) content ]
    ]
