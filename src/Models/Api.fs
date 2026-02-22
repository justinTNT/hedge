module Models.Api

open Hedge.Interface

module GetFeed =
    type FeedItem = {
        Id: string
        Title: string
        Image: string option
        Extract: RichContent option
        OwnerComment: RichContent
        Timestamp: int
    }

    type Response = {
        Items: FeedItem list
    }

    let endpoint : Get<Response> = Get "/api/feed"

module SubmitComment =
    type CommentItem = {
        Id: string
        ItemId: string
        GuestId: string
        ParentId: string option
        Author: string
        Content: RichContent
        Timestamp: int
    }

    type Request = {
        ItemId: string
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

module SubmitItem =
    type MicroblogItem = {
        Id: string
        Title: string
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
        Item: MicroblogItem
    }

    let endpoint : Post<Request, Response> = Post "/api/item"

module GetItem =
    type Response = {
        Item: SubmitItem.MicroblogItem
    }

    let endpoint : GetOne<Response> = GetOne (sprintf "/api/item/%s")

module GetTags =
    type Response = {
        Tags: string list
    }

    let endpoint : Get<Response> = Get "/api/tags"

module GetItemsByTag =
    type Response = {
        Tag: string
        Items: GetFeed.FeedItem list
    }

    let endpoint : GetOne<Response> = GetOne (sprintf "/api/tags/%s/items")

module Events =
    let endpoint : Get<unit> = Get "/api/events"
