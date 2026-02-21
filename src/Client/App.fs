module Client.App

open Feliz
open Feliz.Router
open Elmish
open Hedge.Interface
open Models.Api

type CommentForm = {
    Text: string
    AuthorName: string
    ParentId: string option
}

type ItemForm = {
    Title: string
    Link: string
    OwnerComment: string
    Tags: string
}

type Model = {
    Route: string list
    Feed: GetFeed.Response option
    CurrentItem: GetItem.Response option
    IsLoading: bool
    Error: string option
    CommentForm: CommentForm
    ItemForm: ItemForm
}

type Msg =
    | UrlChanged of string list
    | LoadFeed
    | GotFeed of Result<GetFeed.Response, string>
    | LoadItem of string
    | GotItem of Result<GetItem.Response, string>
    | DismissError
    | ConnectEvents of string
    | DisconnectEvents
    | GotEvent of Models.Sse.NewCommentEvent
    | EventError of string
    | SetCommentText of string
    | SetCommentAuthor of string
    | SubmitComment
    | GotSubmitComment of Result<SubmitComment.Response, string>
    | SetNewItemTitle of string
    | SetNewItemLink of string
    | SetNewItemOwnerComment of string
    | SetNewItemTags of string
    | SubmitItem
    | GotSubmitItem of Result<SubmitItem.Response, string>

let emptyCommentForm = { Text = ""; AuthorName = ""; ParentId = None }
let emptyItemForm = { Title = ""; Link = ""; OwnerComment = ""; Tags = "" }

// WebSocket management via module-level mutable.
// Elmish Cmd.ofEffect doesn't let us capture return values into the Model,
// so we track the close function externally.
let mutable private currentWsClose : (unit -> unit) option = None

let private connectEventsCmd (itemId: string) : Cmd<Msg> =
    Cmd.ofEffect (fun dispatch ->
        match currentWsClose with
        | Some close -> close ()
        | None -> ()
        let close =
            Client.Api.connectEvents
                itemId
                (fun event -> dispatch (GotEvent event))
                (fun err -> dispatch (EventError err))
        currentWsClose <- Some close
    )

let private disconnectEventsCmd () : Cmd<Msg> =
    Cmd.ofEffect (fun _dispatch ->
        match currentWsClose with
        | Some close ->
            close ()
            currentWsClose <- None
        | None -> ()
    )

let init () : Model * Cmd<Msg> =
    let route = Router.currentUrl ()
    let model =
        { Route = route
          Feed = None
          CurrentItem = None
          IsLoading = false
          Error = None
          CommentForm = emptyCommentForm
          ItemForm = emptyItemForm }
    let cmd =
        match route with
        | ["item"; id] -> Cmd.ofMsg (LoadItem id)
        | _ -> Cmd.ofMsg LoadFeed
    model, cmd

let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | UrlChanged route ->
        let cmd =
            match route with
            | [] | ["feed"] -> Cmd.batch [ disconnectEventsCmd (); Cmd.ofMsg LoadFeed ]
            | ["item"; id] -> Cmd.batch [ disconnectEventsCmd (); Cmd.ofMsg (LoadItem id) ]
            | _ -> disconnectEventsCmd ()
        { model with Route = route; CurrentItem = None; CommentForm = emptyCommentForm }, cmd

    | LoadFeed ->
        { model with IsLoading = true },
        Cmd.OfPromise.either Client.Api.getFeed () GotFeed (fun ex -> GotFeed (Error ex.Message))

    | GotFeed (Ok response) ->
        { model with Feed = Some response; IsLoading = false; Error = None }, Cmd.none

    | GotFeed (Error err) ->
        { model with IsLoading = false; Error = Some err }, Cmd.none

    | LoadItem itemId ->
        { model with IsLoading = true; CurrentItem = None },
        Cmd.OfPromise.either Client.Api.getItem itemId GotItem (fun ex -> GotItem (Error ex.Message))

    | GotItem (Ok response) ->
        { model with CurrentItem = Some response; IsLoading = false },
        connectEventsCmd response.Item.Id

    | GotItem (Error err) ->
        { model with IsLoading = false; Error = Some err }, Cmd.none

    | DismissError ->
        { model with Error = None }, Cmd.none

    | ConnectEvents itemId ->
        model, connectEventsCmd itemId

    | DisconnectEvents ->
        model, disconnectEventsCmd ()

    | GotEvent event ->
        match model.CurrentItem with
        | Some response when response.Item.Id = event.ItemId ->
            let newComment : SubmitComment.CommentItem =
                { Id = event.Id
                  ItemId = event.ItemId
                  GuestId = event.GuestId
                  ParentId = event.ParentId
                  AuthorName = event.AuthorName
                  Text = RichContent event.Text
                  Timestamp = event.Timestamp }
            let existingIds = response.Item.Comments |> List.map (fun c -> c.Id) |> Set.ofList
            if Set.contains event.Id existingIds then
                model, Cmd.none
            else
                let updatedItem = { response.Item with Comments = response.Item.Comments @ [newComment] }
                { model with CurrentItem = Some { Item = updatedItem } }, Cmd.none
        | _ -> model, Cmd.none

    | EventError _ ->
        model, Cmd.none

    | SetCommentText text ->
        { model with CommentForm = { model.CommentForm with Text = text } }, Cmd.none

    | SetCommentAuthor name ->
        { model with CommentForm = { model.CommentForm with AuthorName = name } }, Cmd.none

    | SubmitComment ->
        match model.CurrentItem with
        | Some response ->
            let req : SubmitComment.Request =
                { ItemId = response.Item.Id
                  ParentId = model.CommentForm.ParentId
                  Text = model.CommentForm.Text
                  AuthorName =
                    if model.CommentForm.AuthorName = "" then None
                    else Some model.CommentForm.AuthorName }
            { model with CommentForm = emptyCommentForm },
            Cmd.OfPromise.either Client.Api.submitComment req GotSubmitComment (fun ex -> GotSubmitComment (Error ex.Message))
        | None -> model, Cmd.none

    | GotSubmitComment (Ok _) ->
        model, Cmd.none

    | GotSubmitComment (Error err) ->
        { model with Error = Some err }, Cmd.none

    | SetNewItemTitle title ->
        { model with ItemForm = { model.ItemForm with Title = title } }, Cmd.none

    | SetNewItemLink link ->
        { model with ItemForm = { model.ItemForm with Link = link } }, Cmd.none

    | SetNewItemOwnerComment comment ->
        { model with ItemForm = { model.ItemForm with OwnerComment = comment } }, Cmd.none

    | SetNewItemTags tags ->
        { model with ItemForm = { model.ItemForm with Tags = tags } }, Cmd.none

    | SubmitItem ->
        let form = model.ItemForm
        let tags =
            form.Tags.Split(',')
            |> Array.map (fun s -> s.Trim())
            |> Array.filter (fun s -> s <> "")
            |> Array.toList
        let req : SubmitItem.Request =
            { Title = form.Title
              Link = if form.Link = "" then None else Some form.Link
              Image = None
              Extract = None
              OwnerComment = form.OwnerComment
              Tags = tags }
        { model with ItemForm = emptyItemForm },
        Cmd.OfPromise.either Client.Api.submitItem req GotSubmitItem (fun ex -> GotSubmitItem (Error ex.Message))

    | GotSubmitItem (Ok _) ->
        model, Cmd.ofMsg LoadFeed

    | GotSubmitItem (Error err) ->
        { model with Error = Some err }, Cmd.none

module View =
    let loading =
        Html.div [
            prop.className "loading"
            prop.text "Loading..."
        ]

    let error (msg: string) dispatch =
        Html.div [
            prop.className "error"
            prop.children [
                Html.span [ prop.text msg ]
                Html.button [
                    prop.text "Dismiss"
                    prop.onClick (fun _ -> dispatch DismissError)
                ]
            ]
        ]

    let feedItem (item: GetFeed.FeedItem) =
        Html.article [
            prop.className "feed-item"
            prop.style [ style.cursor.pointer ]
            prop.onClick (fun _ -> Router.navigate ("item", item.Id))
            prop.children [
                Html.h2 [ prop.text item.Title ]
                match item.Extract with
                | Some (RichContent text) -> Html.p [ prop.className "extract"; prop.text text ]
                | None -> Html.none
            ]
        ]

    let feed (response: GetFeed.Response) =
        Html.div [
            prop.className "feed"
            prop.children (response.Items |> List.map feedItem)
        ]

    let commentView (comment: SubmitComment.CommentItem) =
        let (RichContent text) = comment.Text
        Html.div [
            prop.className "comment"
            prop.style [
                match comment.ParentId with
                | Some _ -> style.marginLeft 20
                | None -> ()
            ]
            prop.children [
                Html.div [
                    prop.className "comment-meta"
                    prop.children [
                        Html.strong [ prop.text comment.AuthorName ]
                    ]
                ]
                Html.p [ prop.text text ]
            ]
        ]

    let commentForm (form: CommentForm) dispatch =
        Html.div [
            prop.className "comment-form"
            prop.children [
                Html.h3 [ prop.text "Add a comment" ]
                Html.input [
                    prop.placeholder "Name (optional)"
                    prop.value form.AuthorName
                    prop.onChange (SetCommentAuthor >> dispatch)
                ]
                Html.textarea [
                    prop.placeholder "Write a comment..."
                    prop.value form.Text
                    prop.onChange (SetCommentText >> dispatch)
                ]
                Html.button [
                    prop.text "Submit"
                    prop.disabled (form.Text.Trim() = "")
                    prop.onClick (fun _ -> dispatch SubmitComment)
                ]
            ]
        ]

    let itemDetail (response: GetItem.Response) (form: CommentForm) dispatch =
        let item = response.Item
        let (RichContent ownerComment) = item.OwnerComment
        Html.div [
            prop.className "item-detail"
            prop.children [
                Html.h2 [ prop.text item.Title ]
                match item.Link with
                | Some (Link url) ->
                    Html.a [
                        prop.href url
                        prop.target "_blank"
                        prop.rel "noopener"
                        prop.text url
                    ]
                | None -> Html.none
                match item.Image with
                | Some (Link imgUrl) ->
                    Html.img [ prop.src imgUrl; prop.className "item-image" ]
                | None -> Html.none
                match item.Extract with
                | Some (RichContent extract) ->
                    Html.div [ prop.className "extract"; prop.text extract ]
                | None -> Html.none
                Html.div [
                    prop.className "owner-comment"
                    prop.text ownerComment
                ]
                if not item.Tags.IsEmpty then
                    Html.div [
                        prop.className "tags"
                        prop.children (
                            item.Tags |> List.map (fun tag ->
                                Html.span [
                                    prop.className "tag"
                                    prop.text tag
                                ]
                            )
                        )
                    ]
                Html.div [
                    prop.className "comments"
                    prop.children [
                        Html.h3 [ prop.text (sprintf "Comments (%d)" item.Comments.Length) ]
                        yield! item.Comments |> List.map commentView
                    ]
                ]
                commentForm form dispatch
            ]
        ]

    let newItemForm (form: ItemForm) dispatch =
        Html.div [
            prop.className "new-item-form"
            prop.children [
                Html.h2 [ prop.text "New Item" ]
                Html.input [
                    prop.placeholder "Title"
                    prop.value form.Title
                    prop.onChange (SetNewItemTitle >> dispatch)
                ]
                Html.input [
                    prop.placeholder "Link (optional)"
                    prop.value form.Link
                    prop.onChange (SetNewItemLink >> dispatch)
                ]
                Html.textarea [
                    prop.placeholder "Your comment..."
                    prop.value form.OwnerComment
                    prop.onChange (SetNewItemOwnerComment >> dispatch)
                ]
                Html.input [
                    prop.placeholder "Tags (comma-separated)"
                    prop.value form.Tags
                    prop.onChange (SetNewItemTags >> dispatch)
                ]
                Html.button [
                    prop.text "Create"
                    prop.disabled (form.Title.Trim() = "" || form.OwnerComment.Trim() = "")
                    prop.onClick (fun _ -> dispatch SubmitItem)
                ]
            ]
        ]

    let nav =
        Html.nav [
            prop.children [
                Html.a [
                    prop.text "Hedge"
                    prop.style [ style.cursor.pointer ]
                    prop.onClick (fun _ -> Router.navigate "")
                ]
                Html.a [
                    prop.text "New Item"
                    prop.style [ style.cursor.pointer; style.marginLeft 16 ]
                    prop.onClick (fun _ -> Router.navigate "new")
                ]
            ]
        ]

    let app (model: Model) dispatch =
        Html.div [
            prop.className "app"
            prop.children [
                Html.header [ nav ]
                Html.main [
                    match model.Error with
                    | Some err -> error err dispatch
                    | None -> Html.none

                    if model.IsLoading then
                        loading
                    else
                        match model.Route with
                        | ["item"; _] ->
                            match model.CurrentItem with
                            | Some response -> itemDetail response model.CommentForm dispatch
                            | None -> Html.p [ prop.text "Item not found." ]
                        | ["new"] ->
                            newItemForm model.ItemForm dispatch
                        | _ ->
                            match model.Feed with
                            | Some response -> feed response
                            | None -> Html.p [ prop.text "No items yet." ]
                ]
            ]
        ]

open Elmish.React

let view model dispatch =
    React.router [
        router.onUrlChanged (UrlChanged >> dispatch)
        router.children [ View.app model dispatch ]
    ]

#if DEBUG
open Elmish.HMR
#endif

Program.mkProgram init update view
|> Program.withReactSynchronous "app"
|> Program.run
