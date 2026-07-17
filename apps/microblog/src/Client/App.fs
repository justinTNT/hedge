module Client.App

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Feliz.Router
open Elmish
open Client.Types
open Client.Pages

[<Emit("new URLSearchParams(window.location.search).get($0)")>]
let private getQueryParam (name: string) : string = jsNative

let private parseClaimFromRoute () : ClaimState option =
    let identity = getQueryParam "identity"
    let returnTo = getQueryParam "returnTo"
    if isNull identity || identity = "" then None
    else Some { IdentityId = identity; ReturnTo = if isNull returnTo || returnTo = "" then "/" else returnTo }

let private activateIdentityCmd (identityId: string) (merge: bool) : Cmd<Msg> =
    let body = sprintf """{"identityId":"%s","merge":%s}""" identityId (if merge then "true" else "false")
    Cmd.OfPromise.either
        (fun () -> Client.Api.postJsonRaw "/api/auth/activate" body)
        ()
        GotActivateClaim
        (fun ex -> GotActivateClaim (Error ex.Message))

let private revertIdentityCmd (identityId: string) (merge: bool) : Cmd<Msg> =
    let body = sprintf """{"identityId":"%s","merge":%s}""" identityId (if merge then "true" else "false")
    Cmd.OfPromise.either
        (fun () -> Client.Api.postJsonRaw "/api/auth/revert" body)
        ()
        GotRevertIdentity
        (fun ex -> GotRevertIdentity (Error ex.Message))

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
    let claimState =
        match route with
        | ["auth"; "claim"] -> parseClaimFromRoute ()
        | _ -> None
    let model =
        { Route = route
          Feed = None
          CurrentItem = None
          TagItems = None
          IsLoading = false
          Error = None
          GuestSession = GuestSession.getSession ()
          ItemForm = emptyItemForm
          CollapsedComments = Set.empty
          ReplyingTo = None
          ClaimState = claimState
          Identities = []
          ShowIdentitySwitcher = false }
    let routeCmd =
        match route with
        | ["auth"; "claim"] -> Cmd.none
        | ["tag"; name] -> Cmd.ofMsg (LoadTagItems name)
        | ["new"] -> Cmd.batch [ Cmd.ofMsg LoadFeed; NewItem.initOwnerCommentEditorCmd ]
        | [idOrSlug] -> Cmd.ofMsg (LoadItem idOrSlug)
        | _ -> Cmd.ofMsg LoadFeed
    let syncCmd =
        Cmd.OfPromise.perform GuestSession.syncSession () GotSessionSync
    model, Cmd.batch [ routeCmd; syncCmd ]

let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | UrlChanged route ->
        let cleanupCmd = Cmd.batch [
            Item.disconnectEventsCmd ()
            Item.destroyCommentEditorCmd
            NewItem.destroyOwnerCommentEditorCmd
            Item.destroyAllViewersCmd
        ]
        let claimState =
            match route with
            | ["auth"; "claim"] -> parseClaimFromRoute ()
            | _ -> None
        let cmd =
            match route with
            | ["auth"; "claim"] -> cleanupCmd
            | [] -> Cmd.batch [ cleanupCmd; Cmd.ofMsg LoadFeed ]
            | ["tag"; name] -> Cmd.batch [ cleanupCmd; Cmd.ofMsg (LoadTagItems name) ]
            | ["new"] -> Cmd.batch [ cleanupCmd; NewItem.initOwnerCommentEditorCmd ]
            | [idOrSlug] -> Cmd.batch [ cleanupCmd; Cmd.ofMsg (LoadItem idOrSlug) ]
            | _ -> cleanupCmd
        { model with Route = route; CurrentItem = None; TagItems = None; ReplyingTo = None; CollapsedComments = Set.empty; ClaimState = claimState; ShowIdentitySwitcher = false }, cmd

    | DismissError ->
        { model with Error = None }, Cmd.none

    | LoadFeed | GotFeed _ ->
        Feed.update msg model

    | LoadItem _ | GotItem _ | ConnectEvents _ | DisconnectEvents | GotEvent _ | EventError _
    | SubmitComment | GotSubmitComment _ | ToggleCollapse _ | SetReplyTo _ | CancelReply ->
        Item.update msg model

    | SetNewItemTitle _ | SetNewItemLink _ | SetNewItemTags _ | SubmitItem | GotSubmitItem _ ->
        NewItem.update msg model

    | LoadTagItems _ | GotTagItems _ ->
        TagItems.update msg model

    | GotSessionSync session ->
        { model with GuestSession = session }, Cmd.none

    | ActivateClaim merge ->
        match model.ClaimState with
        | Some claim ->
            { model with IsLoading = true }, activateIdentityCmd claim.IdentityId merge
        | None -> model, Cmd.none

    | GotActivateClaim (Ok _) ->
        let returnTo = model.ClaimState |> Option.map (fun c -> c.ReturnTo) |> Option.defaultValue "/"
        { model with ClaimState = None; IsLoading = false },
        Cmd.batch [
            Cmd.OfPromise.perform GuestSession.syncSession () GotSessionSync
            Cmd.ofEffect (fun _ -> Router.navigatePath returnTo)
        ]

    | GotActivateClaim (Error err) ->
        { model with IsLoading = false; Error = Some err }, Cmd.none

    | RevertIdentity (identityId, merge) ->
        { model with IsLoading = true }, revertIdentityCmd identityId merge

    | GotRevertIdentity (Ok _) ->
        { model with IsLoading = false; ShowIdentitySwitcher = false },
        Cmd.OfPromise.perform GuestSession.syncSession () GotSessionSync

    | GotRevertIdentity (Error err) ->
        { model with IsLoading = false; Error = Some err }, Cmd.none

    | LoadIdentities ->
        model, loadIdentitiesCmd

    | GotIdentities identities ->
        { model with Identities = identities }, Cmd.none

    | ToggleIdentitySwitcher ->
        let show = not model.ShowIdentitySwitcher
        { model with ShowIdentitySwitcher = show },
        if show then loadIdentitiesCmd else Cmd.none

let appView (model: Model) dispatch =
    Html.div [
        prop.className "app"
        prop.children [
            Html.header [ Shared.navWithSession model dispatch ]
            Html.main [
                match model.Error with
                | Some err -> Shared.error err dispatch
                | None -> Html.none

                if model.IsLoading then
                    Shared.loading
                else
                    match model.Route with
                    | ["auth"; "claim"] ->
                        Shared.claimView model dispatch
                    | ["tag"; _] ->
                        match model.TagItems with
                        | Some response -> TagItems.view response
                        | None -> Html.p [ prop.text "No items for this tag." ]
                    | ["new"] ->
                        NewItem.view model.ItemForm dispatch
                    | [_] ->
                        match model.CurrentItem with
                        | Some response -> Item.view response model dispatch
                        | None -> Html.p [ prop.text "Item not found." ]
                    | _ ->
                        match model.Feed with
                        | Some response -> Feed.view response
                        | None -> Html.p [ prop.text "No items yet." ]
            ]
        ]
    ]

open Elmish.React

let view model dispatch =
    React.router [
        router.pathMode
        router.onUrlChanged (UrlChanged >> dispatch)
        router.children [ appView model dispatch ]
    ]

#if DEBUG
open Elmish.HMR
#endif

Program.mkProgram init update view
|> Program.withReactSynchronous "app"
|> Program.run
