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

let initCommentEditorCmd : Cmd<Msg> =
    Cmd.ofEffect (fun dispatch ->
        if not commentEditorActive then
            commentEditorActive <- true
            RichText.createEditorWithClose RichText.commentEditorId "" (fun () -> dispatch CancelReply)
    )

let destroyCommentEditorCmd : Cmd<Msg> =
    Cmd.ofEffect (fun _dispatch ->
        if commentEditorActive then
            RichText.destroyEditor RichText.commentEditorId
            commentEditorActive <- false
    )

/// Rich content is rendered declaratively (see `richContent`), so there are no
/// viewer instances to tear down. Kept as a no-op command so the route-change
/// cleanup batch in App.fs stays uniform.
let destroyAllViewersCmd : Cmd<Msg> = Cmd.none

/// Stored content -> markup, as a pure function of the model. The HTML is
/// produced by the same TipTap schema the editor writes with, so anything
/// outside that schema is dropped rather than passed through.
let private richContent (className: string) (content: RichContent) =
    let (RichContent text) = content
    Html.div [
        prop.className (className + " hamlet-rt-viewer")
        prop.dangerouslySetInnerHTML (RichText.toHtml text)
    ]

// --- Update ---

let update msg model =
    match msg with
    | LoadItem itemId ->
        { model with IsLoading = true; CurrentItem = None },
        Cmd.OfPromise.either Client.ClientGen.getItem itemId GotItem (fun ex -> GotItem (Error ex.Message))

    | GotItem (Ok response) ->
        { model with CurrentItem = Some response; IsLoading = false },
        connectEventsCmd response.Item.Id

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
                  IdentityId = event.IdentityId
                  ParentId = event.ParentId
                  Author = event.Author
                  Picture = event.Picture
                  Content = RichContent event.Content
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
                    prop.children [
                        avatar model.GuestSession.AvatarUrl
                        Html.span [ prop.text (sprintf "Commenting as %s" model.GuestSession.DisplayName) ]
                    ]
                ]
                Html.div [ prop.id RichText.commentEditorId ]
                Html.button [
                    prop.text "Submit"
                    prop.onClick (fun _ -> dispatch SubmitComment)
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
                        prop.children [
                            avatar (if comment.Picture <> "" then comment.Picture else GuestSession.avatarForAuthor comment.Author)
                            Html.span [ prop.text comment.Author ]
                        ]
                    ]
                    richContent "comment-body" comment.Content
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
            match item.Link with
            | Some (Link url) ->
                Html.h2 [
                    Html.a [
                        prop.className "main-link"
                        prop.href url
                        prop.target "_blank"
                        prop.rel "noopener"
                        prop.text item.Title
                    ]
                ]
            | None ->
                Html.h2 [ prop.text item.Title ]
            match item.Image with
            | Some (Link imgUrl) ->
                Html.img [ prop.src imgUrl; prop.className "item-image" ]
            | None -> Html.none
            match item.Extract with
            | Some extract -> richContent "extract" extract
            | None -> Html.none
            richContent "owner-comment" item.OwnerComment
            if not item.Tags.IsEmpty then
                Html.div [
                    prop.className "tags"
                    prop.children (item.Tags |> List.map tagPill)
                ]
            Html.div [
                prop.className "comments"
                prop.children [
                    if item.Comments.Length > 0 then
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
