namespace Content

// Shared identity subsystem (unified shell, Stage 3 — convergence). The single identity
// authority a host owns per document: cached guest state now, one sync + one providers
// load at boot, and the switcher/claim/revert/disconnect behaviour. Extracted from the
// (proven) Justat shell copy into ordinary shared client code so every host — the Justat
// shell now, the ndct + microblog standalone hosts as they migrate — consumes ONE copy,
// and the per-module identity duplication can then be deleted.
//
// Ordinary client code, file-linked into each consuming Client project (after the shared
// Client.GuestSession/Client.Api it builds on, before the module .props). Not framework.

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
          PendingClaimFocus: string option }

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

    // -- Codecs + commands (the /api/auth/* endpoints are shared across every site). Typed with
    //    Thoth (C2 item 4): request bodies escape their values — fallbackName is the
    //    user-controlled DisplayName, so string-interpolating it into JSON was an injection risk
    //    — and responses decode through explicit decoders instead of `?field |> unbox`. The wire
    //    is unchanged: the same JSON keys and shapes as before. --

    let private encodeRevert (identityId: string) (merge: bool) : string =
        Encode.object [ "identityId", Encode.string identityId; "merge", Encode.bool merge ] |> Encode.toString 0

    let private encodeDisconnect (identityId: string) (name: string) : string =
        Encode.object [ "identityId", Encode.string identityId; "name", Encode.string name ] |> Encode.toString 0

    let private providersDecoder : Decoder<string list> =
        Decode.field "providers" (Decode.list Decode.string)

    let private identityDecoder : Decoder<IdentityListItem> =
        Decode.object (fun get ->
            { Id = get.Required.Field "id" Decode.string
              Provider = get.Required.Field "provider" Decode.string
              Name = get.Required.Field "name" Decode.string
              Picture = get.Required.Field "picture" Decode.string
              ActivatedAt = get.Optional.Field "activatedAt" Decode.int })

    let private identitiesDecoder : Decoder<IdentityListItem list> =
        Decode.field "identities" (Decode.list identityDecoder)

    let private revertIdentityCmd (identityId: string) (merge: bool) : Cmd<Msg> =
        Cmd.OfPromise.either
            (fun () -> Client.Api.postJsonRaw "/api/auth/revert" (encodeRevert identityId merge))
            ()
            GotRevertIdentity
            (fun ex -> GotRevertIdentity (Error ex.Message))

    let private disconnectIdentityCmd (identityId: string) (fallbackName: string) : Cmd<Msg> =
        Cmd.OfPromise.either
            (fun () -> Client.Api.postJsonRaw "/api/auth/disconnect" (encodeDisconnect identityId fallbackName))
            ()
            GotDisconnect
            (fun ex -> GotDisconnect (Error ex.Message))

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
            (fun () ->
                promise {
                    let! data = Client.Api.fetchJsonRaw "/api/auth/identities"
                    return
                        match Decode.fromValue "$" identitiesDecoder data with
                        | Ok identities -> identities
                        | Error _ -> []
                })
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
              PendingClaimFocus = claimFocus }
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
            model, disconnectIdentityCmd identityId model.GuestSession.DisplayName, NoSignal

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

    /// Consume a pending OAuth claim focus once the host has navigated back to the return
    /// route: open the switcher pre-selected on the claimed identity.
    let consumeClaimFocus (model: Model) : Model =
        match model.PendingClaimFocus with
        | Some id -> { model with ShowIdentitySwitcher = true; SelectedIdentity = Some id; PendingClaimFocus = None }
        | None -> { model with ShowIdentitySwitcher = false; SelectedIdentity = None }
