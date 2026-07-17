module Hedge.Router

open Fable.Core
open Fable.Core.JsInterop
open Thoth.Json
open Hedge.Workers
open Hedge.Validate
open Hedge.OAuth

/// Minimal router for Workers.
/// Pattern matches on method + path to dispatch to handlers.

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

/// Guest identity — resolved from httpOnly cookie, generated if absent.
type GuestContext = {
    GuestId: string
    IsNew: bool
}

let private guestCookieName = "hedge_guest"
let private guestCookieMaxAge = 31536000

let resolveGuest (request: WorkerRequest) : GuestContext =
    let cookieHeader = getCookieHeader request
    match parseCookie guestCookieName cookieHeader with
    | Some id -> { GuestId = id; IsNew = false }
    | None -> { GuestId = newId (); IsNew = true }

let guestCookieValue (guest: GuestContext) : string =
    sprintf "%s=%s; Path=/; HttpOnly; SameSite=Lax; Max-Age=%d" guestCookieName guest.GuestId guestCookieMaxAge

[<Emit("$0.ASSETS.fetch($1)")>]
let private fetchFromAssets (env: obj) (request: WorkerRequest) : JS.Promise<WorkerResponse> = jsNative

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

let redirectResponse (url: string) (cookie: string) : WorkerResponse =
    let options = createObj [
        "status" ==> 302
        "headers" ==> createObj [
            "Location" ==> url
            "Set-Cookie" ==> cookie
        ]
    ]
    WorkerResponse.create("", options)

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

type OAuthConfig = {
    Secret: string
    Providers: Map<string, {| ClientId: string; ClientSecret: string |}>
    /// Called by /api/auth/me. App resolves guest → JSON string (or None for anon).
    ResolveIdentity: D1Database -> string -> JS.Promise<string option>
    OnOAuthComplete: D1Database -> R2Bucket -> string -> obj -> string -> JS.Promise<string>
}

type WorkerConfig = {
    Routes: WorkerRequest -> obj -> ExecutionContext -> JS.Promise<WorkerResponse> option
    Admin: (WorkerRequest -> obj -> Route -> JS.Promise<WorkerResponse> option) option
    OAuth: (obj -> OAuthConfig) option
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

            // 2. Admin routes
            match config.Admin |> Option.bind (fun f -> f request env route) with
            | Some p -> return! p
            | None ->

            // 3. Auth routes (/api/auth/*)
            let oauthCfg = config.OAuth |> Option.map (fun f -> f env)

            match route with
            | GET path when matchPath "/api/auth/me" path = Some (Exact "/api/auth/me") ->
                let guest = resolveGuest request
                if guest.IsNew then
                    return okJsonWithCookie """{"guest":null}""" (guestCookieValue guest)
                else
                    match oauthCfg with
                    | Some oauth ->
                        let db : D1Database = env?DB
                        let! identityJson = oauth.ResolveIdentity db guest.GuestId
                        match identityJson with
                        | Some json ->
                            let body = sprintf """{"guest":{"guestId":"%s","identity":%s}}""" guest.GuestId json
                            return okJsonWithCookie body (guestCookieValue guest)
                        | None ->
                            return okJsonWithCookie """{"guest":null}""" (guestCookieValue guest)
                    | None ->
                        return okJsonWithCookie """{"guest":null}""" (guestCookieValue guest)
            | _ ->

            match route, oauthCfg with
            | GET path, Some oauth when matchPath "/api/auth/:id/login" path |> Option.isSome ->
                let providerName = match (matchPath "/api/auth/:id/login" path).Value with WithParam (_, p) -> p | Exact _ -> ""
                match OAuth.providers.TryFind providerName, oauth.Providers.TryFind providerName with
                | Some providerCfg, Some creds ->
                    let guest = resolveGuest request
                    let returnTo = getQueryParam request.url "returnTo"
                    let returnTo = if isNull (box returnTo) || returnTo = "" then "/" else returnTo
                    let! state = OAuth.generateState oauth.Secret guest.GuestId returnTo
                    let redirectUri =
                        let url = createUrl request.url
                        let origin : string = url?origin
                        sprintf "%s/api/auth/%s/callback" origin providerName
                    let authUrl = OAuth.generateAuthUrl providerCfg creds.ClientId redirectUri state
                    return redirectResponse authUrl (guestCookieValue guest)
                | _ ->
                    return badRequest (sprintf "Unknown provider: %s" providerName)

            | GET path, Some oauth when matchPath "/api/auth/:id/callback" path |> Option.isSome ->
                let providerName = match (matchPath "/api/auth/:id/callback" path).Value with WithParam (_, p) -> p | Exact _ -> ""
                match OAuth.providers.TryFind providerName, oauth.Providers.TryFind providerName with
                | Some providerCfg, Some creds ->
                    let guest = resolveGuest request
                    let code = getQueryParam request.url "code"
                    let stateParam = getQueryParam request.url "state"
                    if isNull (box code) || code = "" then
                        return badRequest "Missing code parameter"
                    elif isNull (box stateParam) || stateParam = "" then
                        return badRequest "Missing state parameter"
                    else
                        let! stateResult = OAuth.verifyState oauth.Secret stateParam
                        match stateResult with
                        | Error err ->
                            return badRequest (sprintf "Invalid state: %s" err)
                        | Ok (stateGuestId, returnTo) ->
                            if stateGuestId <> guest.GuestId then
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
                                let! redirectUrl = oauth.OnOAuthComplete db blobs guest.GuestId (box userInfo) returnTo
                                return redirectResponse redirectUrl (guestCookieValue guest)
                | _ ->
                    return badRequest (sprintf "Unknown provider: %s" providerName)

            | _ ->

            // 4. WebSocket upgrade
            match route with
            | GET path when matchPath "/api/events" path = Some (Exact "/api/events")
                          && isWebSocketUpgrade request ->
                let events : DurableObjectNamespace = env?EVENTS
                let itemId = getQueryParam request.url "itemId"
                if isNull (box itemId) || itemId = "" then
                    return badRequest "Missing itemId query parameter"
                else
                    let doId = events.idFromName(itemId)
                    return! events.get(doId).fetch(request)
            | _ ->

            // 5. Blob routes
            match route with
            | POST path when matchPath "/api/blobs" path = Some (Exact "/api/blobs") ->
                let blobs : R2Bucket = env?BLOBS
                return! handleBlobUpload request blobs
            | GET path when path.StartsWith("/blobs/") ->
                let blobs : R2Bucket = env?BLOBS
                return! handleBlobServe (path.Substring(7)) blobs
            | _ ->

            // 6. App routes (generated)
            match config.Routes request env ctx with
            | Some p -> return! p
            | None ->

            // 7. SPA fallback — delegate to Cloudflare Assets for non-API GET
            match route with
            | GET _ ->
                return! fetchFromAssets env request
            | _ ->
                return notFound ()
        }
    |}
