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

module SubmitComment =
    type CommentItem = {
        Id: string
        ItemId: string
        GuestId: string
        ParentId: string option
        AuthorName: string
        Text: RichContent
        Timestamp: int
    }

    type Request = {
        ItemId: string
        ParentId: string option
        Text: string
        AuthorName: string option
    }

    type ServerContext = {
        FreshGuestId: string
        FreshCommentId: string
    }

    type Response = {
        Comment: CommentItem
    }

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

module GetItem =
    type Response = {
        Item: SubmitItem.MicroblogItem
    }

module GetTags =
    type Response = {
        Tags: string list
    }

module GetItemsByTag =
    type Response = {
        Tag: string
        Items: GetFeed.FeedItem list
    }

module Routes =
    let feed = "/api/feed"
    let item id = sprintf "/api/item/%s" id
    let submitComment = "/api/comment"
    let submitItem = "/api/item"
    let tags = "/api/tags"
    let itemsByTag tag = sprintf "/api/tags/%s/items" tag
    let events = "/api/events"
