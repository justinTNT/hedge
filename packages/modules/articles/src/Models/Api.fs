module Articles.Api

// The articles module's API surface. Paths are module-relative ("/api/...") — the
// site's Gen composition applies the mount's route prefix (e.g. -> "/api/articles/...").

open Hedge.Interface

module GetFeed =
    /// List/feed view of a post — no body, just enough for the list.
    type FeedItem = {
        Id: string
        Title: string
        Slug: string option
        Image: string option
        Teaser: RichContent option
        Timestamp: int
    }

    /// Cursor-paginated. Page 1 omits ?cursor; later pages pass the previous
    /// response's NextCursor token.
    type Query = { Cursor: string option }

    type Response = {
        Items: FeedItem list
        NextCursor: string option
    }

    let endpoint : GetQuery<Query, Response> = GetQuery "/api/feed"

module SubmitComment =
    type CommentItem = {
        Id: string
        PostId: string
        IdentityId: string
        ParentId: string option
        Author: string
        Picture: string
        Content: RichContent
        Timestamp: int
    }

    type Request = {
        // Typed ids: the client can't transpose the post vs the parent comment
        // (distinct phantom types). Serialize as bare strings (byte-identical wire).
        PostId: ForeignKey<Articles.Domain.Post>
        ParentId: ForeignKey<Articles.Domain.Comment> option
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

module GetPost =
    /// Full post for the detail page — includes the body and its comments.
    type PostDetail = {
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
        Post: PostDetail
    }

    // Path param carries the slug (falls back to id in the handler).
    let endpoint : GetBy<Response> = GetBy (sprintf "/api/post/%s")

module Events =
    let endpoint : Get<unit> = Get "/api/events"
