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

// Module-owned editor mount id — distinct from Blog's so both comment views can
// coexist without both grabbing the same element. Used for create/destroy AND passed
// to the shared Content.Comments renderer as its EditorId.
let private commentEditorId = "article-comment-editor"

let initCommentEditorCmd (initial: string) : Cmd<Msg> =
    Cmd.ofEffect (fun dispatch ->
        if not commentEditorActive then
            commentEditorActive <- true
            // C1b: seed from the model draft, report edits back via onChange, and keep the
            // guest upload endpoint the close-button editor used. Submission reads the model.
            RichText.createEditorScoped commentEditorId initial
                (fun text -> dispatch (SetCommentDraft text))
                (fun () -> dispatch CancelReply)
                "/api/blobs/guest"
    )

/// Destroy the comment editor now (synchronous, idempotent). Plain function for
/// ordered host-driven disposal (see disconnectEvents).
let destroyCommentEditor () : unit =
    if commentEditorActive then
        RichText.destroyEditor commentEditorId
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

let update (deps: Deps) msg model =
    match msg with
    | LoadItem idOrSlug ->
        let gen = model.LoadGen + 1
        { model with IsLoading = true; CurrentItem = None; LoadGen = gen },
        Cmd.OfPromise.either deps.Api.articlesGetPost idOrSlug
            (fun r -> GotItem (gen, r)) (fun ex -> GotItem (gen, Error (Hedge.Http.TransportFailure ex.Message)))

    // CP-A: drop a completion from a superseded read generation — the correctness authority.
    // Gen-gating this also stops an inactive module from setting the tab title / reopening a
    // socket after the host switched away (finding 1); the Route/id check is secondary.
    | GotItem (gen, _) when gen <> model.LoadGen -> model, Cmd.none

    | GotItem (_, Ok response) ->
        match model.Route with
        | [ idOrSlug ] when response.Post.Id = idOrSlug || response.Post.Slug = Some idOrSlug ->
            { model with CurrentItem = Some response; IsLoading = false; Error = None },
            Cmd.batch [
                Cmd.ofEffect (fun _ -> deps.Ctx.SetDocTitle response.Post.Title)
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
            Cmd.OfPromise.either deps.Api.articlesSubmitComment req
                (fun r -> GotSubmitComment (rev, r)) (fun ex -> GotSubmitComment (rev, Error (Hedge.Http.TransportFailure ex.Message)))
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

/// Map this module's comment rows to the shared presentation records and render
/// the whole `.comments` section through Content.Comments. Draft/collapse/reply
/// state, the submit request, and the editor lifecycle stay in this module (the
/// adapter just wires callbacks); the parent post id is captured here.
let private commentsSection (response: GetPost.Response) (model: Model) dispatch =
    let comments =
        response.Post.Comments
        |> List.map (fun c ->
            { Content.Comments.Id = c.Id
              Content.Comments.ParentId = c.ParentId
              Content.Comments.Author = c.Author
              Content.Comments.AvatarUrl =
                if c.Picture <> "" then c.Picture else GuestSession.avatarForAuthor c.Author
              Content.Comments.Body = c.Content })
    let active =
        match model.ReplyingTo with
        | None -> Content.Comments.NoReply
        | Some rt ->
            match rt.ParentId with
            | None -> Content.Comments.ReplyRoot
            | Some cid -> Content.Comments.ReplyTo cid
    Content.Comments.view
        { Comments = comments
          Collapsed = model.CollapsedComments
          Active = active
          CurrentAuthor = model.GuestSession.DisplayName
          CurrentAuthorAvatarUrl = model.GuestSession.AvatarUrl
          EditorId = commentEditorId
          OnToggleCollapse = fun id -> dispatch (ToggleCollapse id)
          OnBeginReply = fun parentId -> dispatch (SetReplyTo (response.Post.Id, parentId))
          OnSubmit = fun () -> dispatch SubmitComment }

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
            commentsSection response model dispatch
        ]
    ]
