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
open Models.Api
open Server.Env
open Server.Db

let private identityJson (i: IdentityRow) : string =
    let emailJson = match i.Email with Some e -> sprintf ",\"email\":\"%s\"" e | None -> ""
    sprintf """{"id":"%s","provider":"%s","name":"%s","picture":"%s"%s}""" i.Id i.Provider i.Name i.Picture emailJson

let private avatarTypes = set [ "image/jpeg"; "image/png"; "image/gif"; "image/webp" ]

/// Copy a provider's avatar into R2 and return a local /blobs/ URL — a thin alias over the
/// shared `rehostRemoteImage` primitive (content-addressed under "avatars/", dedup'd,
/// best-effort; SVG excluded). Same key scheme + salt as before, so existing avatars resolve.
let private cacheAvatar (blobs: R2Bucket) (url: string) : JS.Promise<string> =
    rehostRemoteImage blobs "avatars" avatarTypes url

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
                            let countSql = Attribution.countCommentsSql AttributionPolicy.commentTables
                            let! row = (bind (db.prepare countSql) [| for _ in AttributionPolicy.commentTables -> box r.Id |]).first()
                            let n = if isNull (box row) then 0 else row?n |> unbox<int>
                            return r, n
                        })
                    |> Promise.all
                let ordered = counted |> Array.sortBy (fun (r, n) -> -n, r.CreatedAt)
                let survivor = fst ordered.[0]
                for (dup, _) in ordered.[1..] do
                    do! Attribution.reassign db AttributionPolicy.reassignStatements dup.Id survivor.Id
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
            let sql = Attribution.findByProviderGlobalSql AttributionPolicy.commentTables
            bind (db.prepare sql) [| box provider; box providerUserId |]
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
                do! Attribution.reassign env.DB AttributionPolicy.reassignStatements current.Id identityId
            | _ -> ()

        do! Identity.setActive env.DB identityId now
        match guest.Replacement with
        | Some c -> return okJsonWithCookie """{"ok":true}""" c
        | None -> return okJson """{"ok":true}"""
    }

/// Abandon a credentialed identity onto a fresh cookieless guest, its comments
/// intact so signing in again reclaims it. Refuses the anonymous identity.
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

