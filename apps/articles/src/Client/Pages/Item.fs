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

let update msg model =
    match msg with
    | LoadItem idOrSlug ->
        { model with IsLoading = true; CurrentItem = None },
        Cmd.OfPromise.either Client.ClientGen.getArticle idOrSlug GotItem (fun ex -> GotItem (Error ex.Message))

    | GotItem (Ok response) ->
        { model with CurrentItem = Some response; IsLoading = false }, Cmd.none

    | GotItem (Error err) ->
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
                { ArticleId = response.Article.Id
                  ParentId = parentId
                  Content = text
                  Author = Some model.GuestSession.DisplayName }
            model,
            Cmd.OfPromise.either Client.ClientGen.submitComment req GotSubmitComment (fun ex -> GotSubmitComment (Error ex.Message))
        | None -> model, Cmd.none

    | GotSubmitComment (Ok resp) ->
        // No live WS echo — append the returned comment locally so it shows now.
        let updated =
            match model.CurrentItem with
            | Some r when r.Article.Comments |> List.exists (fun c -> c.Id = resp.Comment.Id) |> not ->
                { model with CurrentItem = Some { r with Article = { r.Article with Comments = r.Article.Comments @ [ resp.Comment ] } } }
            | _ -> model
        { updated with ReplyingTo = None }, destroyCommentEditorCmd

    | GotSubmitComment (Error err) ->
        { model with Error = Some err }, Cmd.none

    | ToggleCollapse commentId ->
        let collapsed =
            if Set.contains commentId model.CollapsedComments then Set.remove commentId model.CollapsedComments
            else Set.add commentId model.CollapsedComments
        { model with CollapsedComments = collapsed }, Cmd.none

    | SetReplyTo (articleId, parentId) ->
        { model with ReplyingTo = Some {| ArticleId = articleId; ParentId = parentId |} },
        Cmd.batch [ destroyCommentEditorCmd; initCommentEditorCmd ]

    | CancelReply ->
        { model with ReplyingTo = None }, destroyCommentEditorCmd

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
                                        | Some response -> dispatch (SetReplyTo (response.Article.Id, Some comment.Id))
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

let view (response: GetArticle.Response) (model: Model) dispatch =
    let article = response.Article
    Html.div [
        prop.className "item-detail article-detail"
        prop.children [
            Html.h1 [ prop.className "article-title"; prop.text article.Title ]
            match article.Image with
            | Some imgUrl -> Html.img [ prop.src imgUrl; prop.className "item-image" ]
            | None -> Html.none
            match article.Teaser with
            | Some teaser -> richContent "teaser" teaser
            | None -> Html.none
            richContent "article-body" article.Body
            Html.div [
                prop.className "comments"
                prop.children [
                    if article.Comments.Length > 0 then
                        Html.h3 [ prop.text (sprintf "Comments (%d)" article.Comments.Length) ]
                    yield! filterRootComments article.Comments
                           |> List.map (commentView model article.Comments 0 dispatch)
                    replyForm model None dispatch
                    if model.ReplyingTo.IsNone then
                        Html.button [
                            prop.className "comment-reply-btn"
                            prop.text "Leave a comment"
                            prop.onClick (fun _ -> dispatch (SetReplyTo (article.Id, None)))
                        ]
                ]
            ]
        ]
    ]
