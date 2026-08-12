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

// The typed R2 binding has no options arg; httpMetadata matters because
// handleBlobServe reads contentType back off it when serving.
[<Emit("$0.put($1, $2, { httpMetadata: { contentType: $3 } })")>]
let private r2PutTyped (blobs: R2Bucket) (key: string) (body: obj) (contentType: string) : JS.Promise<obj> = jsNative

let private avatarTypes = set [ "image/jpeg"; "image/png"; "image/gif"; "image/webp" ]

/// Copy a provider's avatar into R2 and return a local /blobs/ URL.
///
/// Provider avatar URLs are hotlinks: they rot when the user changes or deletes
/// their account, and they leak every reader's request to a third party. The
/// key is content-addressed by source URL, so repeat logins reuse the stored
/// copy (no refetch, no growth) while a changed avatar lands under a new key —
/// which keeps handleBlobServe's immutable cache header honest.
///
/// Best-effort by design: any failure returns the provider URL unchanged, so a
/// flaky avatar host can never break sign-in.
let private cacheAvatar (blobs: R2Bucket) (url: string) : JS.Promise<string> =
    promise {
        if isNull url || url = "" || not (url.StartsWith "https://") then return url
        else
            try
                // HMAC used purely as a content-addressing hash, not for secrecy.
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

/// Collapse duplicate identities on a guest, keeping the one carrying the most
/// history and folding the rest into it. Duplicates are per provider account,
/// so two different Google accounts stay separate while two rows for the *same*
/// Google account merge. Anonymous identities all share ('anonymous', ''), so
/// "one anonymous self per person" is this same rule rather than a special case.
///
/// Returns a map of removed id -> surviving id, so callers holding an identity
/// id can follow it.
let private mergeDuplicateIdentities (db: D1Database) (guestId: string) : JS.Promise<Map<string, string>> =
    promise {
        let! all = Identity.listFor db guestId
        let mutable moved = Map.empty
        let groups = all |> Array.groupBy (fun i -> i.Provider, i.ProviderUserId)
        for (_, rows) in groups do
            if rows.Length > 1 then
                // Rank by comments, then by age — the richest row wins
                let! counted =
                    rows
                    |> Array.map (fun r ->
                        promise {
                            let! row = (bind (db.prepare Sql.countCommentsForIdentity) [| box r.Id |]).first()
                            let n = if isNull (box row) then 0 else row?n |> unbox<int>
                            return r, n
                        })
                    |> Promise.all
                let ordered =
                    counted
                    |> Array.sortBy (fun (r, n) -> -n, r.CreatedAt)
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

        // Look up the provider account globally, not just under this guest —
        // signing in on a second machine should join the identity you already
        // have rather than mint a parallel one.
        let findExisting =
            bind (db.prepare Sql.findIdentityByProviderGlobal) [| box provider; box providerUserId |]
        let! existing = findExisting.first()

        let ownerGuestId =
            if isNull (box existing) then guestId else existing?guest_id |> unbox<string>
        let finalId =
            if isNull (box existing) then identityId else existing?id |> unbox<string>

        // Store our own copy of the avatar rather than the provider's hotlink.
        let! storedPicture = cacheAvatar blobs picture

        if isNull (box existing) then
            // New identity — insert but do NOT activate yet (user chooses merge/abandon first)
            let insert =
                bind
                    (db.prepare Sql.insertProviderIdentity)
                    [| box identityId; box guestId; box provider; box providerUserId; box name; box storedPicture; optToDb email; box now |]
            let! _ = insert.run()
            ()
        else
            // Existing identity — update name/picture/email (don't activate yet)
            let update =
                bind (db.prepare Sql.refreshIdentityProfile) [| box name; box storedPicture; optToDb email; box finalId |]
            let! _ = update.run()
            ()

        // The account already belongs to another guest — this browser joins it.
        // You get one anonymous self, not one per device: this machine's
        // anonymous identity is folded into the adopted guest's (its comments
        // reassigned, usually none or one) and then dropped. Everything else
        // this guest holds moves across intact.
        let adopt =
            if isNull (box existing) || ownerGuestId = guestId then None
            else Some ownerGuestId
        // Merging two guests can leave duplicates — both machines may hold rows
        // for the same provider account, and each will have its own anonymous
        // identity. One pass collapses all of it.
        let! landedId =
            match adopt with
            | None -> promise { return finalId }
            | Some owner ->
                promise {
                    let! _ = (bind (db.prepare Sql.moveIdentitiesToGuest) [| box owner; box guestId |]).run()
                    let! moved = mergeDuplicateIdentities db owner
                    // The identity we're about to hand to the claim page may
                    // itself have been folded away — follow it.
                    return moved |> Map.tryFind finalId |> Option.defaultValue finalId
                }

        // Redirect to claim page where user chooses merge/abandon
        let encodedReturnTo = JS.encodeURIComponent returnTo
        return
            { RedirectUrl = sprintf "/auth/claim?identity=%s&returnTo=%s" landedId encodedReturnTo
              AdoptGuestId = adopt }
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

/// Abandon a credentialed identity: it's parked on a fresh, cookieless guest
/// with its comments still attached, so it sits waiting. Signing in with that
/// provider again — from any browser — finds it by provider account and adopts
/// it back, history intact. Nothing is deleted and nothing is re-attributed.
///
/// Refuses the anonymous identity (it's the fallback, not a connection). The
/// guest is left with an anonymous identity to be, created here if they never
/// had one — which happens when someone signed in before ever commenting.
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

        // Whatever happens, the guest needs an identity to post as afterwards
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

        // Park it on a guest nobody holds a cookie for
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
