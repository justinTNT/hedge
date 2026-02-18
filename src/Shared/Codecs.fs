module Shared.Codecs

open System
open Thoth.Json
open Shared.Domain

/// JSON Encoders - used by both client (requests) and server (responses)
/// These are the contract. If they compile, client and server agree.

module Encode =
    let userId (UserId id) = Encode.guid id
    let commentId (CommentId id) = Encode.guid id
    let itemId (ItemId id) = Encode.guid id

    let comment (c: Comment) =
        Encode.object [
            "id", commentId c.Id
            "itemId", itemId c.ItemId
            "parentId", Encode.option commentId c.ParentId
            "author", Encode.string c.Author
            "content", Encode.string c.Content
            "createdAt", Encode.datetime c.CreatedAt
        ]

    let item (i: Item) =
        Encode.object [
            "id", itemId i.Id
            "title", Encode.string i.Title
            "link", Encode.option Encode.string i.Link
            "extract", Encode.option Encode.string i.Extract
            "createdAt", Encode.datetime i.CreatedAt
            "tags", Encode.list (List.map Encode.string i.Tags)
        ]

    let guestSession (g: GuestSession) =
        Encode.object [
            "guestId", Encode.string g.GuestId
            "displayName", Encode.string g.DisplayName
            "createdAt", Encode.int64 g.CreatedAt
        ]

/// JSON Decoders - used by both client (responses) and server (requests)

module Decode =
    let userId : Decoder<UserId> = Decode.guid |> Decode.map UserId
    let commentId : Decoder<CommentId> = Decode.guid |> Decode.map CommentId
    let itemId : Decoder<ItemId> = Decode.guid |> Decode.map ItemId

    let comment : Decoder<Comment> =
        Decode.object (fun get -> {
            Id = get.Required.Field "id" commentId
            ItemId = get.Required.Field "itemId" itemId
            ParentId = get.Optional.Field "parentId" commentId
            Author = get.Required.Field "author" Decode.string
            Content = get.Required.Field "content" Decode.string
            CreatedAt = get.Required.Field "createdAt" Decode.datetime
        })

    let item : Decoder<Item> =
        Decode.object (fun get -> {
            Id = get.Required.Field "id" itemId
            Title = get.Required.Field "title" Decode.string
            Link = get.Optional.Field "link" Decode.string
            Extract = get.Optional.Field "extract" Decode.string
            CreatedAt = get.Required.Field "createdAt" Decode.datetime
            Tags = get.Required.Field "tags" (Decode.list Decode.string)
        })

    let guestSession : Decoder<GuestSession> =
        Decode.object (fun get -> {
            GuestId = get.Required.Field "guestId" Decode.string
            DisplayName = get.Required.Field "displayName" Decode.string
            CreatedAt = get.Required.Field "createdAt" Decode.int64
        })
