module Client.App

open Feliz
open Feliz.Router
open Elmish
open Hedge.Interface
open Models.Api

type ItemForm = {
    Title: string
    Link: string
    Tags: string
}

type Model = {
    Route: string list
    Feed: GetFeed.Response option
    CurrentItem: GetItem.Response option
    TagItems: GetItemsByTag.Response option
    IsLoading: bool
    Error: string option
    GuestSession: GuestSession.GuestSessionData
    ItemForm: ItemForm
    CollapsedComments: Set<string>
    ReplyingTo: {| ItemId: string; ParentId: string option |} option
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
    | GotEvent of Models.Ws.NewCommentEvent
    | EventError of string
    | LoadTagItems of string
    | GotTagItems of Result<GetItemsByTag.Response, string>
    | SubmitComment
    | GotSubmitComment of Result<SubmitComment.Response, string>
    | SetNewItemTitle of string
    | SetNewItemLink of string
    | SetNewItemTags of string
    | SubmitItem
    | GotSubmitItem of Result<SubmitItem.Response, string>
    | ToggleCollapse of string
    | SetReplyTo of itemId: string * parentId: string option
    | CancelReply

let emptyItemForm = { Title = ""; Link = ""; Tags = "" }

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

// Rich text editor lifecycle (same pattern as WebSocket)
let mutable private commentEditorActive = false
let mutable private ownerCommentEditorActive = false
let mutable private activeViewerIds : string list = []

let private initCommentEditorCmd : Cmd<Msg> =
    Cmd.ofEffect (fun _dispatch ->
        if not commentEditorActive then
            commentEditorActive <- true
            RichText.createEditorWhenReady RichText.commentEditorId ""
    )

let private destroyCommentEditorCmd : Cmd<Msg> =
    Cmd.ofEffect (fun _dispatch ->
        if commentEditorActive then
            RichText.destroyEditor RichText.commentEditorId
            commentEditorActive <- false
    )

let private initOwnerCommentEditorCmd : Cmd<Msg> =
    Cmd.ofEffect (fun _dispatch ->
        if not ownerCommentEditorActive then
            ownerCommentEditorActive <- true
            RichText.createEditorWhenReady RichText.ownerCommentEditorId ""
    )

let private destroyOwnerCommentEditorCmd : Cmd<Msg> =
    Cmd.ofEffect (fun _dispatch ->
        if ownerCommentEditorActive then
            RichText.destroyEditor RichText.ownerCommentEditorId
            ownerCommentEditorActive <- false
    )

let private destroyAllViewersCmd : Cmd<Msg> =
    Cmd.ofEffect (fun _dispatch ->
        activeViewerIds |> List.iter RichText.destroyViewer
        activeViewerIds <- []
    )

let private initViewersForItemCmd (response: GetItem.Response) : Cmd<Msg> =
    Cmd.ofEffect (fun _dispatch ->
        let item = response.Item
        let (RichContent ownerComment) = item.OwnerComment
        let ownerViewerId = sprintf "owner-comment-%s" item.Id
        RichText.createViewerWhenReady ownerViewerId ownerComment
        activeViewerIds <- ownerViewerId :: activeViewerIds
        match item.Extract with
        | Some (RichContent extract) ->
            let extractViewerId = sprintf "extract-%s" item.Id
            RichText.createViewerWhenReady extractViewerId extract
            activeViewerIds <- extractViewerId :: activeViewerIds
        | None -> ()
        item.Comments |> List.iter (fun comment ->
            let (RichContent text) = comment.Content
            let commentViewerId = sprintf "comment-%s" comment.Id
            RichText.createViewerWhenReady commentViewerId text
            activeViewerIds <- commentViewerId :: activeViewerIds
        )
    )

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
        | ["new"] -> Cmd.batch [ Cmd.ofMsg LoadFeed; initOwnerCommentEditorCmd ]
        | _ -> Cmd.ofMsg LoadFeed
    model, cmd

let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | UrlChanged route ->
        let cleanupCmd = Cmd.batch [
            disconnectEventsCmd ()
            destroyCommentEditorCmd
            destroyOwnerCommentEditorCmd
            destroyAllViewersCmd
        ]
        let cmd =
            match route with
            | [] | ["feed"] -> Cmd.batch [ cleanupCmd; Cmd.ofMsg LoadFeed ]
            | ["item"; id] -> Cmd.batch [ cleanupCmd; Cmd.ofMsg (LoadItem id) ]
            | ["tag"; name] -> Cmd.batch [ cleanupCmd; Cmd.ofMsg (LoadTagItems name) ]
            | ["new"] -> Cmd.batch [ cleanupCmd; initOwnerCommentEditorCmd ]
            | _ -> cleanupCmd
        { model with Route = route; CurrentItem = None; TagItems = None; ReplyingTo = None; CollapsedComments = Set.empty }, cmd

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
        Cmd.batch [
            connectEventsCmd response.Item.Id
            initViewersForItemCmd response
        ]

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
                  Author = event.Author
                  Content = RichContent event.Content
                  Timestamp = event.Timestamp }
            let existingIds = response.Item.Comments |> List.map (fun c -> c.Id) |> Set.ofList
            if Set.contains event.Id existingIds then
                model, Cmd.none
            else
                let updatedItem = { response.Item with Comments = response.Item.Comments @ [newComment] }
                let viewerId = sprintf "comment-%s" event.Id
                let initViewerCmd = Cmd.ofEffect (fun _dispatch ->
                    RichText.createViewerWhenReady viewerId event.Content
                    activeViewerIds <- viewerId :: activeViewerIds
                )
                { model with CurrentItem = Some { Item = updatedItem } }, initViewerCmd
        | _ -> model, Cmd.none

    | EventError _ ->
        model, Cmd.none

    | LoadTagItems tag ->
        { model with IsLoading = true; TagItems = None },
        Cmd.OfPromise.either Client.Api.getItemsByTag tag GotTagItems (fun ex -> GotTagItems (Error ex.Message))

    | GotTagItems (Ok response) ->
        { model with TagItems = Some response; IsLoading = false; Error = None }, Cmd.none

    | GotTagItems (Error err) ->
        { model with IsLoading = false; Error = Some err }, Cmd.none

    | SubmitComment ->
        match model.CurrentItem with
        | Some response ->
            let text = RichText.getEditorContent RichText.commentEditorId
            let parentId =
                match model.ReplyingTo with
                | Some rt -> rt.ParentId
                | None -> None
            let req : SubmitComment.Request =
                { ItemId = response.Item.Id
                  ParentId = parentId
                  Content = text
                  Author = Some model.GuestSession.DisplayName }
            model,
            Cmd.OfPromise.either Client.Api.submitComment req GotSubmitComment (fun ex -> GotSubmitComment (Error ex.Message))
        | None -> model, Cmd.none

    | GotSubmitComment (Ok _) ->
        { model with ReplyingTo = None },
        destroyCommentEditorCmd

    | GotSubmitComment (Error err) ->
        { model with Error = Some err }, Cmd.none

    | SetNewItemTitle title ->
        { model with ItemForm = { model.ItemForm with Title = title } }, Cmd.none

    | SetNewItemLink link ->
        { model with ItemForm = { model.ItemForm with Link = link } }, Cmd.none

    | SetNewItemTags tags ->
        { model with ItemForm = { model.ItemForm with Tags = tags } }, Cmd.none

    | SubmitItem ->
        let form = model.ItemForm
        let ownerComment = RichText.getEditorContent RichText.ownerCommentEditorId
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
              OwnerComment = ownerComment
              Tags = tags }
        { model with ItemForm = emptyItemForm },
        Cmd.OfPromise.either Client.Api.submitItem req GotSubmitItem (fun ex -> GotSubmitItem (Error ex.Message))

    | GotSubmitItem (Ok _) ->
        model, Cmd.batch [
            Cmd.ofMsg LoadFeed
            Cmd.ofEffect (fun _dispatch -> RichText.clearEditor RichText.ownerCommentEditorId)
        ]

    | GotSubmitItem (Error err) ->
        { model with Error = Some err }, Cmd.none

    | ToggleCollapse commentId ->
        let collapsed =
            if Set.contains commentId model.CollapsedComments then
                Set.remove commentId model.CollapsedComments
            else
                Set.add commentId model.CollapsedComments
        { model with CollapsedComments = collapsed }, Cmd.none

    | SetReplyTo (itemId, parentId) ->
        let cleanupCmd = destroyCommentEditorCmd
        let initCmd = initCommentEditorCmd
        { model with ReplyingTo = Some {| ItemId = itemId; ParentId = parentId |} },
        Cmd.batch [ cleanupCmd; initCmd ]

    | CancelReply ->
        { model with ReplyingTo = None },
        destroyCommentEditorCmd

module View =
    let private tagColors = [| "#e74c3c"; "#3498db"; "#2ecc71"; "#9b59b6"; "#f39c12"; "#1abc9c"; "#e91e63"; "#00bcd4" |]

    let private tagColor (name: string) =
        let hash = name.ToCharArray() |> Array.fold (fun acc c -> int c + acc * 31) 0
        tagColors.[abs hash % tagColors.Length]

    let tagPill (tag: string) =
        Html.span [
            prop.className "tag"
            prop.style [
                style.backgroundColor (tagColor tag)
                style.color "#fff"
                style.cursor.pointer
            ]
            prop.text tag
            prop.onClick (fun e ->
                e.stopPropagation ()
                Router.navigate ("tag", tag)
            )
        ]

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
                | Some (RichContent text) ->
                    Html.p [ prop.className "extract"; prop.text (RichText.extractPlainText text) ]
                | None -> Html.none
            ]
        ]

    let feed (response: GetFeed.Response) =
        Html.div [
            prop.className "feed"
            prop.children (response.Items |> List.map feedItem)
        ]

    let filterRootComments (comments: SubmitComment.CommentItem list) =
        comments |> List.filter (fun c -> c.ParentId.IsNone)

    let filterChildComments parentId (comments: SubmitComment.CommentItem list) =
        comments |> List.filter (fun c -> c.ParentId = Some parentId)

    let rec countAllReplies parentId (comments: SubmitComment.CommentItem list) =
        let children = filterChildComments parentId comments
        children.Length + (children |> List.sumBy (fun c -> countAllReplies c.Id comments))

    let replyForm (model: Model) (parentId: string option) dispatch =
        let isActive =
            match model.ReplyingTo with
            | Some rt -> rt.ParentId = parentId
            | None -> false
        if isActive then
            Html.div [
                prop.className "comment-form"
                prop.children [
                    Html.div [
                        prop.className "commenting-as"
                        prop.text (sprintf "Commenting as %s" model.GuestSession.DisplayName)
                    ]
                    Html.div [ prop.id RichText.commentEditorId ]
                    Html.div [
                        prop.children [
                            Html.button [
                                prop.text "Submit"
                                prop.onClick (fun _ -> dispatch SubmitComment)
                            ]
                            Html.button [
                                prop.className "comment-reply-btn"
                                prop.text "cancel"
                                prop.onClick (fun _ -> dispatch CancelReply)
                            ]
                        ]
                    ]
                ]
            ]
        else
            Html.none

    let rec commentView (model: Model) (allComments: SubmitComment.CommentItem list) (depth: int) dispatch (comment: SubmitComment.CommentItem) =
        let children = filterChildComments comment.Id allComments
        let hasChildren = not children.IsEmpty
        let isCollapsed = Set.contains comment.Id model.CollapsedComments
        let isRoot = depth = 0
        let depthClass = sprintf "depth-%d" (depth % 12)
        let classes =
            [ "comment-thread"
              depthClass
              if isRoot then "root-comment"
              if isCollapsed then "collapsed" ]
            |> String.concat " "
        Html.div [
            prop.className classes
            prop.children [
                if not isRoot then
                    Html.div [
                        prop.className "comment-collapse-line"
                        prop.onClick (fun _ -> dispatch (ToggleCollapse comment.Id))
                    ]
                Html.div [
                    prop.className "comment-content"
                    prop.children [
                        Html.div [
                            prop.className "comment-author"
                            prop.text comment.Author
                        ]
                        Html.div [ prop.id (sprintf "comment-%s" comment.Id) ]
                        Html.div [
                            prop.className "comment-meta"
                            prop.children [
                                if hasChildren then
                                    Html.button [
                                        prop.className "comment-collapse-toggle-inline"
                                        prop.text (if isCollapsed then "+" else "-")
                                        prop.onClick (fun _ -> dispatch (ToggleCollapse comment.Id))
                                    ]
                                if isCollapsed then
                                    let replyCount = countAllReplies comment.Id allComments
                                    Html.span [
                                        prop.className "comment-collapse-toggle-inline"
                                        prop.text (sprintf "(%d)" replyCount)
                                    ]
                                if not isCollapsed then
                                    Html.button [
                                        prop.className "comment-reply-btn"
                                        prop.text "reply"
                                        prop.onClick (fun _ ->
                                            match model.CurrentItem with
                                            | Some response -> dispatch (SetReplyTo (response.Item.Id, Some comment.Id))
                                            | None -> ()
                                        )
                                    ]
                            ]
                        ]
                        replyForm model (Some comment.Id) dispatch
                    ]
                ]
                Html.div [
                    prop.className "comment-children"
                    prop.children (
                        children |> List.map (commentView model allComments (depth + 1) dispatch)
                    )
                ]
            ]
        ]

    let itemDetail (response: GetItem.Response) (model: Model) dispatch =
        let item = response.Item
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
                | Some _ ->
                    Html.div [ prop.className "extract"; prop.id (sprintf "extract-%s" item.Id) ]
                | None -> Html.none
                Html.div [
                    prop.className "owner-comment"
                    prop.id (sprintf "owner-comment-%s" item.Id)
                ]
                if not item.Tags.IsEmpty then
                    Html.div [
                        prop.className "tags"
                        prop.children (item.Tags |> List.map tagPill)
                    ]
                Html.div [
                    prop.className "comments"
                    prop.children [
                        Html.h3 [ prop.text (sprintf "Comments (%d)" item.Comments.Length) ]
                        yield! filterRootComments item.Comments
                               |> List.map (commentView model item.Comments 0 dispatch)
                        replyForm model None dispatch
                        if model.ReplyingTo.IsNone then
                            Html.button [
                                prop.className "comment-reply-btn"
                                prop.text "Leave a comment"
                                prop.onClick (fun _ -> dispatch (SetReplyTo (item.Id, None)))
                            ]
                    ]
                ]
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
                Html.div [ prop.id RichText.ownerCommentEditorId ]
                Html.input [
                    prop.placeholder "Tags (comma-separated)"
                    prop.value form.Tags
                    prop.onChange (SetNewItemTags >> dispatch)
                ]
                Html.button [
                    prop.text "Create"
                    prop.disabled (form.Title.Trim() = "")
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

    let tagItemsView (response: GetItemsByTag.Response) =
        Html.div [
            prop.className "feed"
            prop.children [
                Html.div [
                    prop.className "tag-header"
                    prop.children [ tagPill response.Tag ]
                ]
                yield! response.Items |> List.map feedItem
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
                            | Some response -> itemDetail response model dispatch
                            | None -> Html.p [ prop.text "Item not found." ]
                        | ["tag"; _] ->
                            match model.TagItems with
                            | Some response -> tagItemsView response
                            | None -> Html.p [ prop.text "No items for this tag." ]
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
