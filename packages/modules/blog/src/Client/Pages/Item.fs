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

let initCommentEditorCmd (initial: string) : Cmd<Msg> =
    Cmd.ofEffect (fun dispatch ->
        if not commentEditorActive then
            commentEditorActive <- true
            // C1b: seed from the model draft, report edits back via onChange, and keep the
            // guest upload endpoint the close-button editor used. Submission reads the model.
            RichText.createEditorScoped RichText.commentEditorId initial
                (fun text -> dispatch (SetCommentDraft text))
                (fun () -> dispatch CancelReply)
                "/api/blobs/guest"
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

let update (deps: Deps) msg model =
    match msg with
    | LoadItem itemId ->
        let gen = model.LoadGen + 1
        { model with IsLoading = true; CurrentItem = None; LoadGen = gen },
        Cmd.OfPromise.either deps.Api.blogGetItem itemId
            (fun r -> GotItem (gen, r)) (fun ex -> GotItem (gen, Error (Hedge.Http.TransportFailure ex.Message)))

    // CP-A: drop a completion from a superseded read generation (a reverse-order resolve after
    // switching items, or a read for an item we've since left). This is the correctness
    // authority; the Route/id check below is secondary. It also stops the inactive-child
    // resurrection: a stale GotItem never reaches connectEventsCmd / SetDocTitle.
    | GotItem (gen, _) when gen <> model.LoadGen -> model, Cmd.none

    | GotItem (_, Ok response) ->
        match model.Route with
        | [ idOrSlug ] when response.Item.Id = idOrSlug || response.Item.Slug = Some idOrSlug ->
            // Clear any prior Error so a stale failure can't linger behind a later success.
            { model with CurrentItem = Some response; IsLoading = false; Error = None },
            connectEventsCmd response.Item.Id
        | _ -> model, Cmd.none

    | GotItem (_, Error err) ->
        { model with IsLoading = false; Error = Some err }, Cmd.none

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
            // C1b: build from the model draft (kept current by SetCommentDraft), not a DOM read.
            let text = model.CommentDraft
            let parentId =
                match model.ReplyingTo with
                | Some rt -> rt.ParentId
                | None -> None
            let req : SubmitComment.Request =
                { ItemId = ForeignKey response.Item.Id
                  ParentId = parentId |> Option.map ForeignKey
                  Content = text
                  Author = Some model.GuestSession.DisplayName }
            let rev = model.DraftRev
            model,
            Cmd.OfPromise.either deps.Api.blogSubmitComment req
                (fun r -> GotSubmitComment (rev, r)) (fun ex -> GotSubmitComment (rev, Error (Hedge.Http.TransportFailure ex.Message)))
        | None -> model, Cmd.none

    | GotSubmitComment (rev, Ok resp) ->
        // The comment appends via the WS GotEvent; this only tears down the reply box. Writes are
        // NOT gen-filtered (the outcome must deliver), but the draft is cleared only if the comment
        // belongs to the item still shown AND no newer edit happened since this submit (rev
        // unchanged) — so a late success can't erase a newer draft (CP-A finding 2).
        match model.CurrentItem with
        | Some response when response.Item.Id = resp.Comment.ItemId && rev = model.DraftRev ->
            { model with ReplyingTo = None; CommentDraft = "" }, destroyCommentEditorCmd
        | _ -> model, Cmd.none

    | GotSubmitComment (_, Error err) ->
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
        let initCmd = initCommentEditorCmd model.CommentDraft
        { model with ReplyingTo = Some {| ItemId = itemId; ParentId = parentId |} },
        Cmd.batch [ cleanupCmd; initCmd ]

    | SetCommentDraft text ->
        // Bump the draft revision so a submission already in flight won't clear this newer text.
        { model with CommentDraft = text; DraftRev = model.DraftRev + 1 }, Cmd.none

    | CancelReply ->
        // Keep the draft — closing the reply box preserves in-progress text so reopening the
        // box on the same item restores it. Navigation away clears it (App.enterHosted).
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
            | Some (Image imgUrl) ->
                Html.img [ prop.src imgUrl; prop.className "item-image" ]
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
