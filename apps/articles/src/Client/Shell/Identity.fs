module Articles.Client.Shell.Identity

// Shell-owned identity — a lifted copy of the sync/providers/switcher/claim/revert/
// disconnect behaviour that the articles + blog modules each carry today (unified shell,
// Stage 1; notes/UNIFIED-SHELL.md). The shell is the single identity authority per
// document: it initialises cached guest state once, syncs once at boot, and loads
// providers once, so a hosted content module never issues its own boot identity
// requests. The duplication with the modules' own identity code is deliberate and
// deleted at Stage 3 (convergence), once the standalone entries also consume this.

module GuestSession = Client.GuestSession

open Fable.Core.JsInterop
open Elmish

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
      /// Set on OAuth return; consumed once the shell navigates back to the return
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

/// What an identity update means for the rest of the shell.
type Signal =
    | NoSignal
    /// The authoritative session changed — the host pushes it into the child model(s).
    | SessionChanged of GuestSession.GuestSessionData
    /// A merge/disconnect re-attributed content server-side — the host reloads the
    /// current route so displayed authorship is refreshed.
    | ReloadContent
    /// An identity operation failed — the host surfaces the message to the user.
    | Failed of string

// -- Commands (copied from the modules' App.fs; the /api/auth/* endpoints are shared) --

let private revertIdentityCmd (identityId: string) (merge: bool) : Cmd<Msg> =
    let body = sprintf """{"identityId":"%s","merge":%s}""" identityId (if merge then "true" else "false")
    Cmd.OfPromise.either
        (fun () -> Client.Api.postJsonRaw "/api/auth/revert" body)
        ()
        GotRevertIdentity
        (fun ex -> GotRevertIdentity (Error ex.Message))

let private disconnectIdentityCmd (identityId: string) (fallbackName: string) : Cmd<Msg> =
    let body = sprintf """{"identityId":"%s","name":"%s"}""" identityId fallbackName
    Cmd.OfPromise.either
        (fun () -> Client.Api.postJsonRaw "/api/auth/disconnect" body)
        ()
        GotDisconnect
        (fun ex -> GotDisconnect (Error ex.Message))

let private loadProvidersCmd : Cmd<Msg> =
    Cmd.OfPromise.perform
        (fun () ->
            promise {
                let! data = Client.Api.fetchJsonRaw "/api/auth/providers"
                let arr : string array = data?providers |> unbox
                return List.ofArray arr
            })
        ()
        GotProviders

let loadIdentitiesCmd : Cmd<Msg> =
    Cmd.OfPromise.perform
        (fun () ->
            promise {
                let! data = Client.Api.fetchJsonRaw "/api/auth/identities"
                let arr : obj array = data?identities |> unbox
                return arr |> Array.map (fun o ->
                    { Id = o?id |> unbox<string>
                      Provider = o?provider |> unbox<string>
                      Name = o?name |> unbox<string>
                      Picture = o?picture |> unbox<string>
                      ActivatedAt = let v = o?activatedAt in if isNull v then None else Some (unbox<int> v) }
                ) |> Array.toList
            })
        ()
        GotIdentities

let private syncCmd : Cmd<Msg> =
    Cmd.OfPromise.perform GuestSession.syncSession () GotSessionSync

/// Cached guest state now; sync + providers fire ONCE at boot. `claimFocus` is set when
/// the boot route is an OAuth return, so the switcher opens pre-selected after sync.
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
        // Deliberately no loading state: a switch is a background request on a page we
        // want to keep showing.
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

/// Consume a pending OAuth claim focus once the shell has navigated back to the return
/// route: open the switcher pre-selected on the claimed identity.
let consumeClaimFocus (model: Model) : Model =
    match model.PendingClaimFocus with
    | Some id -> { model with ShowIdentitySwitcher = true; SelectedIdentity = Some id; PendingClaimFocus = None }
    | None -> { model with ShowIdentitySwitcher = false; SelectedIdentity = None }
