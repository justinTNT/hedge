module Client.Api

open Fable.Core
open Fetch
open Thoth.Json
open Shared.Domain
open Shared.Codecs
open Shared.Api

/// Typed HTTP client using Fetch + Thoth.
/// Each function wraps a single API call with proper encoding/decoding.

let private fetchJson<'T> (url: string) (decoder: Decoder<'T>) : JS.Promise<Result<'T, string>> =
    promise {
        let! response = fetch url []
        let! text = response.text()
        return Decode.fromString decoder text
    }

let private postJson<'T> (url: string) (body: string) (decoder: Decoder<'T>) : JS.Promise<Result<'T, string>> =
    promise {
        let! response = fetch url [
            Method HttpMethod.POST
            requestHeaders [ ContentType "application/json" ]
            Body (BodyInit.Case3 body)
        ]
        let! text = response.text()
        return Decode.fromString decoder text
    }

/// Get the feed
let getFeed () : JS.Promise<Result<Item list, string>> =
    let decoder = Decode.field "items" (Decode.list Decode.item)
    fetchJson Routes.feed decoder

/// Get a single item with comments
let getItem (ItemId id) : JS.Promise<Result<GetItem.Response, string>> =
    let decoder =
        Decode.object (fun get -> {
            GetItem.Response.Item = get.Required.Field "item" Decode.item
            Comments = get.Required.Field "comments" (Decode.list Decode.comment)
        })
    fetchJson (Routes.item (string id)) decoder

/// Submit a new comment
let submitComment (req: SubmitComment.Request) : JS.Promise<Result<Comment, string>> =
    let body =
        Encode.object [
            "itemId", Encode.itemId req.ItemId
            "parentId", Encode.option Encode.commentId req.ParentId
            "author", Encode.string req.Author
            "content", Encode.string req.Content
        ] |> Encode.toString 0
    let decoder = Decode.field "comment" Decode.comment
    postJson Routes.submitComment body decoder

/// Submit a new item
let submitItem (req: SubmitItem.Request) : JS.Promise<Result<Item, string>> =
    let body =
        Encode.object [
            "title", Encode.string req.Title
            "link", Encode.option Encode.string req.Link
            "extract", Encode.option Encode.string req.Extract
            "tags", Encode.list (List.map Encode.string req.Tags)
        ] |> Encode.toString 0
    let decoder = Decode.field "item" Decode.item
    postJson Routes.submitItem body decoder
