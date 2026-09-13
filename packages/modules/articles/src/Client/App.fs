module Articles.Client.App

// The shared guest-session accessor is host-provided (packages/hedge/src/Client);
// alias it locally, the same way Client.RichText is aliased.
module GuestSession = Client.GuestSession

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Feliz.Router
open Elmish
open Articles.Client
open Articles.Client.Types
open Articles.Client.Pages

[<Emit("new URLSearchParams(window.location.search).get($0)")>]
let private getQueryParam (name: string) : string = jsNative

/// OAuth return: /auth/claim?identity=...&returnTo=...
let private parseClaimFromRoute () : (string option * string) =
    let identity = getQueryParam "identity"
    let returnTo = getQueryParam "returnTo"
    let identity = if isNull identity || identity = "" then None else Some identity
    let returnTo = if isNull returnTo || returnTo = "" then "/" else returnTo
    identity, returnTo

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

let private loadIdentitiesCmd : Cmd<Msg> =
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

let init () : Model * Cmd<Msg> =
    let route = Router.currentUrl ()
    let claimFocus, claimReturnTo =
        match route with
        | ["auth"; "claim"] | ["auth"; "claim"; _] -> parseClaimFromRoute ()
        | _ -> None, "/"
    let model =
        { Route = route
          Feed = None
          FeedLoadingMore = false
          CurrentItem = None
          IsLoading = false
          Error = None
          GuestSession = GuestSession.getSession ()
          CollapsedComments = Set.empty
          ReplyingTo = None
          Identities = []
          AvailableProviders = []
          ShowIdentitySwitcher = false
          SelectedIdentity = None
          PendingClaimFocus = claimFocus }
    let routeCmd =
        match route with
        | ["auth"; "claim"] | ["auth"; "claim"; _] ->
            Cmd.batch [
                loadIdentitiesCmd
                Cmd.ofEffect (fun _ -> Shared.navigateToPath claimReturnTo)
            ]
        | [idOrSlug] -> Cmd.ofMsg (LoadItem idOrSlug)
        | _ -> Cmd.ofMsg LoadFeed
    let syncCmd =
        Cmd.OfPromise.perform GuestSession.syncSession () GotSessionSync
    model, Cmd.batch [ routeCmd; syncCmd; loadProvidersCmd ]

let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | UrlChanged route ->
        let cleanupCmd = Cmd.batch [ Item.destroyCommentEditorCmd; Item.destroyAllViewersCmd; Item.disconnectEventsCmd () ]
        match route with
        | ["auth"; "claim"] | ["auth"; "claim"; _] ->
            let claimFocus, claimReturnTo = parseClaimFromRoute ()
            let updated =
                { model with
                    Route = route
                    CurrentItem = None
                    ReplyingTo = None
                    CollapsedComments = Set.empty
                    ShowIdentitySwitcher = false
                    SelectedIdentity = None
                    PendingClaimFocus = claimFocus }
            updated,
            Cmd.batch [
                cleanupCmd
                loadIdentitiesCmd
                Cmd.ofEffect (fun _ -> Shared.navigateToPath claimReturnTo)
            ]
        | _ ->
        let showSwitcher, selected =
            match model.PendingClaimFocus with
            | Some id -> true, Some id
            | None -> false, None
        let resetTitleCmd = Cmd.ofEffect (fun _ -> Shared.setDocTitle "")
        let cmd =
            match route with
            | [] -> Cmd.batch [ cleanupCmd; resetTitleCmd; Cmd.ofMsg LoadFeed ]
            | [idOrSlug] -> Cmd.batch [ cleanupCmd; Cmd.ofMsg (LoadItem idOrSlug) ]
            | _ -> Cmd.batch [ cleanupCmd; resetTitleCmd ]
        { model with Route = route; CurrentItem = None; ReplyingTo = None; CollapsedComments = Set.empty; ShowIdentitySwitcher = showSwitcher; SelectedIdentity = selected; PendingClaimFocus = None }, cmd

    | DismissError ->
        { model with Error = None }, Cmd.none

    | LoadFeed | GotFeed _ | LoadMoreFeed | GotMoreFeed _ ->
        Feed.update msg model

    | LoadItem _ | GotItem _ | SubmitComment | GotSubmitComment _ | ToggleCollapse _ | SetReplyTo _ | CancelReply
    | ConnectEvents _ | DisconnectEvents | GotEvent _ | EventError _ ->
        Item.update msg model

    | GotSessionSync session ->
        { model with GuestSession = session }, Cmd.none

    | RevertIdentity (identityId, merge) ->
        model, revertIdentityCmd identityId merge

    | GotRevertIdentity (Ok _) ->
        let reloadCmd =
            match model.Route with
            | [idOrSlug] -> Cmd.ofMsg (LoadItem idOrSlug)
            | _ -> Cmd.ofMsg LoadFeed
        { model with IsLoading = false; ShowIdentitySwitcher = false; SelectedIdentity = None },
        Cmd.batch [
            Cmd.OfPromise.perform GuestSession.syncSession () GotSessionSync
            loadIdentitiesCmd
            reloadCmd
        ]

    | GotRevertIdentity (Error err) ->
        { model with IsLoading = false; Error = Some err }, Cmd.none

    | DisconnectIdentity identityId ->
        model, disconnectIdentityCmd identityId model.GuestSession.DisplayName

    | GotDisconnect (Ok _) ->
        let reloadCmd =
            match model.Route with
            | [idOrSlug] -> Cmd.ofMsg (LoadItem idOrSlug)
            | _ -> Cmd.ofMsg LoadFeed
        { model with ShowIdentitySwitcher = false; SelectedIdentity = None },
        Cmd.batch [
            Cmd.OfPromise.perform GuestSession.syncSession () GotSessionSync
            loadIdentitiesCmd
            reloadCmd
        ]

    | GotDisconnect (Error err) ->
        { model with Error = Some err }, Cmd.none

    | LoadIdentities ->
        model, loadIdentitiesCmd

    | GotProviders providers ->
        { model with AvailableProviders = providers }, Cmd.none

    | GotIdentities identities ->
        { model with Identities = identities }, Cmd.none

    | ToggleIdentitySwitcher ->
        let show = not model.ShowIdentitySwitcher
        { model with ShowIdentitySwitcher = show; SelectedIdentity = None },
        if show then loadIdentitiesCmd else Cmd.none

    | SelectIdentity identityId ->
        let selected = if model.SelectedIdentity = Some identityId then None else Some identityId
        { model with SelectedIdentity = selected }, Cmd.none

let appView (model: Model) dispatch =
    Html.div [
        prop.className "app"
        prop.children [
            if Hedge.Tenant.config.Slug = "ndct" && List.isEmpty model.Route then Shared.ndctHero else Html.none
            Html.header [ Shared.navWithSession model dispatch ]
            Html.main [
                match model.Error with
                | Some err -> Shared.error err dispatch
                | None -> Html.none

                if model.IsLoading then
                    Shared.loading
                else
                    match model.Route with
                    | ["auth"; "claim"] | ["auth"; "claim"; _] ->
                        Shared.loading
                    | [_] ->
                        match model.CurrentItem with
                        | Some response -> Item.view response model dispatch
                        | None -> Html.p [ prop.text "Post not found." ]
                    | _ ->
                        match model.Feed with
                        | Some response -> Feed.view response
                        | None -> Html.p [ prop.text "No posts yet." ]
            ]
            if Hedge.Tenant.config.Slug = "justat" then Shared.justatSidebar else Html.none
        ]
    ]

let view model dispatch =
    React.router [
        router.pathMode
        router.onUrlChanged (Shared.routeOf >> UrlChanged >> dispatch)
        router.children [ appView model dispatch ]
    ]

// This is a COMPONENT (init/update/view) — the standalone entry
// (apps/articles/src/Client/Articles/Main.fs) runs it, and a future unified shell
// can host the same component unchanged (locked decision D1).
