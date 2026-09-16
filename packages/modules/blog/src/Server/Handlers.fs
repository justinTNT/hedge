module Blog.Handlers

// The blog module's content handlers — feed, item, comments, tags. Extracted
// from the microblog app; the identity/guest/OAuth layer is NOT here (it's the
// shared, app-level `Server.Identity` these handlers delegate to). Table names
// come via `Server.Db`'s generated statements + `Blog.Sql` (Tables-driven), so
// the same code serves `items` standalone or `blog_items` mounted in a host.

open Fable.Core
open Fable.Core.JsInterop
open Thoth.Json
open Hedge.Interface
open Hedge.Validate
open Hedge.Workers
open Hedge.Router
open Blog.Codecs
open Blog.Api
open Blog.Db
// C3: the module's capabilities arrive through Blog.Services (built by the host), not the app's
// Server.Env / Server.Identity. Author resolution comes via the content-server contract.
open Blog.Services
open Content.Server.Author

let private toFeedItem (r: ItemRow) : GetFeed.FeedItem =
    { Id = r.Id
      Title = r.Title
      Slug = r.Slug
      Image = r.Image
      Extract = r.Extract |> Option.map RichContent
      OwnerComment = RichContent r.OwnerComment
      Timestamp = r.ArticleDate }

let private uuidPattern = System.Text.RegularExpressions.Regex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)

let private isUuid (s: string) = uuidPattern.IsMatch(s)

let private slugPattern = System.Text.RegularExpressions.Regex("^[a-z0-9]+(?:-[a-z0-9]+)*$")

let private reservedSlugs = set [ "tag"; "new"; "feed"; "api"; "blobs"; "public"; "admin"; "archive" ]

let private validateSlug (slug: string option) =
    match slug with
    | None | Some "" -> Ok None
    | Some s ->
        let s = s.ToLowerInvariant().Trim()
        if not (slugPattern.IsMatch(s)) then
            Error "Slug must be lowercase alphanumeric with hyphens only"
        elif s.Length < 2 then
            Error "Slug must be at least 2 characters"
        elif s.Length > 80 then
            Error "Slug must be 80 characters or fewer"
        elif Set.contains s reservedSlugs then
            Error (sprintf "Slug '%s' is reserved" s)
        else Ok (Some s)

let private toCommentItem (pictureOf: string -> string) (r: ItemCommentRow) : SubmitComment.CommentItem =
    { Id = r.Id
      ItemId = r.ItemId
      IdentityId = r.IdentityId
      ParentId = r.ParentId
      Author = r.Author
      Picture = pictureOf r.IdentityId
      Content = RichContent r.Content
      Timestamp = r.CreatedAt }

let private pageSize = 6   // small: load less, reload more (feed + tag pages)

let getFeed (query: GetFeed.Query) (services: Services) : JS.Promise<WorkerResponse> =
    promise {
        // Fetch pageSize+1 to know whether a further page exists without a count query.
        // No cursor => first page; a cursor is the opaque "<ts>_<id>" token from a
        // prior response's NextCursor.
        let stmt =
            match query.Cursor with
            | None ->
                bind (services.DB.prepare Blog.Sql.feedFirstPage) [| box (pageSize + 1) |]
            | Some cursor ->
                let sep = cursor.IndexOf('_')
                let ts = int (cursor.Substring(0, sep))
                let id = cursor.Substring(sep + 1)
                bind (services.DB.prepare Blog.Sql.feedAfterCursor) [| box ts; box ts; box id; box (pageSize + 1) |]
        let! result = stmt.all()
        let rows = result.results |> Array.map (parseItemRow >> toFeedItem) |> Array.toList
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
                "items", Encode.list (List.map Encode.blogFeedItem pageItems)
                "nextCursor", (match nextCursor with Some c -> Encode.string c | None -> Encode.nil)
            ] |> Encode.toString 0
        return okJson body
    }

let getItem (idOrSlug: string) (services: Services) : JS.Promise<WorkerResponse> =
    promise {
        let itemStmt =
            if isUuid idOrSlug then
                selectItem idOrSlug services.DB
            else
                bind (services.DB.prepare Blog.Sql.itemBySlug) [| box idOrSlug |]

        let! itemResult = itemStmt.all()
        let itemRows = itemResult.results
        if itemRows.Length = 0 then
            return notFound ()
        else
            let r = parseItemRow itemRows.[0]
            let commentStmt = selectItemCommentsByItemId r.Id services.DB
            let tagStmt = bind (services.DB.prepare Blog.Sql.tagsForItem) [| box r.Id |]
            let pictureStmt = bind (services.DB.prepare Blog.Sql.picturesForItemComments) [| box r.Id |]

            let! results = services.DB.batch([| commentStmt; tagStmt; pictureStmt |])
            let pictures =
                results.[2].results
                |> Array.map (fun row -> rowStr row "id", rowStr row "picture")
                |> Map.ofArray
            let pictureOf identityId = pictures |> Map.tryFind identityId |> Option.defaultValue ""
            let comments = results.[0].results |> Array.map (parseItemCommentRow >> toCommentItem pictureOf) |> Array.toList
            let tags = results.[1].results |> Array.map (fun row -> rowStr row "name") |> Array.toList

            let item : SubmitItem.Item =
                { Id = r.Id
                  Title = r.Title
                  Slug = r.Slug
                  Link = r.Link |> Option.map Link
                  Image = r.Image |> Option.map Image
                  Extract = r.Extract |> Option.map RichContent
                  OwnerComment = RichContent r.OwnerComment
                  Tags = tags
                  Comments = comments
                  Timestamp = r.ArticleDate }

            let body =
                Encode.object [
                    "item", Encode.blogItemView item
                ] |> Encode.toString 0

            return okJson body
    }

let submitComment (req: SubmitComment.Request) (request: WorkerRequest)
    (services: Services) (ctx: ExecutionContext) : JS.Promise<WorkerResponse> =
    promise {
        match Validate.blogSubmitCommentReq req with
        | Error errors ->
            return validationErrorResponse errors
        | Ok req ->
        let guest = resolveGuest request
        let guestId = guest.GuestId
        let commentId = services.NewId ()
        let identityId = services.NewId ()
        let now = services.Now ()
        let author = req.Author |> Option.defaultValue "Anonymous"
        // Unwrap the typed request ids to plain strings for storage.
        let (ForeignKey itemId) = req.ItemId
        let parentId = req.ParentId |> Option.map (fun (ForeignKey p) -> p)

        // C3: ensure the guest + anonymous identity and resolve the active author through the
        // host-provided resolver (was Server.Identity.ensure*/activeFor inline). The two ensure
        // statements + the activeFor read happen inside the resolver, preserving the batch shape.
        let! resolved =
            services.Author.ResolveAuthor
                { GuestId = guestId; FallbackIdentityId = identityId; AuthorName = author; Now = now }
        let activeIdentityId = resolved.IdentityId
        let activePicture = resolved.Picture

        let insertComment =
            bind
                (services.DB.prepare Blog.Sql.insertComment)
                [| box commentId; box itemId; box activeIdentityId; optToDb parentId; box author; box req.Content; box 0; box now |]

        let! _ = services.DB.batch([| insertComment |])

        let newComment : SubmitComment.CommentItem =
            { Id = commentId
              ItemId = itemId
              IdentityId = activeIdentityId
              ParentId = parentId
              Author = author
              Picture = activePicture
              Content = RichContent req.Content
              Timestamp = now }

        let event : Blog.Ws.NewCommentEvent =
            { Id = commentId; ItemId = ForeignKey itemId; IdentityId = IdentityRef activeIdentityId
              ParentId = parentId |> Option.map ForeignKey; Author = author; Picture = activePicture
              Content = req.Content; Timestamp = now }

        Hedge.Events.broadcast services.Events ctx itemId "NewComment" (Blog.Codecs.Encode.blogNewCommentEvent event)

        let body =
            Encode.object [
                "comment", Encode.blogCommentItem newComment
            ] |> Encode.toString 0

        return okJsonWithCookie body (guestCookieValue guest)
    }

let getTags (services: Services) : JS.Promise<WorkerResponse> =
    promise {
        let! result = services.DB.prepare(Blog.Sql.tagNames).all()
        let tags = result.results |> Array.map (fun r -> rowStr r "name") |> Array.toList
        let body =
            Encode.object [
                "tags", Encode.list (List.map Encode.string tags)
            ] |> Encode.toString 0
        return okJson body
    }

[<Emit("decodeURIComponent($0)")>]
let private decodeUri (s: string) : string = jsNative

// The tag is the path param (arrives percent-encoded, e.g. "lucas%20heights", so
// decode it before querying + echoing back); pagination is the ?cursor query param
// (page 1 omits it). No more smuggling tag+cursor through one "tag~cursor" segment.
let getItemsByTag (tagParam: string) (query: GetItemsByTag.Query) (services: Services) : JS.Promise<WorkerResponse> =
    promise {
        let tag = decodeUri tagParam
        let stmt =
            match query.Cursor with
            | None ->
                bind (services.DB.prepare Blog.Sql.itemsByTag) [| box tag; box (pageSize + 1) |]
            | Some cursor ->
                let ci = cursor.IndexOf('_')
                let ts = int (cursor.Substring(0, ci))
                let id = cursor.Substring(ci + 1)
                bind (services.DB.prepare Blog.Sql.itemsByTagAfter) [| box tag; box ts; box ts; box id; box (pageSize + 1) |]
        let! result = stmt.all()
        let rows = result.results |> Array.map (parseItemRow >> toFeedItem) |> Array.toList
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
                "tag", Encode.string tag
                "items", Encode.list (List.map Encode.blogFeedItem pageItems)
                "nextCursor", (match nextCursor with Some c -> Encode.string c | None -> Encode.nil)
            ] |> Encode.toString 0
        return okJson body
    }

/// Admin gate for owner-only writes (item authoring): the request must carry the
/// matching X-Admin-Key. Comments (SubmitComment) stay public.
let private isAdmin (request: WorkerRequest) (services: Services) =
    let key = getHeader request "X-Admin-Key"
    key <> "" && key = services.AdminKey

let submitItem (req: SubmitItem.Request) (request: WorkerRequest)
    (services: Services) (ctx: ExecutionContext) : JS.Promise<WorkerResponse> =
    promise {
        // Item creation (authoring posts) is owner-only — require the admin key.
        if not (isAdmin request services) then
            return unauthorized ()
        else
        match Validate.blogSubmitItemReq req with
        | Error errors ->
            return validationErrorResponse errors
        | Ok req ->
        match validateSlug req.Slug with
        | Error msg ->
            return validationErrorResponse [ { Field = "Slug"; Message = msg } ]
        | Ok validatedSlug ->
        // New submissions default their article date to now; backdate later via admin.
        let submittedAt = services.Now ()
        // Rehost the item image into R2 so it survives the source rotting/hotlink-blocking.
        // Best-effort (falls back to the original URL); a `/blobs/...` value (e.g. already
        // uploaded) isn't https:// so it passes through untouched. Existing rows are untouched
        // — only new submissions rehost. (Server-side fetch is the fallback tier; a future
        // extension change can upload the bytes it already has for third-party-blocked images.)
        let! rehostedImage =
            match req.Image with
            | Some url -> promise { let! u = rehostRemoteImage services.Blobs "items" allowedImageTypes url in return Some u }
            | None -> promise { return None }
        let ins = insertItem services.DB
                    { Title = req.Title; Link = req.Link; Image = rehostedImage
                      Extract = req.Extract; OwnerComment = req.OwnerComment
                      ArticleDate = submittedAt
                      Slug = validatedSlug; ViewCount = 0 }

        let tagStmts =
            req.Tags |> List.collect (fun tagName ->
                let tagId = services.NewId ()
                let insertTag =
                    bind
                        (services.DB.prepare Blog.Sql.insertTag)
                        [| box tagId; box tagName; box ins.CreatedAt |]
                let linkTag =
                    bind
                        (services.DB.prepare Blog.Sql.linkItemTag)
                        [| box (services.NewId ()); box ins.Id; box tagName |]
                [ insertTag; linkTag ]
            )

        let allStmts = ins.Stmt :: tagStmts |> List.toArray
        let! insertOk = promise {
            try
                let! _ = services.DB.batch(allStmts)
                return true
            with ex ->
                if ex.Message.Contains("UNIQUE") && ex.Message.Contains("slug") then
                    return false
                else return raise ex
        }
        if not insertOk then
            return validationErrorResponse [ { Field = "Slug"; Message = "This slug is already taken" } ]
        else

        let newItem : SubmitItem.Item =
            { Id = ins.Id
              Title = req.Title
              Slug = validatedSlug
              Link = req.Link |> Option.map Link
              Image = rehostedImage |> Option.map Image
              Extract = req.Extract |> Option.map RichContent
              OwnerComment = RichContent req.OwnerComment
              Tags = req.Tags
              Comments = []
              Timestamp = submittedAt }

        let body =
            Encode.object [
                "item", Encode.blogItemView newItem
            ] |> Encode.toString 0

        return okJson body
    }
