module Server.Handlers

open Fable.Core
open Browser.Types
open Thoth.Json
open Shared.Domain
open Shared.Codecs
open Shared.Api
open Server.Env
open Server.Router

/// API handlers - each function handles one endpoint.
/// Uses Thoth encoders/decoders from Shared for type safety.

let getFeed (env: Env) : JS.Promise<Response> =
    promise {
        // TODO: Query D1 for items
        // let! result = env.DB.prepare("SELECT * FROM items ORDER BY created_at DESC LIMIT 50").all()

        // For now, return mock data
        let mockItems = [
            { Id = ItemId (System.Guid.NewGuid())
              Title = "Welcome to Hedge"
              Link = Some "https://example.com"
              Extract = None
              CreatedAt = System.DateTime.UtcNow
              Tags = ["welcome"; "test"] }
        ]

        let body =
            Encode.object [
                "items", Encode.list (List.map Encode.item mockItems)
            ] |> Encode.toString 0

        return okJson body
    }

let getItem (itemId: string) (env: Env) : JS.Promise<Response> =
    promise {
        // TODO: Query D1 for item and comments
        // let! item = env.DB.prepare("SELECT * FROM items WHERE id = ?").bind(itemId).first()
        // let! comments = env.DB.prepare("SELECT * FROM comments WHERE item_id = ?").bind(itemId).all()

        // For now, return mock data
        let mockItem =
            { Id = ItemId (System.Guid.Parse(itemId))
              Title = "Test Item"
              Link = None
              Extract = Some """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Hello world"}]}]}"""
              CreatedAt = System.DateTime.UtcNow
              Tags = [] }

        let body =
            Encode.object [
                "item", Encode.item mockItem
                "comments", Encode.list []
            ] |> Encode.toString 0

        return okJson body
    }

let submitComment (request: Request) (env: Env) : JS.Promise<Response> =
    promise {
        let! bodyText = request.text()

        let decoder =
            Decode.object (fun get -> {|
                itemId = get.Required.Field "itemId" Decode.itemId
                parentId = get.Optional.Field "parentId" Decode.commentId
                author = get.Required.Field "author" Decode.string
                content = get.Required.Field "content" Decode.string
            |})

        match Decode.fromString decoder bodyText with
        | Error err ->
            return badRequest err
        | Ok req ->
            // TODO: Insert into D1
            let newComment =
                { Id = CommentId (System.Guid.NewGuid())
                  ItemId = req.itemId
                  ParentId = req.parentId
                  Author = req.author
                  Content = req.content
                  CreatedAt = System.DateTime.UtcNow }

            let body =
                Encode.object [
                    "comment", Encode.comment newComment
                ] |> Encode.toString 0

            return okJson body
    }

let submitItem (request: Request) (env: Env) : JS.Promise<Response> =
    promise {
        let! bodyText = request.text()

        let decoder =
            Decode.object (fun get -> {|
                title = get.Required.Field "title" Decode.string
                link = get.Optional.Field "link" Decode.string
                extract = get.Optional.Field "extract" Decode.string
                tags = get.Required.Field "tags" (Decode.list Decode.string)
            |})

        match Decode.fromString decoder bodyText with
        | Error err ->
            return badRequest err
        | Ok req ->
            // TODO: Insert into D1
            let newItem =
                { Id = ItemId (System.Guid.NewGuid())
                  Title = req.title
                  Link = req.link
                  Extract = req.extract
                  CreatedAt = System.DateTime.UtcNow
                  Tags = req.tags }

            let body =
                Encode.object [
                    "item", Encode.item newItem
                ] |> Encode.toString 0

            return okJson body
    }
