module Server.Handlers

// This app's server handlers. The identity handlers now live in the shared identity module
// (Identity.Handlers, via identity.server.props); this file just binds the host seams (Env DB/Blobs,
// the guest-write authorizer, this site's attribution policy, and the /curator return policy) and
// re-exposes them under the names Worker.fs wires. What remains genuinely app-specific is the bespoke
// darwin.news `getRhymes` route over the composed blog module's tables.

open Fable.Core
open Fable.Core.JsInterop
open Thoth.Json
open Hedge.Interface
open Hedge.Workers
open Hedge.Router
open Blog.Api
open Server.Env
open Blog.Codecs
open Blog.Db

// ---- Identity handlers: host seams bound to the shared Identity.Handlers ----

/// OAuth-completion seams (env-free): this site composes only blog, so its attribution policy is the
/// blog comment table/statement; a curator returns to the standalone /curator page (auto-activate +
/// document nav), which the blog SPA switcher can't render.
let private oauthDeps : Identity.Handlers.OAuthDeps =
    { ReassignStatements = Server.AttributionPolicy.reassignStatements
      CommentTables = Server.AttributionPolicy.commentTables
      ActivateOnReturn = fun returnTo ->
        (returnTo.TrimEnd('/')).EndsWith("/curator")
        // The mobile browser-OAuth handoff returns to /api/mobile/return?challenge=… (a same-site path,
        // so it survives safeReturnPath). Activate the verified identity there too — no claim screen.
        || returnTo.StartsWith("/api/mobile/return") }

/// Write-handler seams, per request env: the DB, the guest-write authorizer, and the attribution policy.
/// Public: Server.ModuleServices.identityHttp builds the IdentityHttp dispatch over these deps.
let writeDeps (env: Env) : Identity.Handlers.WriteDeps =
    { DB = env.DB
      RequireGuest = Server.GuestConfig.require env
      ReassignStatements = Server.AttributionPolicy.reassignStatements
      CommentTables = Server.AttributionPolicy.commentTables }

/// Framework OAuthConfig hooks (signatures fixed by Hedge.Router.OAuthConfig).
let resolveIdentity = Identity.Handlers.resolveIdentity
let onOAuthComplete : D1Database -> R2Bucket -> string -> obj -> string -> JS.Promise<OAuthComplete> =
    Identity.Handlers.onOAuthComplete oauthDeps

// ---- Mobile bearer-session routes (Capacitor POC) ----

/// 30-day absolute mobile session lifetime (the plan's default; provider re-auth after).
let [<Literal>] private MobileSessionTtl = 2592000

/// POST /api/mobile/bootstrap — mint an ANONYMOUS mobile bearer session (Capacitor POC first launch)
/// so on-device commenting works before login. The app is cross-origin (capacitor://localhost), so no
/// same-origin gate; POC only — rate-limiting/abuse controls are a follow-up. Returns the opaque bearer
/// once; it authenticates only the fresh anonymous guest (possession is not proof of an identity).
let mobileBootstrap (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let now = epochNow ()
        do! Identity.Mobile.purgeExpired env.DB now
        let guestId = newId ()
        let! token = Identity.Mobile.mintSession env.DB guestId now MobileSessionTtl
        return okJson (sprintf """{"token":"%s"}""" token)
    }

/// GET /api/mobile/me — resolve the request's bearer to its guest's active identity (or null when
/// anonymous / no valid bearer). The bearer analogue of /api/auth/me for native clients.
let mobileMe (request: WorkerRequest) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        match! Hedge.MobileSession.resolve (Server.GuestConfig.mobileDeps env) request with
        | Hedge.MobileSession.Valid guestId ->
            let! identityJson = resolveIdentity env.DB guestId
            match identityJson with
            | Some json -> return okJson (sprintf """{"guest":{"guestId":"%s","identity":%s}}""" guestId json)
            | None -> return okJson """{"guest":null}"""     // a VALID anonymous session (no linked identity)
        // A present-but-unresolvable bearer (revoked/expired) or none at all is 401 — distinct from a
        // valid anon session — so the client clears the dead token and re-bootstraps instead of looping.
        | Hedge.MobileSession.Invalid | Hedge.MobileSession.NoBearer -> return unauthorized ()
    }

/// The app's registered custom scheme (POC). The one-time CODE (never a bearer) rides in the deeplink.
let [<Literal>] private MobileDeeplink = "wtfail://auth"

/// GET /api/mobile/return — the same-site landing after browser-OAuth. The system browser holds the
/// verified guest cookie here (set by the OAuth callback, which activated the identity because
/// ActivateOnReturn matches this path). Mint a one-time PKCE code bound to that guest + the app's
/// challenge, then 302 to the app's deeplink; the app exchanges the code (+ its verifier) for a bearer.
let mobileReturn (request: WorkerRequest) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let challenge = getQueryParam request.url "challenge"
        let! authz = Server.GuestConfig.require env request
        match authz with
        | Hedge.GuestSession.Accepted guest when not (isNull (box challenge)) && challenge <> "" ->
            let! code = Identity.Mobile.mintCode env.DB guest.GuestId challenge (epochNow ())
            return redirectResponseOpt (sprintf "%s?code=%s" MobileDeeplink code) None
        | Hedge.GuestSession.Accepted _ -> return redirectResponseOpt (sprintf "%s?error=challenge" MobileDeeplink) None
        | Hedge.GuestSession.Rejected -> return redirectResponseOpt (sprintf "%s?error=session" MobileDeeplink) None
    }

/// POST /api/mobile/exchange {code, verifier} — the app trades its one-time code + PKCE verifier,
/// presenting its OLD anonymous bearer, for a verified bearer. Verifies the code and the PKCE proof,
/// MERGES the app's anonymous content into the verified identity (cross-guest reassign), rotates the
/// old anon session out, and mints the verified session. Takes the ALREADY-DECODED (code, verifier) from
/// the generated MobileHttp dispatch (which decodes the bounded body); authentication is the code + PKCE
/// proof below, AFTER the required-field check — the optional old bearer identifies content to merge, not
/// permission to sign in.
let mobileExchange (rawCode: string) (verifier: string) (request: WorkerRequest) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        if isNull (box rawCode) || rawCode = "" || isNull (box verifier) || verifier = "" then
            return badRequest "Missing code or verifier"
        else
            let now = epochNow ()
            // Consume + PKCE-verify atomically: None = bad/expired code OR wrong verifier (which does
            // NOT burn the code — see Identity.Mobile.consumeCode).
            let! consumed = Identity.Mobile.consumeCode env.DB now rawCode verifier
            match consumed with
            | None -> return unauthorized ()
            | Some verifiedGuestId ->
                match Hedge.MobileSession.readBearer request with
                | Some rawBearer ->
                    let! oldHash = Hedge.MobileSession.sha256Hex rawBearer
                    let! anonGuestId = Identity.Mobile.resolveByHash env.DB now oldHash
                    match anonGuestId with
                    | Some ag ->
                        do! Identity.Mobile.mergeAnonInto env.DB Server.AttributionPolicy.reassignStatements ag verifiedGuestId now
                        do! Identity.Mobile.revokeByHash env.DB oldHash
                    | None -> ()
                | None -> ()
                let! token = Identity.Mobile.mintSession env.DB verifiedGuestId now MobileSessionTtl
                return okJson (sprintf """{"token":"%s"}""" token)
    }

/// POST /api/mobile/signout — revoke the presented bearer server-side (idempotent). The client reports
/// sign-out success only after this returns, so a replayed bearer no longer authenticates.
let mobileSignout (request: WorkerRequest) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        match Hedge.MobileSession.readBearer request with
        | Some rawBearer ->
            let! hash = Hedge.MobileSession.sha256Hex rawBearer
            do! Identity.Mobile.revokeByHash env.DB hash
        | None -> ()
        return okJson """{"ok":true}"""
    }

/// POST /api/mobile/blobs — bearer-authorized comment-image upload for the native app. The framework's
/// /api/blobs/guest is cookie-only, so this resolves the bearer (GuestConfig.require is bearer-aware)
/// and reuses the shared upload handler with the resolved guest.
let mobileBlobUpload (request: WorkerRequest) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        match! Server.GuestConfig.require env request with
        | Hedge.GuestSession.Accepted a -> return! handleGuestBlobUpload request env.BLOBS a.GuestId a.Replacement
        | Hedge.GuestSession.Rejected -> return unauthorized ()
    }

// The /api/auth/{identities,disconnect,revert,activate} routes are now dispatched through the composed
// IdentityHttp module (Server.ModuleServices.identityHttp over writeDeps), replacing the hand-wired wrappers
// that used to live here. The shared Identity.Handlers still exposes the body-reading wrappers for hosts not
// yet migrated (articles).

// ---- darwin.news glue (app-specific) ----

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
