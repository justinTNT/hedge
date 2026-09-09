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

let private identityJson (i: IdentityRow) : string =
    let emailJson = match i.Email with Some e -> sprintf ",\"email\":\"%s\"" e | None -> ""
    sprintf """{"id":"%s","provider":"%s","name":"%s","picture":"%s"%s}""" i.Id i.Provider i.Name i.Picture emailJson

[<Emit("$0.arrayBuffer()")>]
let private responseArrayBuffer (response: WorkerResponse) : JS.Promise<obj> = jsNative

[<Emit("$0.headers.get($1)")>]
let private responseHeader (response: WorkerResponse) (name: string) : string = jsNative

[<Emit("$0.put($1, $2, { httpMetadata: { contentType: $3 } })")>]
let private r2PutTyped (blobs: R2Bucket) (key: string) (body: obj) (contentType: string) : JS.Promise<obj> = jsNative

let private avatarTypes = set [ "image/jpeg"; "image/png"; "image/gif"; "image/webp" ]

/// Copy a provider's avatar into R2 and return a local /blobs/ URL. Best-effort:
/// any failure returns the provider URL unchanged, so a flaky avatar host can
/// never break sign-in.
let private cacheAvatar (blobs: R2Bucket) (url: string) : JS.Promise<string> =
    promise {
        if isNull url || url = "" || not (url.StartsWith "https://") then return url
        else
            try
                let! digest = hmacSha256 "hedge-avatar" url
                let key = sprintf "avatars/%s" (digest.Substring(0, 32))
                let! existing = blobs.get key
                match existing with
                | Some _ -> return sprintf "/blobs/%s" key
                | None ->
                    let! response = fetchRaw url (createObj [])
                    if not response.ok then return url
                    else
                        let raw = responseHeader response "content-type"
                        let contentType =
                            if isNull raw then ""
                            else raw.Split(';').[0].Trim().ToLowerInvariant()
                        if not (avatarTypes.Contains contentType) then return url
                        else
                            let! body = responseArrayBuffer response
                            let! _ = r2PutTyped blobs key body contentType
                            return sprintf "/blobs/%s" key
            with ex ->
                JS.console.error ("avatar cache failed: " + ex.Message)
                return url
    }

let resolveIdentity (db: D1Database) (guestId: string) : JS.Promise<string option> =
    promise {
        let! active = Identity.activeFor db guestId
        return active |> Option.map identityJson
    }

/// Collapse duplicate identities on a guest, keeping the richest and folding the
/// rest into it. Returns a map of removed id -> survivor.
let private mergeDuplicateIdentities (db: D1Database) (guestId: string) : JS.Promise<Map<string, string>> =
    promise {
        let! all = Identity.listFor db guestId
        let mutable moved = Map.empty
        let groups = all |> Array.groupBy (fun i -> i.Provider, i.ProviderUserId)
        for (_, rows) in groups do
            if rows.Length > 1 then
                let! counted =
                    rows
                    |> Array.map (fun r ->
                        promise {
                            let! row = (bind (db.prepare Sql.countCommentsForIdentity) [| box r.Id |]).first()
                            let n = if isNull (box row) then 0 else row?n |> unbox<int>
                            return r, n
                        })
                    |> Promise.all
                let ordered = counted |> Array.sortBy (fun (r, n) -> -n, r.CreatedAt)
                let survivor = fst ordered.[0]
                for (dup, _) in ordered.[1..] do
                    do! Attribution.reassign db dup.Id survivor.Id
                    let! _ = (bind (db.prepare Sql.deleteIdentityById) [| box dup.Id |]).run()
                    moved <- moved |> Map.add dup.Id survivor.Id
        return moved
    }

let onOAuthComplete (db: D1Database) (blobs: R2Bucket) (guestId: string) (userInfoObj: obj) (returnTo: string) : JS.Promise<OAuthComplete> =
    promise {
        let name : string = userInfoObj?Name
        let picture : string = userInfoObj?PictureUrl
        let provider : string = userInfoObj?Provider
        let providerUserId : string = userInfoObj?ProviderUserId
        let email = let e : string = userInfoObj?Email in if isNull e then None else Some e
        let now = epochNow ()
        let identityId = newId ()

        let! _ = (Identity.ensureGuestStmt db guestId now).run()

        let findExisting =
            bind (db.prepare Sql.findIdentityByProviderGlobal) [| box provider; box providerUserId |]
        let! existing = findExisting.first()

        let ownerGuestId =
            if isNull (box existing) then guestId else existing?guest_id |> unbox<string>
        let finalId =
            if isNull (box existing) then identityId else existing?id |> unbox<string>

        let! storedPicture = cacheAvatar blobs picture

        if isNull (box existing) then
            let insert =
                bind
                    (db.prepare Sql.insertProviderIdentity)
                    [| box identityId; box guestId; box provider; box providerUserId; box name; box storedPicture; optToDb email; box now |]
            let! _ = insert.run()
            ()
        else
            let update =
                bind (db.prepare Sql.refreshIdentityProfile) [| box name; box storedPicture; optToDb email; box finalId |]
            let! _ = update.run()
            ()

        let adopt =
            if isNull (box existing) || ownerGuestId = guestId then None
            else Some ownerGuestId
        let! landedId =
            match adopt with
            | None -> promise { return finalId }
            | Some owner ->
                promise {
                    let! _ = (bind (db.prepare Sql.moveIdentitiesToGuest) [| box owner; box guestId |]).run()
                    let! moved = mergeDuplicateIdentities db owner
                    return moved |> Map.tryFind finalId |> Option.defaultValue finalId
                }

        let encodedReturnTo = JS.encodeURIComponent returnTo
        return
            { RedirectUrl = sprintf "/auth/claim?identity=%s&returnTo=%s" landedId encodedReturnTo
              AdoptGuestId = adopt }
    }

/// Switch the guest's active identity, optionally bringing content along.
let private switchIdentity (request: WorkerRequest) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let guest = resolveGuest request
        let! bodyText = request.text()
        let parsed = JS.JSON.parse bodyText
        let identityId : string = parsed?identityId
        let merge : bool = parsed?merge |> unbox
        let now = epochNow ()

        let! owned = Identity.belongsToGuest env.DB identityId guest.GuestId
        if not owned then
            return unauthorized ()
        else

        if merge then
            let! active = Identity.activeFor env.DB guest.GuestId
            match active with
            | Some current when current.Id <> identityId ->
                do! Attribution.reassign env.DB current.Id identityId
            | _ -> ()

        do! Identity.setActive env.DB identityId now
        return okJsonWithCookie """{"ok":true}""" (guestCookieValue guest)
    }

/// Abandon a credentialed identity onto a fresh cookieless guest, its comments
/// intact so signing in again reclaims it. Refuses the anonymous identity.
let disconnectIdentity (request: WorkerRequest) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let guest = resolveGuest request
        let! bodyText = request.text()
        let parsed = JS.JSON.parse bodyText
        let identityId : string = parsed?identityId
        let fallbackName =
            let n : string = parsed?name
            if isNull n || n = "" then "Anonymous" else n
        let now = epochNow ()

        let! all = Identity.listFor env.DB guest.GuestId
        match all |> Array.tryFind (fun i -> i.Id = identityId) with
        | None -> return unauthorized ()
        | Some target ->

        if target.Provider = "anonymous" then
            return badRequest "The anonymous identity is the fallback and can't be disconnected"
        else

        let! active = Identity.activeFor env.DB guest.GuestId
        let wasActive = active |> Option.map (fun i -> i.Id) |> Option.defaultValue "" = identityId

        let! anonId =
            match all |> Array.tryFind (fun i -> i.Provider = "anonymous") with
            | Some anon -> promise { return anon.Id }
            | None ->
                promise {
                    let created = newId ()
                    let! _ =
                        (bind
                            (env.DB.prepare Sql.insertAnonymousIdentity)
                            [| box created; box guest.GuestId; box fallbackName; jsNull; box now |]).run()
                    return created
                }

        let orphanGuest = newId ()
        let! _ = (Identity.ensureGuestStmt env.DB orphanGuest now).run()
        let! _ = (bind (env.DB.prepare Sql.moveIdentityToGuest) [| box orphanGuest; box identityId |]).run()

        if wasActive then
            do! Identity.setActive env.DB anonId now

        return okJsonWithCookie """{"ok":true}""" (guestCookieValue guest)
    }

let activateIdentity (request: WorkerRequest) (env: Env) : JS.Promise<WorkerResponse> =
    switchIdentity request env

let revertIdentity (request: WorkerRequest) (env: Env) : JS.Promise<WorkerResponse> =
    switchIdentity request env

let getIdentities (request: WorkerRequest) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let guest = resolveGuest request
        if guest.IsNew then
            return okJson """{"identities":[]}"""
        else
        let! rows = Identity.listFor env.DB guest.GuestId
        let identities =
            rows |> Array.map (fun i ->
                let emailJson = match i.Email with Some e -> sprintf ",\"email\":\"%s\"" e | None -> ""
                let activeJson = match i.ActivatedAt with Some t -> sprintf ",\"activatedAt\":%d" t | None -> ""
                sprintf """{"id":"%s","provider":"%s","name":"%s","picture":"%s"%s%s}""" i.Id i.Provider i.Name i.Picture emailJson activeJson
            )
        let body = sprintf """{"identities":[%s]}""" (identities |> String.concat ",")
        return okJsonWithCookie body (guestCookieValue guest)
    }

// ---- Articles ----

let private toArticleItem (row: obj) : GetArticles.ArticleItem =
    { Id = rowStr row "id"
      Title = rowStr row "title"
      Slug = rowStrOpt row "slug"
      Image = rowStrOpt row "image"
      Teaser = rowStrOpt row "teaser" |> Option.map RichContent
      Timestamp = rowInt row "article_date" }

let private uuidPattern = System.Text.RegularExpressions.Regex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
let private isUuid (s: string) = uuidPattern.IsMatch(s)

let private toCommentItem (pictureOf: string -> string) (r: ArticleCommentRow) : SubmitComment.CommentItem =
    { Id = r.Id
      ArticleId = r.ArticleId
      IdentityId = r.IdentityId
      ParentId = r.ParentId
      Author = r.Author
      Picture = pictureOf r.IdentityId
      Content = RichContent r.Content
      Timestamp = r.CreatedAt }

let private pageSize = 8   // small: load less, reload more

let getArticles (cursor: string) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let stmt =
            if cursor = "start" || cursor = "" then
                bind (env.DB.prepare Sql.listFirstPage) [| box (pageSize + 1) |]
            else
                let sep = cursor.IndexOf('_')
                let ts = int (cursor.Substring(0, sep))
                let id = cursor.Substring(sep + 1)
                bind (env.DB.prepare Sql.listAfterCursor) [| box ts; box ts; box id; box (pageSize + 1) |]
        let! result = stmt.all()
        let rows = result.results |> Array.map toArticleItem |> Array.toList
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
                "items", Encode.list (List.map Encode.articleItem pageItems)
                "nextCursor", (match nextCursor with Some c -> Encode.string c | None -> Encode.nil)
            ] |> Encode.toString 0
        return okJson body
    }

let getArticle (idOrSlug: string) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let articleStmt =
            if isUuid idOrSlug then selectArticle idOrSlug env.DB
            else bind (env.DB.prepare Sql.articleBySlug) [| box idOrSlug |]

        let! articleResult = articleStmt.all()
        let rows = articleResult.results
        if rows.Length = 0 then
            return notFound ()
        else
            let r = parseArticleRow rows.[0]
            let commentStmt = selectArticleCommentsByArticleId r.Id env.DB
            let pictureStmt = bind (env.DB.prepare Sql.picturesForArticleComments) [| box r.Id |]

            let! results = env.DB.batch([| commentStmt; pictureStmt |])
            let pictures =
                results.[1].results
                |> Array.map (fun row -> rowStr row "id", rowStr row "picture")
                |> Map.ofArray
            let pictureOf identityId = pictures |> Map.tryFind identityId |> Option.defaultValue ""
            let comments = results.[0].results |> Array.map (parseArticleCommentRow >> toCommentItem pictureOf) |> Array.toList

            let article : GetArticle.ArticleDetail =
                { Id = r.Id
                  Title = r.Title
                  Slug = r.Slug
                  Image = r.Image
                  Teaser = r.Teaser |> Option.map RichContent
                  Body = RichContent r.Body
                  Comments = comments
                  Timestamp = r.ArticleDate }

            let body =
                Encode.object [ "article", Encode.articleDetail article ] |> Encode.toString 0
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
                (env.DB.prepare Sql.insertComment)
                [| box commentId; box req.ArticleId; box activeIdentityId; optToDb req.ParentId; box author; box req.Content; box 0; box now |]

        let! _ = env.DB.batch([| insertComment |])

        let newComment : SubmitComment.CommentItem =
            { Id = commentId
              ArticleId = req.ArticleId
              IdentityId = activeIdentityId
              ParentId = req.ParentId
              Author = author
              Picture = activePicture
              Content = RichContent req.Content
              Timestamp = now }

        // Broadcast to everyone else viewing this article, so the comment
        // appears live without a refresh (the submitter appends it locally
        // from the response below; the WS echo is deduped client-side).
        let event : Models.Ws.NewCommentEvent =
            { Id = commentId
              ArticleId = req.ArticleId
              IdentityId = activeIdentityId
              ParentId = req.ParentId
              Author = author
              Picture = activePicture
              Content = req.Content
              Timestamp = now }
        let eventJson =
            Encode.object [
                "type", Encode.string "NewComment"
                "payload", Codecs.Encode.newCommentEvent event
            ] |> Encode.toString 0
        let doId = env.EVENTS.idFromName(req.ArticleId)
        let stub = env.EVENTS.get(doId)
        let broadcastReq = createRequest "https://do/broadcast" "POST" eventJson
        ctx.waitUntil(stub.fetch(broadcastReq) |> unbox<JS.Promise<obj>>)

        let body =
            Encode.object [ "comment", Encode.commentItem newComment ] |> Encode.toString 0

        return okJsonWithCookie body (guestCookieValue guest)
    }
