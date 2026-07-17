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

let resolveIdentity (db: D1Database) (guestId: string) : JS.Promise<string option> =
    promise {
        let! active = Identity.activeFor db guestId
        return active |> Option.map identityJson
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

        let! _ = (Identity.ensureGuestStmt db guestId now).run()

        // Check if this provider+providerUserId already exists for this guest
        let findExisting =
            bind (db.prepare Sql.findIdentityByProvider) [| box guestId; box provider; box providerUserId |]
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
                    (db.prepare Sql.insertProviderIdentity)
                    [| box identityId; box guestId; box provider; box providerUserId; box name; box picture; optToDb email; box now |]
            let! _ = insert.run()
            ()
        else
            // Existing identity — update name/picture/email (don't activate yet)
            let update =
                bind (db.prepare Sql.refreshIdentityProfile) [| box name; box picture; optToDb email; box finalId |]
            let! _ = update.run()
            ()

        // Redirect to claim page where user chooses merge/abandon
        let encodedReturnTo = JS.encodeURIComponent returnTo
        return sprintf "/auth/claim?identity=%s&returnTo=%s" finalId encodedReturnTo
    }

/// Switch the guest's active identity, optionally bringing attributed
/// content along. Serves both /api/auth/activate (claim) and
/// /api/auth/revert (switch) — the policy is identical.
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

let private toCommentItem (pictureOf: string -> string) (r: ItemCommentRow) : SubmitComment.CommentItem =
    { Id = r.Id
      ItemId = r.ItemId
      IdentityId = r.IdentityId
      ParentId = r.ParentId
      Author = r.Author
      Picture = pictureOf r.IdentityId
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
                bind (env.DB.prepare Sql.itemBySlug) [| box idOrSlug |]

        let! itemResult = itemStmt.all()
        let itemRows = itemResult.results
        if itemRows.Length = 0 then
            return notFound ()
        else
            let r = parseMicroblogItemRow itemRows.[0]
            let commentStmt = selectItemCommentsByItemId r.Id env.DB
            let tagStmt = bind (env.DB.prepare Sql.tagsForItem) [| box r.Id |]
            let pictureStmt = bind (env.DB.prepare Sql.picturesForItemComments) [| box r.Id |]

            let! results = env.DB.batch([| commentStmt; tagStmt; pictureStmt |])
            let pictures =
                results.[2].results
                |> Array.map (fun row -> rowStr row "id", rowStr row "picture")
                |> Map.ofArray
            let pictureOf identityId = pictures |> Map.tryFind identityId |> Option.defaultValue ""
            let comments = results.[0].results |> Array.map (parseItemCommentRow >> toCommentItem pictureOf) |> Array.toList
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
                [| box commentId; box req.ItemId; box activeIdentityId; optToDb req.ParentId; box author; box req.Content; box 0; box now |]

        let! _ = env.DB.batch([| insertComment |])

        let newComment : SubmitComment.CommentItem =
            { Id = commentId
              ItemId = req.ItemId
              IdentityId = activeIdentityId
              ParentId = req.ParentId
              Author = author
              Picture = activePicture
              Content = RichContent req.Content
              Timestamp = now }

        let event : Models.Ws.NewCommentEvent =
            { Id = commentId; ItemId = req.ItemId; IdentityId = activeIdentityId
              ParentId = req.ParentId; Author = author; Picture = activePicture
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
        let! result = env.DB.prepare(Sql.tagNames).all()
        let tags = result.results |> Array.map (fun r -> rowStr r "name") |> Array.toList
        let body =
            Encode.object [
                "tags", Encode.list (List.map Encode.string tags)
            ] |> Encode.toString 0
        return okJson body
    }

let getItemsByTag (tag: string) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let stmt = bind (env.DB.prepare Sql.itemsByTag) [| box tag |]
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
                        (env.DB.prepare Sql.insertTag)
                        [| box tagId; box tagName; box ins.CreatedAt |]
                let linkTag =
                    bind
                        (env.DB.prepare Sql.linkItemTag)
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
