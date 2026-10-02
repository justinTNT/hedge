module Server.ModuleServices

// C3 — the single place that adapts this app's environment into each content module's Services,
// and (C6) composes the site dispatch + cron. It is the only server file that knows BOTH the app's
// Env / Server.Identity AND a module's Services type, so the module itself stays ignorant of the
// app. This is the DEFAULT (blog-only) variant — every microblog tenant except idealist;
// ModuleServices.idealist.fs is the blog+alerts superset. Both expose `blog`/`dispatch`/`scheduled`
// so Worker.fs is site-agnostic (selected by HEDGE_SITE in Server.fsproj).

open Fable.Core
open Hedge.Workers
open Hedge.Router
open Hedge.GuestSession
open Content.Server.Author
open Server.Env

/// Build the author resolver from the app's Server.Identity: ensure the guest + anonymous
/// identity exist (one batch), then return the active (claimed) identity or the anon fallback —
/// exactly the flow the module used to run inline against Identity.Server.
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

/// The blog module's Services, adapted from this app's Env + request (the request supplies the
/// guest-session audience; the guest service is deferred so it costs nothing on a read).
let blog (env: Env) (request: WorkerRequest) : Blog.Services.Services =
    { DB = env.DB
      Blobs = env.BLOBS
      Events = env.EVENTS
      AdminKey = env.ADMIN_KEY
      Author = authorResolver env.DB
      // Bearer-first (Capacitor POC), else the signed cookie. A web request carries no bearer, so this
      // is identical to the cookie-only service; a native client's Authorization: Bearer resolves here.
      Guest = Hedge.MobileSession.service (Server.GuestConfig.mobileDeps env) (fun () -> Server.GuestConfig.deps env request)
      NewId = newId
      Now = epochNow
      CaptureEnabled = true }

/// The site dispatch (blog only) — moved here from Worker.fs so the Worker stays site-agnostic.
let dispatch (env: Env) (request: WorkerRequest) (ctx: ExecutionContext) : JS.Promise<WorkerResponse> option =
    Server.Routes.dispatch (Blog.Composition.bind (blog env request)) request ctx

/// The identity lifecycle paths this module owns and dispatches through the composed IdentityHttp
/// RouteContract. The framework-owned /api/auth/{me,providers,email*,login,callback,logout} are NOT here
/// and fall through untouched — identityHttp must never read or decode their bodies.
let private identityMutationPaths = [ "/api/auth/activate"; "/api/auth/revert"; "/api/auth/disconnect" ]
let [<Literal>] private identityBodyCap = 24000

/// Identity lifecycle HTTP dispatch (/api/auth/{identities,disconnect,revert,activate}) via the composed
/// IdentityHttp module's generated RouteContract — the typed replacement for the host's hand-written route
/// arms + body-reading wrappers. Returns None for every other path so the framework auth routes fall
/// through.
///
/// Preflight before the generated dispatch (refactor plan §2.5): each mutation POST resolves the
/// guest-write authorizer FIRST — a present-but-invalid bearer fails closed with no cookie fallback — so an
/// unauthenticated mutation is denied before any body is consumed. The body is then bounded to 24,000 bytes,
/// read once from the raw stream (so a chunked body with no Content-Length is still capped: 413 on excess),
/// and the bounded bytes are reconstructed into the request handed to the generated dispatch, which
/// JSON-decodes (400 on malformed) and calls the typed handler. The handler re-resolves the guest
/// request-locally (the authoritative check) and performs ownership checks before mutating. GET identities
/// carries no body and dispatches directly.
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
                    | None -> return notFound ()  // unreachable: isMutation already matched the path
        })
    | _ -> None

/// No cron on the default microblog tenants (idealist runs the alerts cron — see the .idealist variant).
let scheduled : (ScheduledController -> obj -> ExecutionContext -> JS.Promise<unit>) option = None
