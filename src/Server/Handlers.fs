module Server.Handlers

open Fable.Core
open Thoth.Json
open Hedge.Interface
open Hedge.Validate
open Hedge.Workers
open Hedge.Router
open Codecs
open Models.Api
open Server.Env

let getFeed (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let stmt =
            env.DB.prepare(
                "SELECT id, title, image, extract, owner_comment, created_at FROM items ORDER BY created_at DESC LIMIT 50"
            )
        let! result = stmt.all()

        let items =
            result.results
            |> Array.map (fun row ->
                let fi : GetFeed.FeedItem =
                    { Id = rowStr row "id"
                      Title = rowStr row "title"
                      Image = rowStrOpt row "image"
                      Extract = rowStrOpt row "extract" |> Option.map RichContent
                      OwnerComment = RichContent (rowStr row "owner_comment")
                      Timestamp = rowInt row "created_at" }
                fi
            )
            |> Array.toList

        let body =
            Encode.object [
                "items", Encode.list (List.map Encode.feedItem items)
            ] |> Encode.toString 0

        return okJson body
    }

let getItem (itemId: string) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let itemStmt =
            bind
                (env.DB.prepare("SELECT id, title, link, image, extract, owner_comment, created_at FROM items WHERE id = ?"))
                [| box itemId |]

        let commentStmt =
            bind
                (env.DB.prepare("SELECT id, item_id, parent_id, author, content, created_at FROM comments WHERE item_id = ? ORDER BY created_at"))
                [| box itemId |]

        let tagStmt =
            bind
                (env.DB.prepare("SELECT t.name FROM tags t JOIN item_tags it ON t.id = it.tag_id WHERE it.item_id = ?"))
                [| box itemId |]

        let! results = env.DB.batch([| itemStmt; commentStmt; tagStmt |])

        let itemRows = results.[0].results
        if itemRows.Length = 0 then
            return notFound ()
        else
            let row = itemRows.[0]

            let comments =
                results.[1].results
                |> Array.map (fun r ->
                    let ci : SubmitComment.CommentItem =
                        { Id = rowStr r "id"
                          ItemId = rowStr r "item_id"
                          GuestId = "guest-anon"
                          ParentId = rowStrOpt r "parent_id"
                          AuthorName = rowStr r "author"
                          Text = RichContent (rowStr r "content")
                          Timestamp = rowInt r "created_at" }
                    ci
                )
                |> Array.toList

            let tags =
                results.[2].results
                |> Array.map (fun r -> rowStr r "name")
                |> Array.toList

            let item : SubmitItem.MicroblogItem =
                { Id = rowStr row "id"
                  Title = rowStr row "title"
                  Link = rowStrOpt row "link" |> Option.map Link
                  Image = rowStrOpt row "image" |> Option.map Link
                  Extract = rowStrOpt row "extract" |> Option.map RichContent
                  OwnerComment = RichContent (rowStr row "owner_comment")
                  Tags = tags
                  Comments = comments
                  Timestamp = rowInt row "created_at" }

            let body =
                Encode.object [
                    "item", Encode.microblogItemView item
                ] |> Encode.toString 0

            return okJson body
    }

let submitComment (request: WorkerRequest) (env: Env) (ctx: ExecutionContext) : JS.Promise<WorkerResponse> =
    promise {
        let! bodyText = request.text()

        match Decode.fromString Decode.submitCommentReq bodyText with
        | Error err ->
            return badRequest err
        | Ok raw ->
            match Validate.submitCommentReq raw with
            | Error errors ->
                return validationErrorResponse errors
            | Ok req ->
            let commentId = newId ()
            let now = epochNow ()
            let author = req.AuthorName |> Option.defaultValue "Anonymous"

            let stmt =
                bind
                    (env.DB.prepare(
                        "INSERT INTO comments (id, item_id, parent_id, author, content, created_at) VALUES (?, ?, ?, ?, ?, ?)"
                    ))
                    [| box commentId; box req.ItemId; optToDb req.ParentId; box author; box req.Text; box now |]

            let! _ = stmt.run()

            let newComment : SubmitComment.CommentItem =
                { Id = commentId
                  ItemId = req.ItemId
                  GuestId = "guest-anon"
                  ParentId = req.ParentId
                  AuthorName = author
                  Text = RichContent req.Text
                  Timestamp = now }

            let event : Models.Sse.NewCommentEvent =
                { Id = commentId; ItemId = req.ItemId; GuestId = "guest-anon"
                  ParentId = req.ParentId; AuthorName = author
                  Text = req.Text; Timestamp = now }

            let eventJson =
                Encode.object [
                    "type", Encode.string "NewComment"
                    "payload", Codecs.Encode.newCommentEvent event
                ] |> Encode.toString 0

            let doId = env.EVENTS.idFromName(req.ItemId)
            let stub = env.EVENTS.get(doId)
            let broadcastReq = createRequest "https://do/broadcast" "POST" eventJson
            ctx.waitUntil(stub.fetch(broadcastReq) |> unbox<JS.Promise<obj>>)

            let body =
                Encode.object [
                    "comment", Encode.commentItem newComment
                ] |> Encode.toString 0

            return okJson body
    }

let getTags (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let! result = env.DB.prepare("SELECT name FROM tags ORDER BY name").all()
        let tags = result.results |> Array.map (fun r -> rowStr r "name") |> Array.toList
        let body =
            Encode.object [
                "tags", Encode.list (List.map Encode.string tags)
            ] |> Encode.toString 0
        return okJson body
    }

let getItemsByTag (tag: string) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let stmt =
            bind
                (env.DB.prepare(
                    "SELECT i.id, i.title, i.image, i.extract, i.owner_comment, i.created_at
                     FROM items i
                     JOIN item_tags it ON i.id = it.item_id
                     JOIN tags t ON it.tag_id = t.id
                     WHERE t.name = ?
                     ORDER BY i.created_at DESC LIMIT 50"))
                [| box tag |]
        let! result = stmt.all()
        let items =
            result.results
            |> Array.map (fun row ->
                let fi : GetFeed.FeedItem =
                    { Id = rowStr row "id"
                      Title = rowStr row "title"
                      Image = rowStrOpt row "image"
                      Extract = rowStrOpt row "extract" |> Option.map RichContent
                      OwnerComment = RichContent (rowStr row "owner_comment")
                      Timestamp = rowInt row "created_at" }
                fi)
            |> Array.toList
        let body =
            Encode.object [
                "tag", Encode.string tag
                "items", Encode.list (List.map Encode.feedItem items)
            ] |> Encode.toString 0
        return okJson body
    }

let submitItem (request: WorkerRequest) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let! bodyText = request.text()

        match Decode.fromString Decode.submitItemReq bodyText with
        | Error err ->
            return badRequest err
        | Ok raw ->
            match Validate.submitItemReq raw with
            | Error errors ->
                return validationErrorResponse errors
            | Ok req ->
            let itemId = newId ()
            let now = epochNow ()

            let insertItem =
                bind
                    (env.DB.prepare(
                        "INSERT INTO items (id, title, link, image, extract, owner_comment, created_at) VALUES (?, ?, ?, ?, ?, ?, ?)"
                    ))
                    [| box itemId; box req.Title; optToDb req.Link; optToDb req.Image; optToDb req.Extract; box req.OwnerComment; box now |]

            let tagStmts =
                req.Tags |> List.collect (fun tagName ->
                    let tagId = newId ()
                    let insertTag =
                        bind
                            (env.DB.prepare("INSERT OR IGNORE INTO tags (id, name) VALUES (?, ?)"))
                            [| box tagId; box tagName |]
                    let linkTag =
                        bind
                            (env.DB.prepare("INSERT INTO item_tags (item_id, tag_id) SELECT ?, id FROM tags WHERE name = ?"))
                            [| box itemId; box tagName |]
                    [ insertTag; linkTag ]
                )

            let allStmts = insertItem :: tagStmts |> List.toArray
            let! _ = env.DB.batch(allStmts)

            let newItem : SubmitItem.MicroblogItem =
                { Id = itemId
                  Title = req.Title
                  Link = req.Link |> Option.map Link
                  Image = req.Image |> Option.map Link
                  Extract = req.Extract |> Option.map RichContent
                  OwnerComment = RichContent req.OwnerComment
                  Tags = req.Tags
                  Comments = []
                  Timestamp = now }

            let body =
                Encode.object [
                    "item", Encode.microblogItemView newItem
                ] |> Encode.toString 0

            return okJson body
    }
