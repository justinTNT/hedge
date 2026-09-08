module Models.Api

open Hedge.Interface

module GetArticles =
    /// List/feed view of an article — no body, just enough for the list.
    type ArticleItem = {
        Id: string
        Title: string
        Slug: string option
        Image: string option
        Teaser: RichContent option
        Timestamp: int
    }

    type Response = {
        Items: ArticleItem list
        NextCursor: string option
    }

    // Cursor-paginated. Page 1 uses the sentinel "start"; later pages pass the
    // previous response's NextCursor token.
    let endpoint : GetOne<Response> = GetOne (sprintf "/api/articles/%s")

module SubmitComment =
    type CommentItem = {
        Id: string
        ArticleId: string
        IdentityId: string
        ParentId: string option
        Author: string
        Picture: string
        Content: RichContent
        Timestamp: int
    }

    type Request = {
        ArticleId: string
        ParentId: string option
        Content: string
        Author: string option
    }

    type ServerContext = {
        FreshGuestId: string
        FreshCommentId: string
    }

    type Response = {
        Comment: CommentItem
    }

    let endpoint : Post<Request, Response> = Post "/api/comment"

module GetArticle =
    /// Full article for the detail page — includes the body and its comments.
    type ArticleDetail = {
        Id: string
        Title: string
        Slug: string option
        Image: string option
        Teaser: RichContent option
        Body: RichContent
        Comments: SubmitComment.CommentItem list
        Timestamp: int
    }

    type Response = {
        Article: ArticleDetail
    }

    // Path param carries the slug (falls back to id in the handler).
    let endpoint : GetOne<Response> = GetOne (sprintf "/api/article/%s")

module Events =
    let endpoint : Get<unit> = Get "/api/events"
