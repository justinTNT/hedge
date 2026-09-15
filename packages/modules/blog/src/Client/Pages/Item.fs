module Blog.Client.Pages.Item

// The shared guest-session accessor is host-provided (packages/hedge/src/Client);
// alias it locally, the same way RichText is aliased.
module GuestSession = Client.GuestSession

// The shared rich-text module lives in the host's Client.RichText namespace.
module RichText = Client.RichText

open Fable.Core.JsInterop
open Feliz
open Elmish
open Blog.Client
open Hedge.Interface
open Blog.Api
open Blog.ClientGen
open Blog.Client.Types
open Blog.Client.Shared

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
                    match blogDecodeWsEvent text with
                    | Ok (BlogNewComment event) -> dispatch (GotEvent event)
                    | Error err -> dispatch (EventError err))
                (fun _ -> dispatch (EventError "WebSocket error"))
        currentWsClose <- Some close
    )

/// Close the live-events socket now (synchronous, idempotent). Exposed as a plain
/// function so a host can dispose the outgoing module's resources in order, before
/// entering the incoming one.
let disconnectEvents () : unit =
    match currentWsClose with
    | Some close ->
        close ()
        currentWsClose <- None
    | None -> ()

let disconnectEventsCmd () : Cmd<Msg> =
    Cmd.ofEffect (fun _dispatch -> disconnectEvents ())

// --- Rich text editor lifecycle ---

let mutable private commentEditorActive = false

let initCommentEditorCmd : Cmd<Msg> =
    Cmd.ofEffect (fun dispatch ->
        if not commentEditorActive then
            commentEditorActive <- true
            RichText.createEditorWithClose RichText.commentEditorId "" (fun () -> dispatch CancelReply)
    )

/// Destroy the comment editor now (synchronous, idempotent). Plain function for
/// ordered host-driven disposal (see disconnectEvents).
let destroyCommentEditor () : unit =
    if commentEditorActive then
        RichText.destroyEditor RichText.commentEditorId
        commentEditorActive <- false

let destroyCommentEditorCmd : Cmd<Msg> =
    Cmd.ofEffect (fun _dispatch -> destroyCommentEditor ())

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

/// Fetch the latest source-page snapshot pointer for an item — archive-feature tenants only, so
/// non-archive sites make no extra request. Best-effort: any failure shows no archived copy.
let private loadSnapshotCmd (itemId: string) : Cmd<Msg> =
    if Hedge.Tenant.hasFeature "archive" then
        Cmd.OfPromise.either
            (fun () -> Client.Api.fetchJsonRaw (sprintf "/api/blog/item/%s/snapshot" itemId))
            ()
            (fun (o: obj) -> let id : string = o?id in GotSnapshot (if isNull (box id) then None else Some id))
            (fun _ -> GotSnapshot None)
    else Cmd.none

let update msg model =
    match msg with
    | LoadItem itemId ->
        { model with IsLoading = true; CurrentItem = None },
        Cmd.OfPromise.either Blog.ClientGen.blogGetItem itemId GotItem (fun ex -> GotItem (Error ex.Message))

    | GotItem (Ok response) ->
        { model with CurrentItem = Some response; IsLoading = false },
        Cmd.batch [ connectEventsCmd response.Item.Id; loadSnapshotCmd response.Item.Id ]

    | GotItem (Error err) ->
        { model with IsLoading = false; Error = Some err }, Cmd.none

    | GotSnapshot id ->
        { model with Snapshot = id }, Cmd.none

    | ToggleArchive ->
        { model with ShowArchive = not model.ShowArchive }, Cmd.none

    | ConnectEvents itemId ->
        model, connectEventsCmd itemId

    | DisconnectEvents ->
        model, disconnectEventsCmd ()

    | GotEvent event ->
        // Unwrap the event's typed reference ids back to plain strings for the view DTO.
        let (ForeignKey eventItemId) = event.ItemId
        let (IdentityRef eventIdentityId) = event.IdentityId
        match model.CurrentItem with
        | Some response when response.Item.Id = eventItemId ->
            let newComment : SubmitComment.CommentItem =
                { Id = event.Id
                  ItemId = eventItemId
                  IdentityId = eventIdentityId
                  ParentId = event.ParentId |> Option.map (fun (ForeignKey p) -> p)
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
                { ItemId = ForeignKey response.Item.Id
                  ParentId = parentId |> Option.map ForeignKey
                  Content = text
                  Author = Some model.GuestSession.DisplayName }
            model,
            Cmd.OfPromise.either Blog.ClientGen.blogSubmitComment req GotSubmitComment (fun ex -> GotSubmitComment (Error ex.Message))
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

let view (ctx: Content.HostContext) (response: GetItem.Response) (model: Model) dispatch =
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
            // Archived copy of the source page (archive-feature tenants, when a snapshot exists).
            // The snapshot is served under a locked-down CSP; the iframe sandbox (no scripts, no
            // same-origin) is the belt to that CSP's braces.
            match (if Hedge.Tenant.hasFeature "archive" then model.Snapshot else None) with
            | Some sid ->
                Html.div [
                    prop.className "archive-affordance"
                    prop.children [
                        Html.button [
                            prop.className "archive-toggle"
                            prop.text (if model.ShowArchive then "Hide archived copy" else "View archived copy")
                            prop.onClick (fun _ -> dispatch ToggleArchive)
                        ]
                        if model.ShowArchive then
                            Html.iframe [
                                prop.className "archive-frame"
                                prop.src ("/archive/" + sid)
                                prop.custom ("sandbox", "allow-popups")
                                prop.custom ("loading", "lazy")
                            ]
                    ]
                ]
            | None -> Html.none
            match item.Extract with
            | Some extract -> richContent "extract" extract
            | None -> Html.none
            richContent "owner-comment" item.OwnerComment
            if not item.Tags.IsEmpty then
                Html.div [
                    prop.className "tags"
                    prop.children (item.Tags |> List.map (tagPill ctx))
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
