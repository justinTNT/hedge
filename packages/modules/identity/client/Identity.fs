namespace Content

// Shared identity subsystem (unified shell, Stage 3 — convergence). The single identity
// authority a host owns per document: cached guest state now, one sync + one providers
// load at boot, and the switcher/claim/revert/disconnect behaviour. Extracted from the
// (proven) Justat shell copy into ordinary shared client code so every host — the Justat
// shell now, the ndct + microblog standalone hosts as they migrate — consumes ONE copy,
// and the per-module identity duplication can then be deleted.
//
// Lives in the identity-owned Identity.Client library (packages/modules/identity/client), referenced by
// each consuming Client project. It builds on the generated IdentityHttp client (the typed /api/auth/*
// contract) and the Hedge.Client runtime (Client.GuestSession/Client.Api). A packaged reusable library,
// not part of the core Hedge framework assembly and no longer part of the generic ContentClient.

open Fable.Core.JsInterop
open Elmish
open Thoth.Json

module GuestSession = Client.GuestSession

module Identity =

    type IdentityListItem =
        { Id: string
          Provider: string
          Name: string
          Picture: string
          ActivatedAt: int option }

    /// State of the passwordless email magic-link request (web only).
    type MagicLinkState =
        | MlIdle
        | MlSending
        | MlSent
        | MlFailed of string

    type Model =
        { GuestSession: GuestSession.GuestSessionData
          Identities: IdentityListItem list
          /// Providers the server has credentials for — the connections pane offers only
          /// these, so an unconfigured provider is never a dead button.
          AvailableProviders: string list
          ShowIdentitySwitcher: bool
          /// Identity id awaiting a merge/fresh decision in the switcher.
          SelectedIdentity: string option
          /// Set on OAuth return; consumed once the host navigates back to the return
          /// route, to open the switcher pre-selected on the claimed identity.
          PendingClaimFocus: string option
          /// Passwordless email magic-link: the input value + request state (web only).
          EmailInput: string
          MagicLink: MagicLinkState }

    type Msg =
        | GotSessionSync of GuestSession.GuestSessionData
        | RevertIdentity of identityId: string * merge: bool
        | GotRevertIdentity of Result<unit, string>
        | LoadIdentities
        | GotIdentities of IdentityListItem list
        | GotProviders of string list
        | ToggleIdentitySwitcher
        | DisconnectIdentity of identityId: string
        | GotDisconnect of Result<unit, string>
        | SelectIdentity of identityId: string
        | SetEmailInput of string
        | RequestMagicLink
        | GotMagicLink of Result<unit, string>

    /// What an identity update means for the rest of the host.
    type Signal =
        | NoSignal
        /// The authoritative session changed — the host pushes it into the child model(s).
        | SessionChanged of GuestSession.GuestSessionData
        /// A merge/disconnect re-attributed content server-side — the host reloads the
        /// current route so displayed authorship is refreshed.
        | ReloadContent
        /// An identity operation failed — the host surfaces the message to the user.
        | Failed of string

    // -- Commands. The identity lifecycle endpoints (revert / disconnect / list) go through the generated,
    //    typed IdentityHttp client below: request records + response decoding come from the one contract
    //    that also drives the server, so the wire shape can't drift unnoticed and user-controlled values
    //    (e.g. a display name) are escaped by the codec. The framework endpoints (providers, email) remain
    //    hand-written. The wire is unchanged: the same JSON keys and shapes as before. --

    /// The typed identity client (IdentityHttp.ClientGen) — the single source of truth for the revert /
    /// disconnect / list wire shape, replacing the hand-written encoders + endpoint strings + response
    /// decoder. Driven by `appTransport`, which resolves exactly as the old direct helpers' `reqBase` +
    /// `authHeaderList` did: on web, basePath + the browser's cookie; in the Capacitor WebView (this switcher
    /// renders there too), the absolute API origin + the bearer. (NOT browserTransport — that would drop the
    /// bearer and hit the wrong origin in the mobile app.)
    let private api = IdentityHttp.ClientGen.createClient Client.Api.appTransport

    let private apiErrorText (e: Hedge.Http.ApiError) : string =
        match e with
        | Hedge.Http.TransportFailure m -> m
        | Hedge.Http.HttpFailure (_, m) -> m
        | Hedge.Http.DecodeFailure m -> m
        | Hedge.Http.ValidationFailure (_, errs) -> errs |> List.map (fun v -> v.Message) |> String.concat "; "

    let private toUnitResult (r: Result<'a, Hedge.Http.ApiError>) : Result<unit, string> =
        match r with Ok _ -> Ok () | Error e -> Error (apiErrorText e)

    // Framework endpoints (not part of the IdentityHttp module contract) stay hand-written: the
    // passwordless email magic-link (POST /api/auth/email) and the provider list (GET /api/auth/providers).
    let private encodeMagicLink (email: string) (returnTo: string) : string =
        Encode.object [ "email", Encode.string email; "returnTo", Encode.string returnTo ] |> Encode.toString 0

    let private requestMagicLinkCmd (email: string) (returnTo: string) : Cmd<Msg> =
        Cmd.OfPromise.either
            (fun () -> Client.Api.postJsonRaw "/api/auth/email" (encodeMagicLink email returnTo))
            ()
            GotMagicLink
            (fun ex -> GotMagicLink (Error ex.Message))

    let private providersDecoder : Decoder<string list> =
        Decode.field "providers" (Decode.list Decode.string)

    let private revertIdentityCmd (identityId: string) (merge: bool) : Cmd<Msg> =
        Cmd.OfPromise.perform
            (fun () -> promise {
                let! r = api.identityHttpRevert { identityId = identityId; merge = merge }
                return toUnitResult r })
            ()
            GotRevertIdentity

    let private disconnectIdentityCmd (identityId: string) (fallbackName: string) : Cmd<Msg> =
        Cmd.OfPromise.perform
            (fun () -> promise {
                let! r = api.identityHttpDisconnect { identityId = identityId; name = Some fallbackName }
                return toUnitResult r })
            ()
            GotDisconnect

    let private loadProvidersCmd : Cmd<Msg> =
        Cmd.OfPromise.perform
            (fun () ->
                promise {
                    let! data = Client.Api.fetchJsonRaw "/api/auth/providers"
                    return
                        match Decode.fromValue "$" providersDecoder data with
                        | Ok providers -> providers
                        | Error _ -> []
                })
            ()
            GotProviders

    let loadIdentitiesCmd : Cmd<Msg> =
        Cmd.OfPromise.perform
            (fun () -> promise {
                let! r = api.identityHttpGetIdentities ()
                return
                    match r with
                    | Ok resp ->
                        resp.identities
                        |> List.map (fun (it: IdentityHttp.Api.IdentityListItem) ->
                            ({ Id = it.id; Provider = it.provider; Name = it.name
                               Picture = it.picture; ActivatedAt = it.activatedAt } : IdentityListItem))
                    | Error _ -> [] })
            ()
            GotIdentities

    let private syncCmd : Cmd<Msg> =
        Cmd.OfPromise.perform GuestSession.syncSession () GotSessionSync

    /// Cached guest state now; sync + providers fire ONCE at boot. `claimFocus` is set
    /// when the boot route is an OAuth return, so the switcher opens pre-selected after sync.
    let init (claimFocus: string option) : Model * Cmd<Msg> =
        let model =
            { GuestSession = GuestSession.getSession ()
              Identities = []
              AvailableProviders = []
              ShowIdentitySwitcher = false
              SelectedIdentity = None
              PendingClaimFocus = claimFocus
              EmailInput = ""
              MagicLink = MlIdle }
        let bootCmds =
            [ syncCmd
              loadProvidersCmd
              if claimFocus.IsSome then loadIdentitiesCmd ]
        model, Cmd.batch bootCmds

    let update (msg: Msg) (model: Model) : Model * Cmd<Msg> * Signal =
        match msg with
        | GotSessionSync session ->
            { model with GuestSession = session }, Cmd.none, SessionChanged session

        | RevertIdentity (identityId, merge) ->
            // Deliberately no loading state: a switch is a background request on a page
            // we want to keep showing.
            model, revertIdentityCmd identityId merge, NoSignal

        | GotRevertIdentity (Ok _) ->
            // A merge rewrites comment authorship server-side, so refresh the session and
            // reload whatever the current route displays.
            { model with ShowIdentitySwitcher = false; SelectedIdentity = None },
            Cmd.batch [ syncCmd; loadIdentitiesCmd ],
            ReloadContent

        | GotRevertIdentity (Error err) ->
            // Keep the switcher open so the user can retry; surface the failure.
            model, Cmd.none, Failed err

        | DisconnectIdentity identityId ->
            // Dropping a provider returns the guest to anonymous. Store the guest's own generated
            // pseudonym (the new-guest formula) as the fallback name — NOT the current provider
            // display name — so the anonymous identity presents as a fresh generated name + matching
            // icon rather than inheriting the dropped account's name.
            model, disconnectIdentityCmd identityId (GuestSession.anonName()), NoSignal

        | GotDisconnect (Ok _) ->
            { model with ShowIdentitySwitcher = false; SelectedIdentity = None },
            Cmd.batch [ syncCmd; loadIdentitiesCmd ],
            ReloadContent

        | GotDisconnect (Error err) ->
            model, Cmd.none, Failed err

        | LoadIdentities ->
            model, loadIdentitiesCmd, NoSignal

        | GotProviders providers ->
            { model with AvailableProviders = providers }, Cmd.none, NoSignal

        | GotIdentities identities ->
            { model with Identities = identities }, Cmd.none, NoSignal

        | ToggleIdentitySwitcher ->
            let show = not model.ShowIdentitySwitcher
            { model with ShowIdentitySwitcher = show; SelectedIdentity = None },
            (if show then loadIdentitiesCmd else Cmd.none),
            NoSignal

        | SelectIdentity identityId ->
            let selected = if model.SelectedIdentity = Some identityId then None else Some identityId
            { model with SelectedIdentity = selected }, Cmd.none, NoSignal

        | SetEmailInput value ->
            // Editing (or the "use a different email" reset, which sends "") clears a prior failure/sent
            // state so the form is usable again — e.g. after a typo'd address.
            { model with EmailInput = value; MagicLink = (match model.MagicLink with MlFailed _ | MlSent -> MlIdle | s -> s) }, Cmd.none, NoSignal

        | RequestMagicLink ->
            let email = model.EmailInput.Trim().ToLowerInvariant()
            if email = "" || not (email.Contains "@") then
                { model with MagicLink = MlFailed "Enter a valid email address" }, Cmd.none, NoSignal
            else
                // Return to the current route after the link is clicked (same rule as loginButton).
                let path = Browser.Dom.window.location.pathname
                let returnTo = if path.StartsWith "/auth/" then "/" else path
                { model with MagicLink = MlSending }, requestMagicLinkCmd email returnTo, NoSignal

        | GotMagicLink (Ok _) ->
            { model with MagicLink = MlSent }, Cmd.none, NoSignal

        | GotMagicLink (Error err) ->
            { model with MagicLink = MlFailed err }, Cmd.none, NoSignal

    /// Consume a pending OAuth claim focus once the host has navigated back to the return
    /// route: open the switcher pre-selected on the claimed identity.
    let consumeClaimFocus (model: Model) : Model =
        match model.PendingClaimFocus with
        | Some id -> { model with ShowIdentitySwitcher = true; SelectedIdentity = Some id; PendingClaimFocus = None }
        | None -> { model with ShowIdentitySwitcher = false; SelectedIdentity = None }
