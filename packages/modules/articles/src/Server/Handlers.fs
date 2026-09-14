module Articles.Handlers

// The articles module's content handlers — feed, post, comments. Extracted from
// the articles app; the identity/guest/OAuth layer is NOT here (it's the shared,
// app-level `Server.Identity` these handlers delegate to). Table names come via
// `Server.Db`'s generated statements + `Articles.Sql` (Tables-driven), so the same
// code serves `posts`/`comments` standalone or `articles_posts`/`articles_comments`
// mounted in a host.

open Fable.Core
open Thoth.Json
open Hedge.Interface
open Hedge.Validate
open Hedge.Workers
open Hedge.Router
open Articles.Codecs
open Articles.Api
open Server.Env
open Articles.Db

// The shared, app-level identity layer. `Server.Handlers` reaches it as `Identity`
// by namespace proximity; this module lives outside `Server`, so alias it.
module Identity = Server.Identity

// The feed SELECT is a lean column subset (no body), so read the row directly
// rather than through the full parsePostRow.
let private toFeedItem (row: obj) : GetFeed.FeedItem =
    { Id = rowStr row "id"
      Title = rowStr row "title"
      Slug = rowStrOpt row "slug"
      Image = rowStrOpt row "image"
      Teaser = rowStrOpt row "teaser" |> Option.map RichContent
      Timestamp = rowInt row "article_date" }

let private uuidPattern = System.Text.RegularExpressions.Regex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
let private isUuid (s: string) = uuidPattern.IsMatch(s)

let private toCommentItem (pictureOf: string -> string) (r: CommentRow) : SubmitComment.CommentItem =
    { Id = r.Id
      PostId = r.PostId
      IdentityId = r.IdentityId
      ParentId = r.ParentId
      Author = r.Author
      Picture = pictureOf r.IdentityId
      Content = RichContent r.Content
      Timestamp = r.CreatedAt }

let private pageSize = 8   // small: load less, reload more

let getFeed (query: GetFeed.Query) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        // Fetch pageSize+1 to know whether a further page exists without a count query.
        // No cursor => first page; a cursor is the opaque "<ts>_<id>" token from a
        // prior response's NextCursor.
        let stmt =
            match query.Cursor with
            | None ->
                bind (env.DB.prepare Articles.Sql.feedFirstPage) [| box (pageSize + 1) |]
            | Some cursor ->
                let sep = cursor.IndexOf('_')
                let ts = int (cursor.Substring(0, sep))
                let id = cursor.Substring(sep + 1)
                bind (env.DB.prepare Articles.Sql.feedAfterCursor) [| box ts; box ts; box id; box (pageSize + 1) |]
        let! result = stmt.all()
        let rows = result.results |> Array.map toFeedItem |> Array.toList
        let hasMore = List.length rows > pageSize
        let pageItems = if hasMore then List.truncate pageSize rows else rows
        let nextCursor =
            if hasMore then
                match List.tryLast pageItems with
                | Some last -> Some (sprintf "%d_%s" last.Timestamp last.Id)
                | None -> None
            else None
        let body =
            Encode.object [
                "items", Encode.list (List.map Encode.articlesFeedItem pageItems)
                "nextCursor", (match nextCursor with Some c -> Encode.string c | None -> Encode.nil)
            ] |> Encode.toString 0
        return okJson body
    }

let getPost (idOrSlug: string) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let postStmt =
            if isUuid idOrSlug then selectPost idOrSlug env.DB
            else bind (env.DB.prepare Articles.Sql.postBySlug) [| box idOrSlug |]

        let! postResult = postStmt.all()
        let rows = postResult.results
        if rows.Length = 0 then
            return notFound ()
        else
            let r = parsePostRow rows.[0]
            let commentStmt = selectCommentsByPostId r.Id env.DB
            let pictureStmt = bind (env.DB.prepare Articles.Sql.picturesForPostComments) [| box r.Id |]

            let! results = env.DB.batch([| commentStmt; pictureStmt |])
            let pictures =
                results.[1].results
                |> Array.map (fun row -> rowStr row "id", rowStr row "picture")
                |> Map.ofArray
            let pictureOf identityId = pictures |> Map.tryFind identityId |> Option.defaultValue ""
            let comments = results.[0].results |> Array.map (parseCommentRow >> toCommentItem pictureOf) |> Array.toList

            let post : GetPost.PostDetail =
                { Id = r.Id
                  Title = r.Title
                  Slug = r.Slug
                  Image = r.Image
                  Teaser = r.Teaser |> Option.map RichContent
                  Body = RichContent r.Body
                  Comments = comments
                  Timestamp = r.ArticleDate }

            let body =
                Encode.object [ "post", Encode.articlesPostDetail post ] |> Encode.toString 0
            return okJson body
    }

let submitComment (req: SubmitComment.Request) (request: WorkerRequest)
    (env: Env) (ctx: ExecutionContext) : JS.Promise<WorkerResponse> =
    promise {
        match Validate.articlesSubmitCommentReq req with
        | Error errors ->
            return validationErrorResponse errors
        | Ok req ->
        let guest = resolveGuest request
        let guestId = guest.GuestId
        let commentId = newId ()
        let identityId = newId ()
        let now = epochNow ()
        let author = req.Author |> Option.defaultValue "Anonymous"
        // Unwrap the typed request ids to plain strings for storage.
        let (ForeignKey postId) = req.PostId
        let parentId = req.ParentId |> Option.map (fun (ForeignKey p) -> p)

        let! _ =
            env.DB.batch([|
                Identity.ensureGuestStmt env.DB guestId now
                Identity.ensureAnonymousStmt env.DB identityId guestId author now
            |])
        let! active = Identity.activeFor env.DB guestId
        let activeIdentityId = active |> Option.map (fun i -> i.Id) |> Option.defaultValue identityId
        let activePicture = active |> Option.map (fun i -> i.Picture) |> Option.defaultValue ""

        let insertComment =
            bind
                (env.DB.prepare Articles.Sql.insertComment)
                [| box commentId; box postId; box activeIdentityId; optToDb parentId; box author; box req.Content; box 0; box now |]

        let! _ = env.DB.batch([| insertComment |])

        let newComment : SubmitComment.CommentItem =
            { Id = commentId
              PostId = postId
              IdentityId = activeIdentityId
              ParentId = parentId
              Author = author
              Picture = activePicture
              Content = RichContent req.Content
              Timestamp = now }

        let event : Articles.Ws.NewCommentEvent =
            { Id = commentId; PostId = ForeignKey postId; IdentityId = IdentityRef activeIdentityId
              ParentId = parentId |> Option.map ForeignKey; Author = author; Picture = activePicture
              Content = req.Content; Timestamp = now }

        Hedge.Events.broadcast env.EVENTS ctx postId "NewComment" (Articles.Codecs.Encode.articlesNewCommentEvent event)

        let body =
            Encode.object [ "comment", Encode.articlesCommentItem newComment ] |> Encode.toString 0

        return okJsonWithCookie body (guestCookieValue guest)
    }
