module Server.ModuleServices

// The one place that knows BOTH this app's Env / identity layer AND the calendar module's Services:
// it adapts the environment into the module's capabilities, composes the site dispatch (with the
// framework's guest-authorized bounded-body preflight in front of the module's POSTs), and dispatches
// the identity lifecycle HTTP through the composed IdentityHttp module. The module itself stays
// ignorant of the app.

open Fable.Core
open Hedge.Workers
open Hedge.Router
open Server.Env

/// The calendar module's viewer resolver: the shared Content.Server.Viewer contract over this app's
/// guest-write authorizer and the identity module's signed-in lookup (a live guest whose ACTIVE
/// identity is a provider account, not the anonymous fallback). No cookie → guest before the signing
/// policy is bound, so a public read works on a deployment that has not configured GUEST_SECRET yet.
let private viewer (env: Env) : Content.Server.Viewer.ViewerResolver =
    Content.Server.Viewer.resolve (Server.GuestConfig.require env) (Identity.Server.signedIn env.DB)

/// The calendar module's Services, adapted from this app's Env.
let calendar (env: Env) : Calendar.Services.Services =
    { DB = env.DB
      Viewer = viewer env
      Facets = Models.Site.calendarFacets
      Derived = Calendar.Services.noDerived
      NewId = newId
      Now = epochNow }

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
        Some (guestBoundedPost (Server.GuestConfig.require env) entryBodyCap (fun r -> Server.Routes.dispatch handlers r ctx) request)
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
        Some (guestBoundedPost deps.RequireGuest identityBodyCap (fun r -> IdentityHttp.RouteContract.dispatch handlers r ctx) request)
    | _ -> None
