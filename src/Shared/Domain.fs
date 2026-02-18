module Shared.Domain

open System

/// Core domain types shared between client and server.
/// These are the "Rosetta Stone" - defined once, used everywhere.

type UserId = UserId of Guid
type CommentId = CommentId of Guid
type ItemId = ItemId of Guid

type User = {
    Id: UserId
    Email: string
    DisplayName: string
}

type Comment = {
    Id: CommentId
    ItemId: ItemId
    ParentId: CommentId option
    Author: string
    Content: string  // Rich text JSON from TipTap
    CreatedAt: DateTime
}

type Item = {
    Id: ItemId
    Title: string
    Link: string option
    Extract: string option  // Rich text JSON
    CreatedAt: DateTime
    Tags: string list
}

type GuestSession = {
    GuestId: string
    DisplayName: string
    CreatedAt: int64
}
