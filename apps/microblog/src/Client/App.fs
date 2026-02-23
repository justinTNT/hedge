module Client.App

open Feliz
open Feliz.Router
open Elmish
open Client.Types
open Client.Pages

let init () : Model * Cmd<Msg> =
    let route = Router.currentUrl ()
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
          ReplyingTo = None }
    let cmd =
        match route with
        | ["item"; id] -> Cmd.ofMsg (LoadItem id)
        | ["tag"; name] -> Cmd.ofMsg (LoadTagItems name)
        | ["new"] -> Cmd.batch [ Cmd.ofMsg LoadFeed; NewItem.initOwnerCommentEditorCmd ]
        | _ -> Cmd.ofMsg LoadFeed
    model, cmd

let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | UrlChanged route ->
        let cleanupCmd = Cmd.batch [
            Item.disconnectEventsCmd ()
            Item.destroyCommentEditorCmd
            NewItem.destroyOwnerCommentEditorCmd
            Item.destroyAllViewersCmd
        ]
        let cmd =
            match route with
            | [] | ["feed"] -> Cmd.batch [ cleanupCmd; Cmd.ofMsg LoadFeed ]
            | ["item"; id] -> Cmd.batch [ cleanupCmd; Cmd.ofMsg (LoadItem id) ]
            | ["tag"; name] -> Cmd.batch [ cleanupCmd; Cmd.ofMsg (LoadTagItems name) ]
            | ["new"] -> Cmd.batch [ cleanupCmd; NewItem.initOwnerCommentEditorCmd ]
            | _ -> cleanupCmd
        { model with Route = route; CurrentItem = None; TagItems = None; ReplyingTo = None; CollapsedComments = Set.empty }, cmd

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

let appView (model: Model) dispatch =
    Html.div [
        prop.className "app"
        prop.children [
            Html.header [ Shared.nav ]
            Html.main [
                match model.Error with
                | Some err -> Shared.error err dispatch
                | None -> Html.none

                if model.IsLoading then
                    Shared.loading
                else
                    match model.Route with
                    | ["item"; _] ->
                        match model.CurrentItem with
                        | Some response -> Item.view response model dispatch
                        | None -> Html.p [ prop.text "Item not found." ]
                    | ["tag"; _] ->
                        match model.TagItems with
                        | Some response -> TagItems.view response
                        | None -> Html.p [ prop.text "No items for this tag." ]
                    | ["new"] ->
                        NewItem.view model.ItemForm dispatch
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
        router.onUrlChanged (UrlChanged >> dispatch)
        router.children [ appView model dispatch ]
    ]

#if DEBUG
open Elmish.HMR
#endif

Program.mkProgram init update view
|> Program.withReactSynchronous "app"
|> Program.run
