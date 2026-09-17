module Server.Handlers

open Fable.Core
open Fable.Core.JsInterop
open Thoth.Json
open Hedge.Interface
open Hedge.Validate
open Hedge.Workers
open Hedge.Router
open Hedge.GuestSession
open Codecs
open Blog.Api
open Server.Env
open Server.Db
// darwin.news glue (getRhymes) reaches into the composed blog module's surface.
open Blog.Codecs
open Blog.Db

let private identityJson (i: IdentityRow) : string =
    let emailJson = match i.Email with Some e -> sprintf ",\"email\":\"%s\"" e | None -> ""
    sprintf """{"id":"%s","provider":"%s","name":"%s","picture":"%s"%s}""" i.Id i.Provider i.Name i.Picture emailJson

let private avatarTypes = set [ "image/jpeg"; "image/png"; "image/gif"; "image/webp" ]

/// Copy a provider's avatar into R2 and return a local /blobs/ URL — a thin alias over the
/// shared `rehostRemoteImage` primitive (content-addressed under "avatars/", dedup'd,
/// best-effort). Provider avatar URLs are hotlinks that rot and leak each reader's request to
/// a third party; rehosting fixes both, and repeat logins reuse the stored copy. Avatars
/// exclude SVG (avatarTypes). Same key scheme + salt as before, so existing avatars still resolve.
let private cacheAvatar (blobs: R2Bucket) (url: string) : JS.Promise<string> =
    rehostRemoteImage blobs "avatars" avatarTypes url

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
        // Identity mutation is a WRITE: require an accepted signed guest, never create one.
        let! authz = Server.GuestConfig.require env request
        match authz with
        | Rejected -> return unauthorized ()
        | Accepted guest ->
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
        match guest.Replacement with
        | Some c -> return okJsonWithCookie """{"ok":true}""" c
        | None -> return okJson """{"ok":true}"""
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
        // Identity mutation is a WRITE: require an accepted signed guest, never create one.
        let! authz = Server.GuestConfig.require env request
        match authz with
        | Rejected -> return unauthorized ()
        | Accepted guest ->
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

        match guest.Replacement with
        | Some c -> return okJsonWithCookie """{"ok":true}""" c
        | None -> return okJson """{"ok":true}"""
    }

let activateIdentity (request: WorkerRequest) (env: Env) : JS.Promise<WorkerResponse> =
    switchIdentity request env

let revertIdentity (request: WorkerRequest) (env: Env) : JS.Promise<WorkerResponse> =
    switchIdentity request env

let getIdentities (request: WorkerRequest) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        // Identity listing requires an accepted credential (it exposes a guest's linked accounts).
        // Without one, return an empty list rather than bootstrapping — the client establishes a
        // session via /api/auth/me first, then lists.
        let! authz = Server.GuestConfig.require env request
        match authz with
        | Rejected -> return okJson """{"identities":[]}"""
        | Accepted guest ->
        let! rows = Identity.listFor env.DB guest.GuestId
        let identities =
            rows |> Array.map (fun i ->
                let emailJson = match i.Email with Some e -> sprintf ",\"email\":\"%s\"" e | None -> ""
                let activeJson = match i.ActivatedAt with Some t -> sprintf ",\"activatedAt\":%d" t | None -> ""
                sprintf """{"id":"%s","provider":"%s","name":"%s","picture":"%s"%s%s}""" i.Id i.Provider i.Name i.Picture emailJson activeJson
            )
        let body = sprintf """{"identities":[%s]}""" (identities |> String.concat ",")
        match guest.Replacement with
        | Some c -> return okJsonWithCookie body c
        | None -> return okJson body
    }

let private toFeedItem (r: ItemRow) : GetFeed.FeedItem =
    { Id = r.Id
      Title = r.Title
      Slug = r.Slug
      Image = r.Image
      Extract = r.Extract |> Option.map RichContent
      OwnerComment = RichContent r.OwnerComment
      Timestamp = r.ArticleDate }



/// GET /api/rhymes — every `rhyme-*` tag with the items sharing it, for
/// rhyming.darwin.news. A bespoke darwin.news route (not a reflected endpoint): it
/// reads the composed blog module's tables (via Blog.Sql + the generated
/// Server.Db.Tables) and hand-builds the JSON with the blog FeedItem codec.
let getRhymes (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let! tagRes = env.DB.prepare(Sql.rhymeTags).all()
        let tags = tagRes.results |> Array.map (fun r -> rowStr r "name") |> Array.toList
        let groups = ResizeArray<string * GetFeed.FeedItem list>()
        for tag in tags do
            let! itemsRes = (bind (env.DB.prepare Blog.Sql.itemsByTag) [| box tag; box 12 |]).all()
            let items = itemsRes.results |> Array.map (parseItemRow >> toFeedItem) |> Array.toList
            // A rhyme needs at least a pair; skip empty/singleton tags.
            if List.length items >= 2 then groups.Add(tag, items)
        let body =
            Encode.object [
                "rhymes", Encode.list [
                    for (tag, items) in groups ->
                        Encode.object [
                            "tag", Encode.string tag
                            "items", Encode.list (List.map Encode.blogFeedItem items)
                        ]
                ]
            ] |> Encode.toString 0
        return okJson body
    }

