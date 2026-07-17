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

let resolveIdentity (db: D1Database) (guestId: string) : JS.Promise<string option> =
    promise {
        let stmt =
            bind
                (db.prepare(
                    "SELECT id, provider, provider_user_id, name, picture, email, activated_at FROM identities WHERE guest_id = ? AND activated_at IS NOT NULL ORDER BY activated_at DESC LIMIT 1"
                ))
                [| box guestId |]
        let! result = stmt.first()
        if isNull (box result) then return None
        else
            let id : string = result?id
            let provider : string = result?provider
            let name : string = result?name
            let picture : string = result?picture
            let email = rowStrOpt result "email"
            let emailJson = match email with Some e -> sprintf ",\"email\":\"%s\"" e | None -> ""
            let json = sprintf """{"id":"%s","provider":"%s","name":"%s","picture":"%s"%s}""" id provider name picture emailJson
            return Some json
    }

let onOAuthComplete (db: D1Database) (blobs: R2Bucket) (guestId: string) (userInfoObj: obj) (returnTo: string) : JS.Promise<string> =
    promise {
        let name : string = userInfoObj?Name
        let picture : string = userInfoObj?PictureUrl
        let provider : string = userInfoObj?Provider
        let providerUserId : string = userInfoObj?ProviderUserId
        let email = let e : string = userInfoObj?Email in if isNull (box e) then None else Some e
        let now = epochNow ()
        let identityId = newId ()

        // Ensure guest exists
        let ensureGuest =
            bind
                (db.prepare("INSERT OR IGNORE INTO guests (id, session_id, created_at) VALUES (?, ?, ?)"))
                [| box guestId; box guestId; box now |]

        // Check if this provider+providerUserId already exists for this guest
        let findExisting =
            bind
                (db.prepare("SELECT id FROM identities WHERE guest_id = ? AND provider = ? AND provider_user_id = ?"))
                [| box guestId; box provider; box providerUserId |]

        let! _ = ensureGuest.run()
        let! existing = findExisting.first()

        let finalId =
            if isNull (box existing) then
                identityId
            else
                existing?id |> unbox<string>

        if isNull (box existing) then
            // New identity — insert but do NOT activate yet (user chooses merge/abandon first)
            let insert =
                bind
                    (db.prepare(
                        "INSERT INTO identities (id, guest_id, provider, provider_user_id, name, picture, email, activated_at, created_at) VALUES (?, ?, ?, ?, ?, ?, ?, NULL, ?)"
                    ))
                    [| box identityId; box guestId; box provider; box providerUserId; box name; box picture; optToDb email; box now |]
            let! _ = insert.run()
            ()
        else
            // Existing identity — update name/picture/email (don't activate yet)
            let update =
                bind
                    (db.prepare(
                        "UPDATE identities SET name = ?, picture = ?, email = ? WHERE id = ?"
                    ))
                    [| box name; box picture; optToDb email; box finalId |]
            let! _ = update.run()
            ()

        // Redirect to claim page where user chooses merge/abandon
        let encodedReturnTo = JS.encodeURIComponent returnTo
        return sprintf "/auth/claim?identity=%s&returnTo=%s" finalId encodedReturnTo
    }

let activateIdentity (request: WorkerRequest) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let guest = resolveGuest request
        let! bodyText = request.text()
        let parsed = JS.JSON.parse bodyText
        let identityId : string = parsed?identityId
        let merge : bool = parsed?merge |> unbox
        let now = epochNow ()

        // Verify this identity belongs to this guest
        let verify =
            bind
                (env.DB.prepare("SELECT id FROM identities WHERE id = ? AND guest_id = ?"))
                [| box identityId; box guest.GuestId |]
        let! verifyResult = verify.first()
        if isNull (box verifyResult) then
            return unauthorized ()
        else

        if merge then
            // Find the current active identity
            let findActive =
                bind
                    (env.DB.prepare("SELECT id FROM identities WHERE guest_id = ? AND activated_at IS NOT NULL ORDER BY activated_at DESC LIMIT 1"))
                    [| box guest.GuestId |]
            let! activeResult = findActive.first()
            if not (isNull (box activeResult)) then
                let oldId : string = activeResult?id
                if oldId <> identityId then
                    // Merge: reassign comments from old identity to new
                    let mergeStmt =
                        bind
                            (env.DB.prepare("UPDATE comments SET identity_id = ?, author = (SELECT name FROM identities WHERE id = ?) WHERE identity_id = ?"))
                            [| box identityId; box identityId; box oldId |]
                    let! _ = mergeStmt.run()
                    ()

        // Activate the identity
        let activate =
            bind
                (env.DB.prepare("UPDATE identities SET activated_at = ? WHERE id = ?"))
                [| box now; box identityId |]
        let! _ = activate.run()

        return okJsonWithCookie """{"ok":true}""" (guestCookieValue guest)
    }

let revertIdentity (request: WorkerRequest) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let guest = resolveGuest request
        let! bodyText = request.text()
        let parsed = JS.JSON.parse bodyText
        let identityId : string = parsed?identityId
        let merge : bool = parsed?merge |> unbox
        let now = epochNow ()

        // Verify target identity belongs to this guest
        let verify =
            bind
                (env.DB.prepare("SELECT id FROM identities WHERE id = ? AND guest_id = ?"))
                [| box identityId; box guest.GuestId |]
        let! verifyResult = verify.first()
        if isNull (box verifyResult) then
            return unauthorized ()
        else

        if merge then
            // Find current active identity
            let findActive =
                bind
                    (env.DB.prepare("SELECT id FROM identities WHERE guest_id = ? AND activated_at IS NOT NULL ORDER BY activated_at DESC LIMIT 1"))
                    [| box guest.GuestId |]
            let! activeResult = findActive.first()
            if not (isNull (box activeResult)) then
                let oldId : string = activeResult?id
                if oldId <> identityId then
                    let mergeStmt =
                        bind
                            (env.DB.prepare("UPDATE comments SET identity_id = ?, author = (SELECT name FROM identities WHERE id = ?) WHERE identity_id = ?"))
                            [| box identityId; box identityId; box oldId |]
                    let! _ = mergeStmt.run()
                    ()

        // Activate the target identity (most-recent activated_at wins)
        let activate =
            bind
                (env.DB.prepare("UPDATE identities SET activated_at = ? WHERE id = ?"))
                [| box now; box identityId |]
        let! _ = activate.run()

        return okJsonWithCookie """{"ok":true}""" (guestCookieValue guest)
    }

let getIdentities (request: WorkerRequest) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let guest = resolveGuest request
        if guest.IsNew then
            return okJson """{"identities":[]}"""
        else
        let stmt =
            bind
                (env.DB.prepare(
                    "SELECT id, provider, provider_user_id, name, picture, email, activated_at, created_at FROM identities WHERE guest_id = ? ORDER BY activated_at DESC NULLS LAST, created_at DESC"
                ))
                [| box guest.GuestId |]
        let! result = stmt.all()
        let identities =
            result.results |> Array.map (fun row ->
                let id = rowStr row "id"
                let provider = rowStr row "provider"
                let name = rowStr row "name"
                let picture = rowStr row "picture"
                let email = rowStrOpt row "email"
                let activatedAt = rowIntOpt row "activated_at"
                let emailJson = match email with Some e -> sprintf ",\"email\":\"%s\"" e | None -> ""
                let activeJson = match activatedAt with Some t -> sprintf ",\"activatedAt\":%d" t | None -> ""
                sprintf """{"id":"%s","provider":"%s","name":"%s","picture":"%s"%s%s}""" id provider name picture emailJson activeJson
            )
        let body = sprintf """{"identities":[%s]}""" (identities |> String.concat ",")
        return okJsonWithCookie body (guestCookieValue guest)
    }

let private toFeedItem (r: MicroblogItemRow) : GetFeed.FeedItem =
    { Id = r.Id
      Title = r.Title
      Slug = r.Slug
      Image = r.Image
      Extract = r.Extract |> Option.map RichContent
      OwnerComment = RichContent r.OwnerComment
      Timestamp = r.CreatedAt }

let private uuidPattern = System.Text.RegularExpressions.Regex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)

let private isUuid (s: string) = uuidPattern.IsMatch(s)

let private slugPattern = System.Text.RegularExpressions.Regex("^[a-z0-9]+(?:-[a-z0-9]+)*$")

let private reservedSlugs = set [ "tag"; "new"; "feed"; "api"; "blobs"; "public"; "admin" ]

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

let private toCommentItem (r: ItemCommentRow) : SubmitComment.CommentItem =
    { Id = r.Id
      ItemId = r.ItemId
      IdentityId = r.IdentityId
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

let getItem (idOrSlug: string) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let itemStmt =
            if isUuid idOrSlug then
                selectMicroblogItem idOrSlug env.DB
            else
                bind (env.DB.prepare("SELECT id, title, link, image, extract, owner_comment, slug, created_at, updated_at, view_count, deleted_at FROM items WHERE slug = ?")) [| box idOrSlug |]

        let! itemResult = itemStmt.all()
        let itemRows = itemResult.results
        if itemRows.Length = 0 then
            return notFound ()
        else
            let r = parseMicroblogItemRow itemRows.[0]
            let commentStmt = selectItemCommentsByItemId r.Id env.DB
            let tagStmt =
                bind
                    (env.DB.prepare("SELECT t.name FROM tags t JOIN item_tags it ON t.id = it.tag_id WHERE it.item_id = ?"))
                    [| box r.Id |]

            let! results = env.DB.batch([| commentStmt; tagStmt |])
            let comments = results.[0].results |> Array.map (parseItemCommentRow >> toCommentItem) |> Array.toList
            let tags = results.[1].results |> Array.map (fun row -> rowStr row "name") |> Array.toList

            let item : SubmitItem.MicroblogItem =
                { Id = r.Id
                  Title = r.Title
                  Slug = r.Slug
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
        let identityId = newId ()
        let now = epochNow ()
        let author = req.Author |> Option.defaultValue "Anonymous"

        let ensureGuest =
            bind
                (env.DB.prepare(
                    "INSERT OR IGNORE INTO guests (id, session_id, created_at) VALUES (?, ?, ?)"
                ))
                [| box guestId; box guestId; box now |]

        let ensureIdentity =
            bind
                (env.DB.prepare(
                    "INSERT OR IGNORE INTO identities (id, guest_id, provider, provider_user_id, name, picture, email, activated_at, created_at) SELECT ?, ?, 'anonymous', '', ?, '', NULL, ?, ? WHERE NOT EXISTS (SELECT 1 FROM identities WHERE guest_id = ? AND activated_at IS NOT NULL)"
                ))
                [| box identityId; box guestId; box author; box now; box now; box guestId |]

        let resolveIdentityId =
            bind
                (env.DB.prepare(
                    "SELECT id FROM identities WHERE guest_id = ? AND activated_at IS NOT NULL ORDER BY activated_at DESC LIMIT 1"
                ))
                [| box guestId |]

        let! _ = env.DB.batch([| ensureGuest; ensureIdentity |])
        let! identityResult = resolveIdentityId.first()
        let activeIdentityId : string =
            if isNull (box identityResult) then identityId
            else identityResult?id

        let insertComment =
            bind
                (env.DB.prepare(
                    "INSERT INTO comments (id, item_id, identity_id, parent_id, author, content, removed, created_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?)"
                ))
                [| box commentId; box req.ItemId; box activeIdentityId; optToDb req.ParentId; box author; box req.Content; box 0; box now |]

        let! _ = env.DB.batch([| insertComment |])

        let newComment : SubmitComment.CommentItem =
            { Id = commentId
              ItemId = req.ItemId
              IdentityId = activeIdentityId
              ParentId = req.ParentId
              Author = author
              Content = RichContent req.Content
              Timestamp = now }

        let event : Models.Ws.NewCommentEvent =
            { Id = commentId; ItemId = req.ItemId; IdentityId = activeIdentityId
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
        match validateSlug req.Slug with
        | Error msg ->
            return validationErrorResponse [ { Field = "Slug"; Message = msg } ]
        | Ok validatedSlug ->
        let ins = insertMicroblogItem env.DB
                    { Title = req.Title; Link = req.Link; Image = req.Image
                      Extract = req.Extract; OwnerComment = req.OwnerComment
                      Slug = validatedSlug; ViewCount = 0 }

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
        let! insertOk = promise {
            try
                let! _ = env.DB.batch(allStmts)
                return true
            with ex ->
                if ex.Message.Contains("UNIQUE") && ex.Message.Contains("slug") then
                    return false
                else return raise ex
        }
        if not insertOk then
            return validationErrorResponse [ { Field = "Slug"; Message = "This slug is already taken" } ]
        else

        let newItem : SubmitItem.MicroblogItem =
            { Id = ins.Id
              Title = req.Title
              Slug = validatedSlug
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
