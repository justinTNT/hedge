module Server.ModuleServices

// The one place that knows BOTH this app's Env / identity layer AND the calendar module's Services:
// it adapts the environment into the module's capabilities, composes the site dispatch (with the
// bounded-body preflight in front of the module's POSTs), and dispatches the identity lifecycle
// HTTP through the composed IdentityHttp module. The module itself stays ignorant of the app.

open Fable.Core
open Hedge.Workers
open Hedge.Router
open Hedge.GuestSession
open Server.Env

/// Resolve the request's viewer for the calendar module: a verified guest cookie whose guest is live
/// and whose ACTIVE identity is a provider account (not the anonymous fallback) is signed in. Reading
/// never mints a guest, and a request with no cookie short-circuits before the signing policy is
/// bound, so a public read works on a deployment that has not configured GUEST_SECRET yet.
let private viewer (env: Env) (request: WorkerRequest) : JS.Promise<Calendar.Services.Viewer> =
    promise {
        match readCookie request with
        | None -> return { IdentityId = None; Replacement = None }
        | Some _ ->
            let! r = Server.GuestConfig.require env request
            match r with
            | Rejected -> return { IdentityId = None; Replacement = None }
            | Accepted guest ->
                let! live = (bind (env.DB.prepare Identity.Sql.guestNotDeleted) [| box guest.GuestId |]).first()
                if isNull (box live) then
                    return { IdentityId = None; Replacement = guest.Replacement }
                else
                    let! active = Identity.Server.activeFor env.DB guest.GuestId
                    match active with
                    | Some i when i.Provider <> "anonymous" ->
                        return { IdentityId = Some i.Id; Replacement = guest.Replacement }
                    | _ -> return { IdentityId = None; Replacement = guest.Replacement }
    }

/// The calendar module's Services, adapted from this app's Env.
let calendar (env: Env) : Calendar.Services.Services =
    { DB = env.DB
      Viewer = viewer env
      NewId = newId
      Now = epochNow }

/// Bounded-body POST preflight: resolve the guest-write authorizer FIRST (an unauthenticated mutation is
/// denied before any body is consumed), bound the body (413 on excess), then hand the reconstructed
/// request to the generated dispatch (400 on malformed JSON). Any renewal cookie the authorizer issued is
/// preserved on every outcome.
let private boundedPost (env: Env) (request: WorkerRequest) (cap: int)
    (dispatch: WorkerRequest -> JS.Promise<WorkerResponse> option) : JS.Promise<WorkerResponse> =
    promise {
        let! authz = Server.GuestConfig.require env request
        match authz with
        | Rejected -> return unauthorized ()
        | Accepted guest ->
            let! bounded = readBodyCapped request cap
            if isNull (box bounded) then
                return withReplacementCookie guest.Replacement (payloadTooLarge ())
            else
                match dispatch (rebuildRequest request bounded) with
                | Some p ->
                    let! resp = p
                    return withReplacementCookie guest.Replacement resp
                | None -> return notFound ()
    }

let private isExact (paths: string list) (path: string) =
    paths |> List.exists (fun pat -> matchPath pat path = Some (Exact pat))

/// The calendar module's own writes are signed-in-only and may carry a rich-text body.
let private entryWritePaths = [ "/api/calendar/entry"; "/api/calendar/entry/delete" ]
let [<Literal>] private entryBodyCap = 64000

/// The site dispatch: the composed calendar module over Services bound from `env`.
let dispatch (env: Env) (request: WorkerRequest) (ctx: ExecutionContext) : JS.Promise<WorkerResponse> option =
    let handlers = Calendar.Composition.bind (calendar env)
    match parseRoute request with
    | POST path when isExact entryWritePaths path ->
        Some (boundedPost env request entryBodyCap (fun r -> Server.Routes.dispatch handlers r ctx))
    | _ -> Server.Routes.dispatch handlers request ctx

/// The identity lifecycle paths this module owns and dispatches through the composed IdentityHttp
/// RouteContract. The framework-owned /api/auth/{me,providers,email*,login,callback,logout} are NOT here
/// and fall through untouched — identityHttp must never read or decode their bodies.
let private identityMutationPaths = [ "/api/auth/activate"; "/api/auth/revert"; "/api/auth/disconnect" ]
let [<Literal>] private identityBodyCap = 24000

/// Identity lifecycle HTTP dispatch (/api/auth/{identities,disconnect,revert,activate}). Returns None for
/// every other path so the framework auth routes fall through.
let identityHttp (env: Env) (request: WorkerRequest) (ctx: ExecutionContext) : JS.Promise<WorkerResponse> option =
    let deps = Server.Handlers.writeDeps env
    let handlers = IdentityHttp.Composition.bind deps
    match parseRoute request with
    | GET path when matchPath "/api/auth/identities" path = Some (Exact "/api/auth/identities") ->
        IdentityHttp.RouteContract.dispatch handlers request ctx
    | POST path when isExact identityMutationPaths path ->
        Some (boundedPost env request identityBodyCap (fun r -> IdentityHttp.RouteContract.dispatch handlers r ctx))
    | _ -> None
