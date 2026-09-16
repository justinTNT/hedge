module Articles.Client.Pages.Item

// The shared guest-session accessor is host-provided (packages/hedge/src/Client);
// alias it locally, the same way RichText is aliased.
module GuestSession = Client.GuestSession

// The shared rich-text module lives in the host's Client.RichText namespace.
module RichText = Client.RichText

open Fable.Core.JsInterop
open Feliz
open Elmish
open Articles.Client
open Hedge.Interface
open Articles.Api
open Articles.ClientGen
open Articles.Client.Types
open Articles.Client.Shared

// --- WebSocket management ---

let mutable private currentWsClose : (unit -> unit) option = None

let connectEventsCmd (postId: string) : Cmd<Msg> =
    Cmd.ofEffect (fun dispatch ->
        match currentWsClose with
        | Some close -> close ()
        | None -> ()
        let url = sprintf "%s/api/events?itemId=%s" (Client.Api.wsBase()) postId
        let close =
            Client.Api.openWebSocket
                url
                (fun e ->
                    let text : string = e?data |> string
                    match articlesDecodeWsEvent text with
                    | Ok (ArticlesNewComment event) -> dispatch (GotEvent event)
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

/// Rich content is rendered declaratively, so there's nothing to tear down.
let destroyAllViewersCmd : Cmd<Msg> = Cmd.none

/// Stored content -> markup, as a pure function of the model.
let private richContent (className: string) (content: RichContent) =
    let (RichContent text) = content
    Html.div [
        prop.className (className + " hamlet-rt-viewer")
        prop.dangerouslySetInnerHTML (RichText.toHtml text)
    ]

// --- Update ---

let update (ctx: Content.HostContext) msg model =
    match msg with
    | LoadItem idOrSlug ->
        let gen = model.LoadGen + 1
        { model with IsLoading = true; CurrentItem = None; LoadGen = gen },
        Cmd.OfPromise.either Articles.Client.Shared.Api.articlesGetPost idOrSlug
            (fun r -> GotItem (gen, r)) (fun ex -> GotItem (gen, Error ex.Message))

    // CP-A: drop a completion from a superseded read generation — the correctness authority.
    // Gen-gating this also stops an inactive module from setting the tab title / reopening a
    // socket after the host switched away (finding 1); the Route/id check is secondary.
    | GotItem (gen, _) when gen <> model.LoadGen -> model, Cmd.none

    | GotItem (_, Ok response) ->
        match model.Route with
        | [ idOrSlug ] when response.Post.Id = idOrSlug || response.Post.Slug = Some idOrSlug ->
            { model with CurrentItem = Some response; IsLoading = false; Error = None },
            Cmd.batch [
                Cmd.ofEffect (fun _ -> ctx.SetDocTitle response.Post.Title)
                connectEventsCmd response.Post.Id
            ]
        | _ -> model, Cmd.none

    | GotItem (_, Error err) ->
        { model with IsLoading = false; Error = Some err }, Cmd.none

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
                { PostId = ForeignKey response.Post.Id
                  ParentId = parentId |> Option.map ForeignKey
                  Content = text
                  Author = Some model.GuestSession.DisplayName }
            // CP-A finding 2: capture the draft revision this submit belongs to.
            let rev = model.DraftRev
            model,
            Cmd.OfPromise.either Articles.Client.Shared.Api.articlesSubmitComment req
                (fun r -> GotSubmitComment (rev, r)) (fun ex -> GotSubmitComment (rev, Error ex.Message))
        | None -> model, Cmd.none

    | GotSubmitComment (rev, Ok resp) ->
        // Append only if the comment belongs to the post still shown (a stale success from a
        // since-left post must not append to a different post). No WS echo here, so the append
        // happens on success — and a write must deliver even when late, so it is NOT gen-filtered.
        match model.CurrentItem with
        | Some r when r.Post.Id = resp.Comment.PostId ->
            let alreadyHas = r.Post.Comments |> List.exists (fun c -> c.Id = resp.Comment.Id)
            let updatedItem =
                if alreadyHas then r
                else { r with Post = { r.Post with Comments = r.Post.Comments @ [ resp.Comment ] } }
            // CP-A finding 2: clear the draft/editor only if no newer edit happened since this
            // submit (rev unchanged) — a late success must not erase a newer draft.
            if rev = model.DraftRev then
                { model with CurrentItem = Some updatedItem; ReplyingTo = None; CommentDraft = "" }, destroyCommentEditorCmd
            else
                { model with CurrentItem = Some updatedItem }, Cmd.none
        | _ -> model, Cmd.none

    | GotSubmitComment (_, Error err) ->
        { model with Error = Some err }, Cmd.none

    | ToggleCollapse commentId ->
        let collapsed =
            if Set.contains commentId model.CollapsedComments then Set.remove commentId model.CollapsedComments
            else Set.add commentId model.CollapsedComments
        { model with CollapsedComments = collapsed }, Cmd.none

    | SetReplyTo (postId, parentId) ->
        { model with ReplyingTo = Some {| PostId = postId; ParentId = parentId |} },
        Cmd.batch [ destroyCommentEditorCmd; initCommentEditorCmd model.CommentDraft ]

    | SetCommentDraft text ->
        { model with CommentDraft = text; DraftRev = model.DraftRev + 1 }, Cmd.none

    | CancelReply ->
        // Keep the draft — closing the reply box preserves in-progress text so reopening the
        // box on the same post restores it. Navigation away clears it (App.enterHosted).
        { model with ReplyingTo = None }, destroyCommentEditorCmd

    | ConnectEvents postId ->
        model, connectEventsCmd postId

    | DisconnectEvents ->
        model, disconnectEventsCmd ()

    | GotEvent event ->
        // Unwrap the event's typed reference ids back to plain strings for the view DTO.
        let (ForeignKey eventPostId) = event.PostId
        let (IdentityRef eventIdentityId) = event.IdentityId
        match model.CurrentItem with
        | Some response when response.Post.Id = eventPostId ->
            let existingIds = response.Post.Comments |> List.map (fun c -> c.Id) |> Set.ofList
            if Set.contains event.Id existingIds then
                model, Cmd.none
            else
                let newComment : SubmitComment.CommentItem =
                    { Id = event.Id
                      PostId = eventPostId
                      IdentityId = eventIdentityId
                      ParentId = event.ParentId |> Option.map (fun (ForeignKey p) -> p)
                      Author = event.Author
                      Picture = event.Picture
                      Content = RichContent event.Content
                      Timestamp = event.Timestamp }
                { model with CurrentItem = Some { response with Post = { response.Post with Comments = response.Post.Comments @ [ newComment ] } } }, Cmd.none
        | _ -> model, Cmd.none

    | EventError _ ->
        model, Cmd.none

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
                                        | Some response -> dispatch (SetReplyTo (response.Post.Id, Some comment.Id))
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

let view (response: GetPost.Response) (model: Model) dispatch =
    let post = response.Post
    Html.div [
        prop.className "item-detail article-detail"
        prop.children [
            Html.h1 [ prop.className "article-title"; prop.text post.Title ]
            // The post body is self-contained (its own images). The hero Image is
            // only for the feed thumbnail, and the teaser is the list preview —
            // showing either here would double the body's opening/image.
            richContent "article-body" post.Body
            Html.div [
                prop.className "comments"
                prop.children [
                    if post.Comments.Length > 0 then
                        Html.h3 [ prop.text (sprintf "Comments (%d)" post.Comments.Length) ]
                    yield! filterRootComments post.Comments
                           |> List.map (commentView model post.Comments 0 dispatch)
                    replyForm model None dispatch
                    if model.ReplyingTo.IsNone then
                        Html.button [
                            prop.className "comment-reply-btn"
                            prop.text "Leave a comment"
                            prop.onClick (fun _ -> dispatch (SetReplyTo (post.Id, None)))
                        ]
                ]
            ]
        ]
    ]
