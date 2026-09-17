module Articles.Handlers

// The articles module's content handlers — feed, post, comments. Content only: the
// identity/guest/OAuth layer stays in the host and reaches these handlers as a resolved
// author through Articles.Services (Services.Author; see Articles.Composition + the app's
// Server.ModuleServices). Table names come via Articles.Db's generated statements +
// Articles.Sql (Tables-driven), so the same code serves `posts`/`comments` standalone or
// `articles_posts`/`articles_comments` mounted in a host.

open Fable.Core
open Thoth.Json
open Hedge.Interface
open Hedge.Validate
open Hedge.Workers
open Hedge.Router
open Articles.Codecs
open Articles.Api
open Articles.Db
// C3: the module's capabilities arrive through Articles.Services (built by the host), not the
// app's Server.Env / Server.Identity. Author resolution comes via the content-server contract.
open Articles.Services
open Content.Server.Author
open Hedge.GuestSession

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

let getFeed (query: GetFeed.Query) (services: Services) : JS.Promise<WorkerResponse> =
    promise {
        // Fetch pageSize+1 to know whether a further page exists without a count query.
        // No cursor => first page; a cursor is the opaque "<ts>_<id>" token from a
        // prior response's NextCursor.
        let stmt =
            match query.Cursor with
            | None ->
                bind (services.DB.prepare Articles.Sql.feedFirstPage) [| box (pageSize + 1) |]
            | Some cursor ->
                let sep = cursor.IndexOf('_')
                let ts = int (cursor.Substring(0, sep))
                let id = cursor.Substring(sep + 1)
                bind (services.DB.prepare Articles.Sql.feedAfterCursor) [| box ts; box ts; box id; box (pageSize + 1) |]
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

let getPost (idOrSlug: string) (services: Services) : JS.Promise<WorkerResponse> =
    promise {
        let postStmt =
            if isUuid idOrSlug then selectPost idOrSlug services.DB
            else bind (services.DB.prepare Articles.Sql.postBySlug) [| box idOrSlug |]

        let! postResult = postStmt.all()
        let rows = postResult.results
        if rows.Length = 0 then
            return notFound ()
        else
            let r = parsePostRow rows.[0]
            let commentStmt = selectCommentsByPostId r.Id services.DB
            let pictureStmt = bind (services.DB.prepare Articles.Sql.picturesForPostComments) [| box r.Id |]

            let! results = services.DB.batch([| commentStmt; pictureStmt |])
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
    (services: Services) (ctx: ExecutionContext) : JS.Promise<WorkerResponse> =
    promise {
        match Validate.articlesSubmitCommentReq req with
        | Error errors ->
            return validationErrorResponse errors
        | Ok req ->
        // Comment is a WRITE: require an accepted (signed / bridge-upgraded) guest — never create one
        // on this path. A rejected credential is refused before any side effect.
        let! authz = services.Guest.Require request
        match authz with
        | Rejected -> return unauthorized ()
        | Accepted guest ->
        let guestId = guest.GuestId
        let commentId = services.NewId ()
        let identityId = services.NewId ()
        let now = services.Now ()
        let author = req.Author |> Option.defaultValue "Anonymous"
        // Unwrap the typed request ids to plain strings for storage.
        let (ForeignKey postId) = req.PostId
        let parentId = req.ParentId |> Option.map (fun (ForeignKey p) -> p)

        // C3: ensure the guest + anonymous identity and resolve the active author through the
        // host-provided resolver (was Server.Identity.ensure*/activeFor inline).
        let! resolved =
            services.Author.ResolveAuthor
                { GuestId = guestId; FallbackIdentityId = identityId; AuthorName = author; Now = now }
        let activeIdentityId = resolved.IdentityId
        let activePicture = resolved.Picture

        let insertComment =
            bind
                (services.DB.prepare Articles.Sql.insertComment)
                [| box commentId; box postId; box activeIdentityId; optToDb parentId; box author; box req.Content; box 0; box now |]

        let! _ = services.DB.batch([| insertComment |])

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

        Hedge.Events.broadcast services.Events ctx postId "NewComment" (Articles.Codecs.Encode.articlesNewCommentEvent event)

        let body =
            Encode.object [ "comment", Encode.articlesCommentItem newComment ] |> Encode.toString 0

        // Attach the signed replacement cookie only when the policy issued one (fresh renewal or a
        // bridge upgrade); a still-valid credential needs no re-set.
        match guest.Replacement with
        | Some c -> return okJsonWithCookie body c
        | None -> return okJson body
    }
