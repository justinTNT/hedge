module Shared.Api

open Shared.Domain

/// API request/response types.
/// These define the contract between client and server.

module Feed =
    type Response = {
        Items: Item list
    }

module GetItem =
    type Request = {
        ItemId: ItemId
    }
    type Response = {
        Item: Item
        Comments: Comment list
    }

module SubmitComment =
    type Request = {
        ItemId: ItemId
        ParentId: CommentId option
        Author: string
        Content: string
    }
    type Response = {
        Comment: Comment
    }

module SubmitItem =
    type Request = {
        Title: string
        Link: string option
        Extract: string option
        Tags: string list
    }
    type Response = {
        Item: Item
    }

/// API routes - shared constants to avoid typos
module Routes =
    let feed = "/api/feed"
    let item id = sprintf "/api/item/%s" id
    let submitComment = "/api/comment"
    let submitItem = "/api/item"
    let tags = "/api/tags"
    let events = "/api/events"  // SSE endpoint
