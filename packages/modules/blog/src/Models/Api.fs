module Blog.Api

// The blog module's API surface. Paths are module-relative ("/api/...") — the
// site's Gen composition applies the mount's route prefix (e.g. -> "/api/blog/...").
// darwin.news-specific views (rhyming) stay in the microblog app, not the module.

open Hedge.Interface

module GetFeed =
    type FeedItem = {
        Id: string
        Title: string
        Slug: string option
        Image: string option
        Extract: RichContent option
        OwnerComment: RichContent
        Timestamp: int
    }

    /// Cursor-paginated (infinite scroll). Page 1 omits ?cursor; later pages pass
    /// the previous response's NextCursor token.
    type Query = { Cursor: string option }

    type Response = {
        Items: FeedItem list
        NextCursor: string option
    }

    let endpoint : GetQuery<Query, Response> = GetQuery "/api/feed"

module SubmitComment =
    type CommentItem = {
        Id: string
        ItemId: string
        IdentityId: string
        ParentId: string option
        Author: string
        Picture: string
        Content: RichContent
        Timestamp: int
    }

    type Request = {
        // Typed ids: the client can't transpose the item vs the parent comment
        // (distinct phantom types). Serialize as bare strings (byte-identical wire).
        ItemId: ForeignKey<Blog.Domain.Item>
        ParentId: ForeignKey<Blog.Domain.ItemComment> option
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

module SubmitItem =
    type Item = {
        Id: string
        Title: string
        Slug: string option
        Link: Link option
        Image: Link option
        Extract: RichContent option
        OwnerComment: RichContent
        Tags: string list
        Comments: SubmitComment.CommentItem list
        Timestamp: int
    }

    type Request = {
        Title: string
        Slug: string option
        Link: string option
        Image: string option
        Extract: string option
        OwnerComment: string
        Tags: string list
    }

    type ServerContext = {
        FreshTagIds: string list
    }

    type Response = {
        Item: Item
    }

    let endpoint : Post<Request, Response> = Post "/api/item"

module GetItem =
    type Response = {
        Item: SubmitItem.Item
    }

    let endpoint : GetBy<Response> = GetBy (sprintf "/api/item/%s")

module GetTags =
    type Response = {
        Tags: string list
    }

    let endpoint : Get<Response> = Get "/api/tags"

module GetItemsByTag =
    /// The tag is the path param; pagination is a proper query param (page 1 omits
    /// ?cursor). No more smuggling both through one "tag~cursor" path segment.
    type Query = { Cursor: string option }

    type Response = {
        Tag: string
        Items: GetFeed.FeedItem list
        NextCursor: string option
    }

    let endpoint : GetByQuery<Query, Response> = GetByQuery (sprintf "/api/tags/%s/items")

module Events =
    let endpoint : Get<unit> = Get "/api/events"
