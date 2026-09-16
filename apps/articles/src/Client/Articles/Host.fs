module Articles.Client.Host.App

// ndct's single-module host (CP-B convergence). articles is this app's PRIMARY module; on ndct
// it runs standalone (ndct composes articles only — no shell, no blog). This host owns the one
// router, the shared identity authority (Content.Identity), and ndct's chrome, and drives the
// articles content module purely through its Stage-0 hosted surface. It mirrors the Justat shell
// minus the second module: same-module navigation goes straight through enterHosted (which
// disposes the outgoing route's resources and bumps the read generation), so no
// activation/LeaveCompleted barrier is needed. Justat keeps using the Shell (Shell/App.fs) — it
// can't reuse this host, and this host can't reuse the shell, because the shell opens
// Blog.Client.Types and ndct omits the blog module.

module Shared = Articles.Client.Shared
module ArtApp = Articles.Client.App
module Identity = Content.Identity

open Fable.Core
open Feliz
open Feliz.Router
open Elmish

let private ctx : Content.HostContext = Content.HostContext.standalone Shared.navigateTo

// CP-C: the host constructs the typed API client once (browser transport) and injects it, with
// the host context, into the content update path. The module keeps the typed Hedge.Http.ApiError.
let private api = Articles.ClientGen.createClient Client.Api.browserTransport
let private deps : Articles.Client.Types.Deps = { Ctx = ctx; Api = api }

type Model =
    { Route: string list
      Content: Articles.Client.Types.Model
      Identity: Identity.Model }

type Msg =
    | UrlChanged of string list
    | ContentMsg of Articles.Client.Types.Msg
    | IdentityMsg of Identity.Msg

[<Emit("new URLSearchParams(window.location.search).get($0)")>]
let private getQueryParam (name: string) : string = jsNative

/// OAuth return: /auth/claim?identity=...&returnTo=...
let private parseClaimFromRoute () : string option * string =
    let identity = getQueryParam "identity"
    let returnTo = getQueryParam "returnTo"
    let identity = if isNull identity || identity = "" then None else Some identity
    let returnTo = if isNull returnTo || returnTo = "" then "/" else returnTo
    identity, returnTo

let private (|ClaimRoute|_|) route =
    match route with
    | [ "auth"; "claim" ] | [ "auth"; "claim"; _ ] -> Some ()
    | _ -> None

let init () : Model * Cmd<Msg> =
    let route = Shared.routeOf (Router.currentUrl ())
    match route with
    | ClaimRoute ->
        let claimFocus, returnTo = parseClaimFromRoute ()
        let idModel, idCmd = Identity.init claimFocus
        { Route = []; Content = ArtApp.emptyHosted idModel.GuestSession; Identity = idModel },
        Cmd.batch [ Cmd.map IdentityMsg idCmd; Cmd.ofEffect (fun _ -> Shared.navigateToPath returnTo) ]
    | _ ->
        let idModel, idCmd = Identity.init None
        let content, cmd = ArtApp.enterHosted ctx route (ArtApp.emptyHosted idModel.GuestSession)
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
                Cmd.ofEffect (fun _ -> Shared.navigateToPath returnTo)
            ]
        | _ ->
            let idModel = Identity.consumeClaimFocus model.Identity
            let content, cmd = ArtApp.enterHosted ctx route model.Content
            { model with Route = route; Content = content; Identity = idModel },
            Cmd.map ContentMsg cmd

    | ContentMsg m ->
        let content, cmd = ArtApp.updateHosted deps m model.Content
        { model with Content = content }, Cmd.map ContentMsg cmd

    | IdentityMsg m ->
        let idModel, idCmd, signal = Identity.update m model.Identity
        let baseModel = { model with Identity = idModel }
        let model', extraCmd =
            match signal with
            | Identity.SessionChanged session ->
                { baseModel with Content = ArtApp.withSession session baseModel.Content }, Cmd.none
            | Identity.ReloadContent ->
                let content' = ArtApp.invalidateContent baseModel.Content
                let content, cmd = ArtApp.enterHosted ctx baseModel.Route content'
                { baseModel with Content = content }, Cmd.map ContentMsg cmd
            | Identity.Failed err ->
                // CP-C: the content model's Error is a typed ApiError; wrap the identity
                // subsystem's string failure so one error type flows to the view.
                { baseModel with Content = { baseModel.Content with Error = Some (Hedge.Http.HttpFailure (0, err)) } }, Cmd.none
            | Identity.NoSignal ->
                baseModel, Cmd.none
        model', Cmd.batch [ Cmd.map IdentityMsg idCmd; extraCmd ]

let view (model: Model) (dispatch: Msg -> unit) =
    let content = ArtApp.contentView ctx model.Content (ContentMsg >> dispatch)
    // ndct's hero shows on the home feed only. App-level (not slug-branched in the shared module).
    let showHero = List.isEmpty model.Route && Hedge.Tenant.config.Slug = "ndct"
    React.router [
        router.pathMode
        router.onUrlChanged (Shared.routeOf >> UrlChanged >> dispatch)
        router.children [
            Chrome.frame
                model.Identity
                (IdentityMsg >> dispatch)
                (fun () -> Shared.navigateTo [])   // Home (articles is primary — the module root)
                showHero
                content
        ]
    ]
