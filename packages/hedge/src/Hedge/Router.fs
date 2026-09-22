module Hedge.Router

open Fable.Core
open Fable.Core.JsInterop
open Thoth.Json
open Hedge.Workers
open Hedge.Validate
open Hedge.OAuth
open Hedge.GuestSession

/// Minimal router for Workers.
/// Pattern matches on method + path to dispatch to handlers.

/// The URL pathname keeps percent-encoding (e.g. %20 for a space), but R2 keys
/// are stored decoded — decode before looking a blob up so they match.
[<Emit("decodeURIComponent($0)")>]
let private decodeUri (s: string) : string = jsNative

type Route =
    | GET of string
    | POST of string
    | PUT of string
    | DELETE of string
    | OPTIONS of string

type RouteMatch =
    | Exact of string
    | WithParam of prefix: string * param: string

[<Emit("new URL($0)")>]
let private createUrl (url: string) : obj = jsNative

let parseRoute (request: WorkerRequest) : Route =
    let url = createUrl request.url
    let path : string = url?pathname
    match request.method with
    | "GET" -> GET path
    | "POST" -> POST path
    | "PUT" -> PUT path
    | "DELETE" -> DELETE path
    | "OPTIONS" -> OPTIONS path
    | _ -> GET path  // fallback

let matchPath (pattern: string) (path: string) : RouteMatch option =
    if pattern.Contains(":id") then
        let idIdx = pattern.IndexOf(":id")
        let prefix = pattern.Substring(0, idIdx)
        let suffix = pattern.Substring(idIdx + 3)
        if path.StartsWith(prefix) && path.EndsWith(suffix) then
            let paramLen = path.Length - prefix.Length - suffix.Length
            if paramLen > 0 then
                let param = path.Substring(prefix.Length, paramLen)
                Some (WithParam (prefix, param))
            else None
        else None
    elif pattern = path then
        Some (Exact path)
    else None

// The old unsigned guest resolver (GuestContext/resolveGuest/guestCookieValue — a raw arbitrary-id
// cookie) is gone: every reader/writer now goes through the signed policy Hedge.GuestSession, bound
// per app via WorkerConfig.GuestSession and the modules' Services.Guest. No authenticated route
// accepts an arbitrary client-supplied guest id any more.

[<Emit("$0.ASSETS.fetch($1)")>]
let private fetchFromAssets (env: obj) (request: WorkerRequest) : JS.Promise<WorkerResponse> = jsNative

/// Where a mounted view lives: on its own host, or under a path prefix of the
/// main host. Path mounts are the composition primitive for merged sites
/// (e.g. a blog module at /blog); host mounts for a distinct subdomain.
type MountOn =
    | OnHost of string
    | OnPath of string

/// A mounted view: matching requests are served the `Shell` HTML asset (its own
/// client entry) from ASSETS instead of index.html. One deploy, one D1, multiple
/// client views — the composition primitive.
///
/// `When` gates the mount per-request against the raw `env`, so one shared worker
/// binary can mount a module for some deployments and not others (mounts are
/// inherently per-env). Unconditional mounts pass `fun _ -> true`.
type Mount = { On: MountOn; Shell: string; When: obj -> bool }

[<Emit("new URL($0.url).hostname")>]
let private requestHost (request: WorkerRequest) : string = jsNative

/// Serve a specific shell asset (e.g. "/rhyming.html") for this request's origin.
[<Emit("$0.ASSETS.fetch(new Request(new URL($2, $1.url)))")>]
let private fetchShell (env: obj) (request: WorkerRequest) (shell: string) : JS.Promise<WorkerResponse> = jsNative


/// Response helpers
let jsonResponse (body: string) (status: int) : WorkerResponse =
    let options = createObj [
        "status" ==> status
        "headers" ==> createObj [
            "Content-Type" ==> "application/json"
            "Access-Control-Allow-Origin" ==> "*"
        ]
    ]
    WorkerResponse.create(body, options)

let okJson body = jsonResponse body 200

let jsonResponseWithCookie (body: string) (status: int) (cookie: string) : WorkerResponse =
    let options = createObj [
        "status" ==> status
        "headers" ==> createObj [
            "Content-Type" ==> "application/json"
            "Access-Control-Allow-Origin" ==> "*"
            "Set-Cookie" ==> cookie
        ]
    ]
    WorkerResponse.create(body, options)

let okJsonWithCookie body cookie = jsonResponseWithCookie body 200 cookie
let unauthorized () = jsonResponse """{"error":"Unauthorized"}""" 401
let notFound () = jsonResponse """{"error":"Not found"}""" 404
let badRequest msg =
    let body = Encode.object [ "error", Encode.string msg ] |> Encode.toString 0
    jsonResponse body 400
let serverError msg =
    let body = Encode.object [ "error", Encode.string msg ] |> Encode.toString 0
    jsonResponse body 500

/// GET /admin/logout — clears the browser's stored admin key, then returns to
/// the key prompt. The admin SPA is a static asset and the key lives in
/// localStorage, so the worker can't clear it directly; instead it serves a
/// tiny page that does, then redirects to /admin (which now shows the login
/// field, the key being empty). This is the sign-out for tenants without the
/// browser extension (articles, music) — there's no other way to swap keys.
let logoutResponse () : WorkerResponse =
    let body =
        "<!doctype html><meta charset=\"utf-8\"><title>Signed out</title>"
        + "<script>try{localStorage.removeItem('adminKey')}catch(e){}"
        + "location.replace('/admin')</script>"
        + "<noscript>Signed out. <a href=\"/admin\">Continue</a></noscript>"
    let options = createObj [
        "status" ==> 200
        "headers" ==> createObj [
            "Content-Type" ==> "text/html; charset=utf-8"
            "Cache-Control" ==> "no-store"
        ]
    ]
    WorkerResponse.create(body, options)

/// 302 redirect, attaching Set-Cookie only when there is one to set. Guest bootstrap/renewal/adoption
/// yields a replacement cookie sometimes (fresh/renewed/adopted) and None when the existing signed
/// credential is still good — re-setting it needlessly is avoided.
let redirectResponseOpt (url: string) (cookie: string option) : WorkerResponse =
    let headers = [ "Location" ==> url ]
    let headers = match cookie with Some c -> headers @ [ "Set-Cookie" ==> c ] | None -> headers
    let options = createObj [
        "status" ==> 302
        "headers" ==> createObj headers
    ]
    WorkerResponse.create("", options)

let redirectResponse (url: string) (cookie: string) : WorkerResponse = redirectResponseOpt url (Some cookie)

let corsPreflightResponse () : WorkerResponse =
    let options = createObj [
        "status" ==> 204
        "headers" ==> createObj [
            "Access-Control-Allow-Origin" ==> "*"
            "Access-Control-Allow-Methods" ==> "GET, POST, PUT, DELETE, OPTIONS"
            "Access-Control-Allow-Headers" ==> "Content-Type, X-Admin-Key"
        ]
    ]
    WorkerResponse.create("", options)

let validationErrorResponse (errors: ValidationError list) =
    let body =
        Encode.object [
            "errors", Encode.list (errors |> List.map (fun e ->
                Encode.object [
                    "field", Encode.string e.Field
                    "message", Encode.string e.Message
                ]))
        ] |> Encode.toString 0
    jsonResponse body 422

// ============================================================
// createWorker — framework entry point
// ============================================================

/// Outcome of a completed OAuth round trip.
type OAuthComplete = {
    RedirectUrl: string
    /// Set when the provider account is already known to a different guest.
    /// The browser is re-cookied to that guest, so a second machine joins the
    /// existing identity set rather than starting a parallel one.
    AdoptGuestId: string option
}

type OAuthConfig = {
    Secret: string
    Providers: Map<string, {| ClientId: string; ClientSecret: string |}>
    /// Called by /api/auth/me. App resolves guest → JSON string (or None for anon).
    ResolveIdentity: D1Database -> string -> JS.Promise<string option>
    OnOAuthComplete: D1Database -> R2Bucket -> string -> obj -> string -> JS.Promise<OAuthComplete>
}

/// C4 — which R2 key prefixes are PRIVATE: objects under them are never served through the
/// generic public /blobs/ route (a feature owns a dedicated, isolated route for them, e.g.
/// blog's snapshot archive). The public route decodes the key once, then denies any whole
/// private-prefix match. A content feature supplies its prefix; the consuming app configures
/// the policy. `{ PrivatePrefixes = [] }` = nothing private (the default for apps without such
/// a feature; the framework knows no feature-specific directory name).
type BlobServingPolicy = { PrivatePrefixes: string list }

type WorkerConfig = {
    Routes: WorkerRequest -> obj -> ExecutionContext -> JS.Promise<WorkerResponse> option
    Admin: (WorkerRequest -> obj -> Route -> JS.Promise<WorkerResponse> option) option
    OAuth: (obj -> OAuthConfig) option
    /// Signed-guest-cookie policy, bound per request from env (audience = request host). `None` for
    /// deployments with no guest identity (they acquire no secret requirement). Built LAZILY inside
    /// each guest route so a missing/short GUEST_SECRET fails only guest operations, not content
    /// reads. Independent of OAuth: a host with no providers still issues/verifies signed guests.
    GuestSession: (obj -> WorkerRequest -> Hedge.GuestSession.Deps) option
    /// Extra client views mounted on other hosts of this same deploy (default []).
    Mounts: Mount list
    /// R2 key prefixes never served through the public /blobs/ route (C4). See BlobServingPolicy.
    BlobServing: BlobServingPolicy
    /// Cron handler (C6). `None` ⇒ inert — the `scheduled` export fires nothing without a
    /// `[triggers]` crons block in wrangler.toml. A generic hook: the framework carries no
    /// knowledge of what any deployment schedules.
    Scheduled: (ScheduledController -> obj -> ExecutionContext -> JS.Promise<unit>) option
}

let createWorker (config: WorkerConfig) =
    {| fetch = fun (request: WorkerRequest) (env: obj) (ctx: ExecutionContext) ->
        promise {
            let route = parseRoute request

            // 1. CORS preflight
            match route with
            | OPTIONS _ ->
                return corsPreflightResponse ()
            | _ ->

            // 1b. Admin sign-out: clear the stored key, bounce to /admin.
            match route with
            | GET path when matchPath "/admin/logout" path = Some (Exact "/admin/logout") ->
                return logoutResponse ()
            | _ ->

            // 2. Admin routes
            match config.Admin |> Option.bind (fun f -> f request env route) with
            | Some p -> return! p
            | None ->

            // 3. Auth routes (/api/auth/*)
            let oauthCfg = config.OAuth |> Option.map (fun f -> f env)

            match route with
            | GET path when matchPath "/api/auth/me" path = Some (Exact "/api/auth/me") ->
                match config.GuestSession with
                | None ->
                    // No guest identity configured on this deployment → anonymous, no cookie set.
                    return okJson """{"guest":null}"""
                | Some guestOf ->
                    // Bootstrap: verify the signed cookie, mint a fresh signed guest if none is
                    // acceptable, renew if due. resolveOrBootstrap carries the replacement (if any).
                    let! boot = resolveOrBootstrap (guestOf env request) (readCookie request)
                    let attach body =
                        match boot.Replacement with
                        | Some c -> okJsonWithCookie body c
                        | None -> okJson body
                    if boot.IsNew then
                        // A brand-new guest is not revealed to the client — identity is established
                        // but the id stays in the httpOnly cookie only (unchanged /api/auth/me shape).
                        return attach """{"guest":null}"""
                    else
                        match oauthCfg with
                        | Some oauth ->
                            let db : D1Database = env?DB
                            let! identityJson = oauth.ResolveIdentity db boot.GuestId
                            match identityJson with
                            | Some json ->
                                return attach (sprintf """{"guest":{"guestId":"%s","identity":%s}}""" boot.GuestId json)
                            | None ->
                                return attach """{"guest":null}"""
                        | None ->
                            return attach """{"guest":null}"""
            | _ ->

            // Which providers can actually complete a login: known to the
            // framework AND carrying credentials. Clients render their sign-in
            // options from this, so dropping a provider is deleting a secret
            // rather than a redeploy.
            match route with
            | GET path when matchPath "/api/auth/providers" path = Some (Exact "/api/auth/providers") ->
                let configured =
                    match oauthCfg with
                    | None -> []
                    | Some oauth ->
                        oauth.Providers
                        |> Map.toList
                        |> List.filter (fun (name, creds) ->
                            OAuth.providers.ContainsKey name
                            && not (isNull creds.ClientId)
                            && creds.ClientId <> ""
                            && not (isNull creds.ClientSecret)
                            && creds.ClientSecret <> "")
                        |> List.map fst
                let body =
                    configured
                    |> List.map (sprintf "\"%s\"")
                    |> String.concat ","
                    |> sprintf """{"providers":[%s]}"""
                return okJson body
            | _ ->

            match route, oauthCfg with
            | GET path, Some oauth when matchPath "/api/auth/:id/login" path |> Option.isSome ->
                let providerName = match (matchPath "/api/auth/:id/login" path).Value with WithParam (_, p) -> p | Exact _ -> ""
                match OAuth.providers.TryFind providerName, oauth.Providers.TryFind providerName with
                | Some providerCfg, Some creds ->
                    match config.GuestSession with
                    | None -> return serverError "Guest signing is not configured"
                    | Some guestOf ->
                        // Bootstrap the guest (mint a signed one if none), then bind its id into the
                        // HMAC-signed OAuth state so the callback can require the same subject.
                        let! boot = resolveOrBootstrap (guestOf env request) (readCookie request)
                        let returnTo = getQueryParam request.url "returnTo"
                        let returnTo = if isNull returnTo || returnTo = "" then "/" else returnTo
                        let! state = OAuth.generateState oauth.Secret boot.GuestId returnTo
                        let redirectUri =
                            let url = createUrl request.url
                            let origin : string = url?origin
                            sprintf "%s/api/auth/%s/callback" origin providerName
                        let authUrl = OAuth.generateAuthUrl providerCfg creds.ClientId redirectUri state
                        return redirectResponseOpt authUrl boot.Replacement
                | _ ->
                    return badRequest (sprintf "Unknown provider: %s" providerName)

            | GET path, Some oauth when matchPath "/api/auth/:id/callback" path |> Option.isSome ->
                let providerName = match (matchPath "/api/auth/:id/callback" path).Value with WithParam (_, p) -> p | Exact _ -> ""
                match OAuth.providers.TryFind providerName, oauth.Providers.TryFind providerName with
                | Some providerCfg, Some creds ->
                    match config.GuestSession with
                    | None -> return serverError "Guest signing is not configured"
                    | Some guestOf ->
                        let deps = guestOf env request
                        // The callback must run under an accepted (signed / bridge-upgraded) credential
                        // — never sign a callback's unverified subject. It is not a bootstrap route.
                        let! required = requireGuest deps (readCookie request)
                        let code = getQueryParam request.url "code"
                        let stateParam = getQueryParam request.url "state"
                        if isNull code || code = "" then
                            return badRequest "Missing code parameter"
                        elif isNull stateParam || stateParam = "" then
                            return badRequest "Missing state parameter"
                        else
                            let! stateResult = OAuth.verifyState oauth.Secret stateParam
                            match stateResult with
                            | Error err ->
                                return badRequest (sprintf "Invalid state: %s" err)
                            | Ok (stateGuestId, returnTo) ->
                                match required with
                                | Rejected ->
                                    // In-flight login whose guest cookie expired or fell to a cutover
                                    // between start and callback — the flow must be restarted.
                                    return badRequest "Session expired during login; please retry"
                                | Accepted a ->
                                    if stateGuestId <> a.GuestId then
                                        return badRequest "State mismatch"
                                    else
                                        let redirectUri =
                                            let url = createUrl request.url
                                            let origin : string = url?origin
                                            sprintf "%s/api/auth/%s/callback" origin providerName
                                        let! accessToken = OAuth.exchangeCode providerCfg code redirectUri creds.ClientId creds.ClientSecret
                                        let! userInfo = OAuth.fetchUserinfo providerCfg accessToken
                                        let db : D1Database = env?DB
                                        let blobs : R2Bucket = env?BLOBS
                                        let! completion = oauth.OnOAuthComplete db blobs a.GuestId (box userInfo) returnTo
                                        // Adoption is the privileged re-sign: issue a signed cookie for
                                        // the adopted subject, but only after OnOAuthComplete's verified
                                        // provider-ownership check. Otherwise carry any renewal cookie.
                                        let! cookie =
                                            match completion.AdoptGuestId with
                                            | Some adopted -> promise { let! c = adopt deps adopted in return Some c }
                                            | None -> promise { return a.Replacement }
                                        return redirectResponseOpt completion.RedirectUrl cookie
                | _ ->
                    return badRequest (sprintf "Unknown provider: %s" providerName)

            | _ ->

            // 4. WebSocket upgrade
            match route with
            | GET path when matchPath "/api/events" path = Some (Exact "/api/events")
                          && isWebSocketUpgrade request ->
                let events : DurableObjectNamespace = env?EVENTS
                let itemId = getQueryParam request.url "itemId"
                if isNull itemId || itemId = "" then
                    return badRequest "Missing itemId query parameter"
                else
                    let doId = events.idFromName(itemId)
                    return! events.get(doId).fetch(request)
            | _ ->

            // 5. Blob routes
            match route with
            | POST path when matchPath "/api/blobs" path = Some (Exact "/api/blobs") ->
                // Uploads require the admin key — otherwise anyone could fill the
                // bucket. The rich-text editor attaches it from localStorage.adminKey,
                // which only the owner's browser has (after signing into /admin).
                let adminKey : string = env?ADMIN_KEY
                let provided = getHeader request "X-Admin-Key"
                if provided <> "" && provided = adminKey then
                    let blobs : R2Bucket = env?BLOBS
                    return! handleBlobUpload request blobs
                else
                    return unauthorized ()
            | POST path when matchPath "/api/blobs/guest" path = Some (Exact "/api/blobs/guest") ->
                // Guest comment-image upload — NOT admin-gated. A WRITE: require an ACCEPTED signed
                // (or bridge-upgraded) guest via the shared policy, never create one on this path,
                // and reject before touching storage otherwise. handleGuestBlobUpload enforces
                // raster-only (no SVG) + a size cap, keys the object WITHOUT the guest id, and stores
                // the subject only in private R2 metadata. Any renewal/upgrade cookie is attached.
                match config.GuestSession with
                | None -> return unauthorized ()
                | Some guestOf ->
                    let! required = requireGuest (guestOf env request) (readCookie request)
                    match required with
                    | Rejected -> return unauthorized ()
                    | Accepted a ->
                        let blobs : R2Bucket = env?BLOBS
                        return! handleGuestBlobUpload request blobs a.GuestId a.Replacement
            | GET path when path.StartsWith("/blobs/") ->
                let blobs : R2Bucket = env?BLOBS
                let key = decodeUri (path.Substring(7))
                // C4: objects under a configured PRIVATE prefix (e.g. blog's "archive/" snapshot
                // HTML) must NEVER be served through this generic public route (raw bytes, no
                // isolation) — only via the feature's own dedicated route. Check AFTER decoding,
                // so an encoded key (…/archive%2F…) can't slip past. This route runs before
                // config.Routes, so an app-level guard cannot cover it; the policy does. The
                // framework knows no feature directory name — the app configures the prefixes.
                if config.BlobServing.PrivatePrefixes |> List.exists (fun p -> key.StartsWith(p)) then
                    return notFound ()
                else
                    return! handleBlobServe key blobs
            | _ ->

            // 6. App routes (generated)
            match config.Routes request env ctx with
            | Some p -> return! p
            | None ->

            // 7. SPA fallback — delegate to Cloudflare Assets for non-API GET.
            //    A mounted host is served its own shell; everything else the default.
            match route with
            | GET path ->
                let matches (m: Mount) =
                    m.When env &&
                    match m.On with
                    | OnHost h -> requestHost request = h
                    | OnPath p -> path = p || path.StartsWith(p + "/")
                match config.Mounts |> List.tryFind matches with
                | Some m -> return! fetchShell env request m.Shell
                | None -> return! fetchFromAssets env request
            | _ ->
                return notFound ()
        }
       // Literal 3-arg lambda (like fetch) so Fable emits an uncurried scheduled(c,e,ctx); a
       // point-free value would bind only the controller. Inert unless config.Scheduled is Some AND
       // wrangler.toml declares a [triggers] crons block (C6).
       scheduled = fun (controller: ScheduledController) (env: obj) (ctx: ExecutionContext) ->
        promise {
            match config.Scheduled with
            | Some handler -> return! handler controller env ctx
            | None -> return ()
        }
    |}
