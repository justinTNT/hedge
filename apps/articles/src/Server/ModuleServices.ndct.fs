module Server.ModuleServices

// C3 — the ndct (articles-only) variant of ModuleServices. Same module name + `dispatch`
// signature as ModuleServices.fs (justat: articles + blog), selected by HEDGE_SITE in
// Server.fsproj, so the shared Worker is site-agnostic. Blog is not composed on ndct, so no
// blog Services builder here (Blog.Services isn't compiled).

open Fable.Core
open Hedge.Workers
open Hedge.Router
open Hedge.GuestSession
open Content.Server.Author
open Server.Env

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

let private articles (env: Env) (request: WorkerRequest) : Articles.Services.Services =
    { DB = env.DB; Events = env.EVENTS; Author = authorResolver env.DB
      Guest = Hedge.GuestSession.service (fun () -> Server.GuestConfig.deps env request)
      NewId = newId; Now = epochNow }

let dispatch (env: Env) (request: WorkerRequest) (ctx: ExecutionContext) : JS.Promise<WorkerResponse> option =
    Server.Routes.dispatch (Articles.Composition.bind (articles env request)) request ctx

/// The identity lifecycle paths this module owns and dispatches through the composed IdentityHttp
/// RouteContract. The framework-owned /api/auth/{me,providers,email*,login,callback,logout} are NOT here
/// and fall through untouched — identityHttp must never read or decode their bodies.
let private identityMutationPaths = [ "/api/auth/activate"; "/api/auth/revert"; "/api/auth/disconnect" ]
let [<Literal>] private identityBodyCap = 24000

/// Identity lifecycle HTTP dispatch (/api/auth/{identities,disconnect,revert,activate}) via the composed
/// IdentityHttp module's generated RouteContract — the typed replacement for the host's hand-written route
/// arms + body-reading wrappers. Returns None for every other path so the framework auth routes fall
/// through. See the default ModuleServices.fs for the full preflight contract (refactor plan §2.5): auth
/// resolved before any body is consumed, body bounded to 24,000 bytes read once from the raw stream (413 on
/// excess), the bounded bytes reconstructed for the generated dispatch (400 on malformed), the handler
/// re-resolving the guest request-locally and checking ownership before mutating.
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
            | Accepted guest ->
                let! bounded = readBodyCapped request identityBodyCap
                if isNull (box bounded) then
                    // Preserve the accepted guest's renewal/rotation cookie on the 413 (plan §2.5 fix).
                    return withReplacementCookie guest.Replacement (payloadTooLarge ())
                else
                    match IdentityHttp.RouteContract.dispatch handlers (rebuildRequest request bounded) ctx with
                    | Some p ->
                        // ...and on the generated dispatch's 400 (malformed JSON); a no-op on handler
                        // successes, which already carry the cookie.
                        let! resp = p
                        return withReplacementCookie guest.Replacement resp
                    | None -> return notFound ()
        })
    | _ -> None
