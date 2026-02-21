module Codecs

open Thoth.Json
open Hedge.Interface
open Hedge.Codec
open Models.Domain
open Models.Api

/// Unwrap helpers — terse pattern matches used in Handlers.fs.
let inline pk (PrimaryKey v) = v
let inline mt (MultiTenant v) = v
let inline ct (CreateTimestamp v) = v
let inline ut (UpdateTimestamp v) = v
let inline sd (SoftDelete v) = v
let inline fk (ForeignKey v) = v
let inline rc (RichContent v) = v
let inline lk (Link v) = v

module Encode =

    // -- Schema types --
    let inline guest (g: Guest) = encode g
    let inline microblogItem (i: MicroblogItem) = encode i
    let inline itemComment (c: ItemComment) = encode c
    let inline tag (t: Tag) = encode t
    let inline guestSession (g: GuestSession) = encode g

    // -- API view types --
    let inline feedItem (i: GetFeed.FeedItem) = encode i
    let inline commentItem (c: SubmitComment.CommentItem) = encode c
    let inline microblogItemView (i: SubmitItem.MicroblogItem) = encode i

    // -- SSE event encoders --
    let inline newCommentEvent (e: Models.Sse.NewCommentEvent) = encode e

    // -- API request encoders --
    let inline submitCommentReq (r: SubmitComment.Request) = encode r
    let inline submitItemReq (r: SubmitItem.Request) = encode r

module Decode =

    // -- Schema types --
    let guest : Decoder<Guest> = decode<Guest>()
    let microblogItem : Decoder<MicroblogItem> = decode<MicroblogItem>()
    let itemComment : Decoder<ItemComment> = decode<ItemComment>()
    let tag : Decoder<Tag> = decode<Tag>()
    let guestSession : Decoder<GuestSession> = decode<GuestSession>()

    // -- API view types --
    let feedItem : Decoder<GetFeed.FeedItem> = decode<GetFeed.FeedItem>()
    let commentItem : Decoder<SubmitComment.CommentItem> = decode<SubmitComment.CommentItem>()
    let microblogItemView : Decoder<SubmitItem.MicroblogItem> = decode<SubmitItem.MicroblogItem>()

    // -- SSE event decoders --
    let newCommentEvent : Decoder<Models.Sse.NewCommentEvent> = decode<Models.Sse.NewCommentEvent>()

    // -- API response decoders --
    let getFeedResponse : Decoder<GetFeed.Response> = decode<GetFeed.Response>()
    let getItemResponse : Decoder<GetItem.Response> = decode<GetItem.Response>()
    let submitCommentResponse : Decoder<SubmitComment.Response> = decode<SubmitComment.Response>()
    let submitItemResponse : Decoder<SubmitItem.Response> = decode<SubmitItem.Response>()
    let getTagsResponse : Decoder<GetTags.Response> = decode<GetTags.Response>()
    let getItemsByTagResponse : Decoder<GetItemsByTag.Response> = decode<GetItemsByTag.Response>()

    // -- API request decoders --
    let submitCommentReq : Decoder<SubmitComment.Request> = decode<SubmitComment.Request>()
    let submitItemReq : Decoder<SubmitItem.Request> = decode<SubmitItem.Request>()

module Validate =

    open Hedge.Schema
    open Hedge.Validate

    let submitCommentSchema =
        schema "SubmitComment.Request" [
            fieldWith "ItemId" FString [Required; Trim]
            fieldWith "ParentId" (FOption FString) [Trim]
            fieldWith "Text" FString [Required; Trim; MinLength 1; MaxLength 10000]
            fieldWith "AuthorName" (FOption FString) [Trim; MaxLength 100]
        ]

    let submitItemSchema =
        schema "SubmitItem.Request" [
            fieldWith "Title" FString [Required; Trim; MinLength 1; MaxLength 200]
            fieldWith "Link" (FOption FString) [Trim; MaxLength 2000]
            fieldWith "Image" (FOption FString) [Trim; MaxLength 2000]
            fieldWith "Extract" (FOption FString) [Trim; MaxLength 5000]
            fieldWith "OwnerComment" FString [Required; Trim; MinLength 1; MaxLength 10000]
            fieldWith "Tags" (FList FString) [MaxLength 20]
        ]

    let inline submitCommentReq (r: SubmitComment.Request) = validate submitCommentSchema r
    let inline submitItemReq (r: SubmitItem.Request) = validate submitItemSchema r
