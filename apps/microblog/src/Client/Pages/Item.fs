module Client.Pages.Item

open Fable.Core.JsInterop
open Feliz
open Elmish
open Client
open Hedge.Interface
open Models.Api
open Client.ClientGen
open Client.Types
open Client.Shared

// --- WebSocket management ---

let mutable private currentWsClose : (unit -> unit) option = None

let connectEventsCmd (itemId: string) : Cmd<Msg> =
    Cmd.ofEffect (fun dispatch ->
        match currentWsClose with
        | Some close -> close ()
        | None -> ()
        let url = sprintf "%s/api/events?itemId=%s" (Client.Api.wsBase()) itemId
        let close =
            Client.Api.openWebSocket
                url
                (fun e ->
                    let text : string = e?data |> string
                    match decodeWsEvent text with
                    | Ok (NewComment event) -> dispatch (GotEvent event)
                    | Ok _ -> ()
                    | Error err -> dispatch (EventError err))
                (fun _ -> dispatch (EventError "WebSocket error"))
        currentWsClose <- Some close
    )

let disconnectEventsCmd () : Cmd<Msg> =
    Cmd.ofEffect (fun _dispatch ->
        match currentWsClose with
        | Some close ->
            close ()
            currentWsClose <- None
        | None -> ()
    )

// --- Rich text editor lifecycle ---

let mutable private commentEditorActive = false
let mutable private activeViewerIds : string list = []

let initCommentEditorCmd : Cmd<Msg> =
    Cmd.ofEffect (fun _dispatch ->
        if not commentEditorActive then
            commentEditorActive <- true
            RichText.createEditorWhenReady RichText.commentEditorId ""
    )

let destroyCommentEditorCmd : Cmd<Msg> =
    Cmd.ofEffect (fun _dispatch ->
        if commentEditorActive then
            RichText.destroyEditor RichText.commentEditorId
            commentEditorActive <- false
    )

let destroyAllViewersCmd : Cmd<Msg> =
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

// --- Update ---

let update msg model =
    match msg with
    | LoadItem itemId ->
        { model with IsLoading = true; CurrentItem = None },
        Cmd.OfPromise.either Client.ClientGen.getItem itemId GotItem (fun ex -> GotItem (Error ex.Message))

    | GotItem (Ok response) ->
        { model with CurrentItem = Some response; IsLoading = false },
        Cmd.batch [
            connectEventsCmd response.Item.Id
            initViewersForItemCmd response
        ]

    | GotItem (Error err) ->
        { model with IsLoading = false; Error = Some err }, Cmd.none

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
            Cmd.OfPromise.either Client.ClientGen.submitComment req GotSubmitComment (fun ex -> GotSubmitComment (Error ex.Message))
        | None -> model, Cmd.none

    | GotSubmitComment (Ok _) ->
        { model with ReplyingTo = None },
        destroyCommentEditorCmd

    | GotSubmitComment (Error err) ->
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

    | _ -> model, Cmd.none

// --- Views ---

let private filterRootComments (comments: SubmitComment.CommentItem list) =
    comments |> List.filter (fun c -> c.ParentId.IsNone)

let private filterChildComments parentId (comments: SubmitComment.CommentItem list) =
    comments |> List.filter (fun c -> c.ParentId = Some parentId)

let rec private countAllReplies parentId (comments: SubmitComment.CommentItem list) =
    let children = filterChildComments parentId comments
    children.Length + (children |> List.sumBy (fun c -> countAllReplies c.Id comments))

let private replyForm (model: Model) (parentId: string option) dispatch =
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

let rec private commentView (model: Model) (allComments: SubmitComment.CommentItem list) (depth: int) dispatch (comment: SubmitComment.CommentItem) =
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

let view (response: GetItem.Response) (model: Model) dispatch =
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
