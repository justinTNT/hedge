module Server.ModuleServices

// C3 — adapts this app's Env into each composed content module's Services and composes their
// generated dispatches into one site dispatch. This is the DEFAULT (justat: articles + blog);
// ndct (articles-only) uses ModuleServices.ndct.fs. Both expose `dispatch` with the same
// signature, so the shared Worker stays site-agnostic (mirrors AttributionPolicy.fs / .ndct.fs).
// It is the one place that knows both the app's Env / Server.Identity and the modules' Services.

open Fable.Core
open Hedge.Workers
open Hedge.Router
open Hedge.GuestSession
open Content.Server.Author
open Server.Env

/// Build the author resolver from the app's Server.Identity: ensure the guest + anonymous
/// identity exist, then return the active (claimed) identity or the anon fallback.
let private authorResolver (db: D1Database) : AuthorResolver =
    { ResolveAuthor = fun (req: AuthorRequest) ->
        promise {
            let! _ =
                db.batch([|
                    Identity.Server.ensureGuestStmt db req.GuestId req.Now
                    Identity.Server.ensureAnonymousStmt db req.FallbackIdentityId req.GuestId req.AuthorName req.Now
                |])
            let! active = Identity.Server.activeFor db req.GuestId
            return
                { IdentityId = active |> Option.map (fun i -> i.Id) |> Option.defaultValue req.FallbackIdentityId
                  Picture = active |> Option.map (fun i -> i.Picture) |> Option.defaultValue "" }
        } }

// The guest-session service each module gets — the shared signed-cookie policy bound from this
// app's Env + Server.Identity (Server.GuestConfig), deferred so a missing GUEST_SECRET fails only
// a comment write, not a feed read. `request` supplies the audience (host).
let private guestService (env: Env) (request: WorkerRequest) : Hedge.GuestSession.Service =
    Hedge.GuestSession.service (fun () -> Server.GuestConfig.deps env request)

let private articles (env: Env) (request: WorkerRequest) : Articles.Services.Services =
    { DB = env.DB; Events = env.EVENTS; Author = authorResolver env.DB
      Guest = guestService env request; NewId = newId; Now = epochNow }

let private blog (env: Env) (request: WorkerRequest) : Blog.Services.Services =
    // Justat keeps snapshot capture DISABLED (CaptureEnabled = false → POST /api/blog/snapshot
    // returns 404, no write; the /archive route is not mounted below).
    { DB = env.DB; Blobs = env.BLOBS; Events = env.EVENTS; AdminKey = env.ADMIN_KEY
      Author = authorResolver env.DB; Guest = guestService env request
      NewId = newId; Now = epochNow; CaptureEnabled = false }

/// Compose every composed content module's dispatch (articles + blog) over records bound from
/// `env` + `request`, in the order the generated site Routes expects (articles then blog).
let dispatch (env: Env) (request: WorkerRequest) (ctx: ExecutionContext) : JS.Promise<WorkerResponse> option =
    Server.Routes.dispatch (Articles.Composition.bind (articles env request)) (Blog.Composition.bind (blog env request)) request ctx

/// The identity lifecycle paths this module owns and dispatches through the composed IdentityHttp
/// RouteContract. The framework-owned /api/auth/{me,providers,email*,login,callback,logout} are NOT here
/// and fall through untouched — identityHttp must never read or decode their bodies.
let private identityMutationPaths = [ "/api/auth/activate"; "/api/auth/revert"; "/api/auth/disconnect" ]
let [<Literal>] private identityBodyCap = 24000

/// Identity lifecycle HTTP dispatch (/api/auth/{identities,disconnect,revert,activate}) via the composed
/// IdentityHttp module's generated RouteContract — the typed replacement for the host's hand-written route
/// arms + body-reading wrappers. Returns None for every other path so the framework auth routes fall
/// through. Preflight before the generated dispatch (refactor plan §2.5): each mutation POST resolves the
/// guest-write authorizer FIRST (a present-but-invalid bearer fails closed, no cookie fallback), so an
/// unauthenticated mutation is denied before any body is consumed; the body is then bounded to 24,000 bytes,
/// read once from the raw stream (413 on excess), and the bounded bytes are reconstructed for the generated
/// dispatch, which JSON-decodes (400 on malformed) and calls the typed handler. The handler re-resolves the
/// guest request-locally and checks ownership before mutating. GET identities carries no body.
let identityHttp (env: Env) (request: WorkerRequest) (ctx: ExecutionContext) : JS.Promise<WorkerResponse> option =
    let deps = Server.Handlers.writeDeps env
    let handlers = IdentityHttp.Composition.bind deps
    let isMutation p = identityMutationPaths |> List.exists (fun pat -> matchPath pat p = Some (Exact pat))
    match parseRoute request with
    | GET path when matchPath "/api/auth/identities" path = Some (Exact "/api/auth/identities") ->
        IdentityHttp.RouteContract.dispatch handlers request ctx
    | POST path when isMutation path ->
        Some (promise {
            let! authz = deps.RequireGuest request
            match authz with
            | Rejected -> return unauthorized ()
            | Accepted _ ->
                let! bounded = readBodyCapped request identityBodyCap
                if isNull (box bounded) then
                    return payloadTooLarge ()
                else
                    match IdentityHttp.RouteContract.dispatch handlers (rebuildRequest request bounded) ctx with
                    | Some p -> return! p
                    | None -> return notFound ()
        })
    | _ -> None
