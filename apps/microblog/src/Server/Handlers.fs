module Server.Handlers

open Fable.Core
open Fable.Core.JsInterop
open Thoth.Json
open Hedge.Interface
open Hedge.Validate
open Hedge.Workers
open Hedge.Router
open Codecs
open Models.Api
open Server.Env
open Server.Db

let private toFeedItem (r: MicroblogItemRow) : GetFeed.FeedItem =
    { Id = r.Id
      Title = r.Title
      Image = r.Image
      Extract = r.Extract |> Option.map RichContent
      OwnerComment = RichContent r.OwnerComment
      Timestamp = r.CreatedAt }

let private toCommentItem (r: ItemCommentRow) : SubmitComment.CommentItem =
    { Id = r.Id
      ItemId = r.ItemId
      GuestId = r.GuestId
      ParentId = r.ParentId
      Author = r.Author
      Content = RichContent r.Content
      Timestamp = r.CreatedAt }

let getFeed (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let! result = selectMicroblogItems(env.DB).all()
        let items = result.results |> Array.map (parseMicroblogItemRow >> toFeedItem) |> Array.toList
        let body =
            Encode.object [
                "items", Encode.list (List.map Encode.feedItem items)
            ] |> Encode.toString 0
        return okJson body
    }

let getItem (itemId: string) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let itemStmt = selectMicroblogItem itemId env.DB
        let commentStmt = selectItemCommentsByItemId itemId env.DB
        let tagStmt =
            bind
                (env.DB.prepare("SELECT t.name FROM tags t JOIN item_tags it ON t.id = it.tag_id WHERE it.item_id = ?"))
                [| box itemId |]

        let! results = env.DB.batch([| itemStmt; commentStmt; tagStmt |])

        let itemRows = results.[0].results
        if itemRows.Length = 0 then
            return notFound ()
        else
            let r = parseMicroblogItemRow itemRows.[0]
            let comments = results.[1].results |> Array.map (parseItemCommentRow >> toCommentItem) |> Array.toList
            let tags = results.[2].results |> Array.map (fun row -> rowStr row "name") |> Array.toList

            let item : SubmitItem.MicroblogItem =
                { Id = r.Id
                  Title = r.Title
                  Link = r.Link |> Option.map Link
                  Image = r.Image |> Option.map Link
                  Extract = r.Extract |> Option.map RichContent
                  OwnerComment = RichContent r.OwnerComment
                  Tags = tags
                  Comments = comments
                  Timestamp = r.CreatedAt }

            let body =
                Encode.object [
                    "item", Encode.microblogItemView item
                ] |> Encode.toString 0

            return okJson body
    }

let submitComment (req: SubmitComment.Request) (request: WorkerRequest)
    (env: Env) (ctx: ExecutionContext) : JS.Promise<WorkerResponse> =
    promise {
        match Validate.submitCommentReq req with
        | Error errors ->
            return validationErrorResponse errors
        | Ok req ->
        let guest = resolveGuest request
        let guestId = guest.GuestId
        let commentId = newId ()
        let now = epochNow ()
        let author = req.Author |> Option.defaultValue "Anonymous"

        let ensureGuest =
            bind
                (env.DB.prepare(
                    "INSERT OR IGNORE INTO guests (id, name, picture, session_id, created_at) VALUES (?, ?, ?, ?, ?)"
                ))
                [| box guestId; box author; box ""; box guestId; box now |]

        let upsertGuestSession =
            bind
                (env.DB.prepare(
                    "INSERT OR REPLACE INTO guest_sessions (guest_id, display_name, created_at) VALUES (?, ?, ?)"
                ))
                [| box guestId; box author; box now |]

        let insertComment =
            bind
                (env.DB.prepare(
                    "INSERT INTO comments (id, item_id, guest_id, parent_id, author, content, removed, created_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?)"
                ))
                [| box commentId; box req.ItemId; box guestId; optToDb req.ParentId; box author; box req.Content; box 0; box now |]

        let! _ = env.DB.batch([| ensureGuest; upsertGuestSession; insertComment |])

        let newComment : SubmitComment.CommentItem =
            { Id = commentId
              ItemId = req.ItemId
              GuestId = guestId
              ParentId = req.ParentId
              Author = author
              Content = RichContent req.Content
              Timestamp = now }

        let event : Models.Ws.NewCommentEvent =
            { Id = commentId; ItemId = req.ItemId; GuestId = guestId
              ParentId = req.ParentId; Author = author
              Content = req.Content; Timestamp = now }

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

        return okJsonWithCookie body (guestCookieValue guest)
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
                    "SELECT i.*
                     FROM items i
                     JOIN item_tags it ON i.id = it.item_id
                     JOIN tags t ON it.tag_id = t.id
                     WHERE t.name = ?
                     ORDER BY i.created_at DESC LIMIT 50"))
                [| box tag |]
        let! result = stmt.all()
        let items = result.results |> Array.map (parseMicroblogItemRow >> toFeedItem) |> Array.toList
        let body =
            Encode.object [
                "tag", Encode.string tag
                "items", Encode.list (List.map Encode.feedItem items)
            ] |> Encode.toString 0
        return okJson body
    }

let submitItem (req: SubmitItem.Request) (request: WorkerRequest)
    (env: Env) (ctx: ExecutionContext) : JS.Promise<WorkerResponse> =
    promise {
        match Validate.submitItemReq req with
        | Error errors ->
            return validationErrorResponse errors
        | Ok req ->
        let ins = insertMicroblogItem env.DB
                    { Title = req.Title; Link = req.Link; Image = req.Image
                      Extract = req.Extract; OwnerComment = req.OwnerComment; ViewCount = 0 }

        let tagStmts =
            req.Tags |> List.collect (fun tagName ->
                let tagId = newId ()
                let insertTag =
                    bind
                        (env.DB.prepare("INSERT OR IGNORE INTO tags (id, name, created_at) VALUES (?, ?, ?)"))
                        [| box tagId; box tagName; box ins.CreatedAt |]
                let linkTag =
                    bind
                        (env.DB.prepare("INSERT INTO item_tags (item_id, tag_id) SELECT ?, id FROM tags WHERE name = ?"))
                        [| box ins.Id; box tagName |]
                [ insertTag; linkTag ]
            )

        let allStmts = ins.Stmt :: tagStmts |> List.toArray
        let! _ = env.DB.batch(allStmts)

        let newItem : SubmitItem.MicroblogItem =
            { Id = ins.Id
              Title = req.Title
              Link = req.Link |> Option.map Link
              Image = req.Image |> Option.map Link
              Extract = req.Extract |> Option.map RichContent
              OwnerComment = RichContent req.OwnerComment
              Tags = req.Tags
              Comments = []
              Timestamp = ins.CreatedAt }

        let body =
            Encode.object [
                "item", Encode.microblogItemView newItem
            ] |> Encode.toString 0

        return okJson body
    }
