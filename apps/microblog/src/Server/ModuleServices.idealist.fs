module Server.ModuleServices

// C6 — the idealist variant: blog + alerts. Same module name and surface (`blog`, `dispatch`,
// `scheduled`) as the default ModuleServices.fs, selected by HEDGE_SITE in Server.fsproj, so
// Worker.fs stays site-agnostic. This is the one place that knows BOTH the alerts module and blog's
// create surface: it implements alerts' PromoteToFeed against Blog.Db.insertItem + Blog.Sql tag
// statements, so alerts itself keeps no compile-time dependency on blog.

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

let blog (env: Env) (request: WorkerRequest) : Blog.Services.Services =
    { DB = env.DB
      Blobs = env.BLOBS
      Events = env.EVENTS
      AdminKey = env.ADMIN_KEY
      Author = authorResolver env.DB
      Guest = Hedge.GuestSession.service (fun () -> Server.GuestConfig.deps env request)
      NewId = newId
      Now = epochNow
      CaptureEnabled = true }

/// alerts → blog bridge. Build blog's item + topic-tag inserts (UNEXECUTED) and return them with the
/// new item id, so the alerts module batches them together with its own alerts_promotions row (one
/// atomic COMMIT). The tag is upserted (INSERT OR IGNORE) then linked by name. Extract/OwnerComment
/// arrive as ready rich-text JSON from the alerts module.
let private promoteToFeed (db: D1Database) (input: Alerts.Services.PromotionInput) : Alerts.Services.FeedInsertion =
    let itemId = newId ()
    let now = epochNow ()
    let create : Blog.Db.ItemCreate =
        { Title = input.Title
          Link = Some input.Link
          Image = input.Image
          Extract = Some input.Extract
          OwnerComment = input.OwnerComment
          ArticleDate = input.ArticleDate
          Slug = None
          ViewCount = 0 }
    let itemStmt = (Blog.Db.insertItem db itemId now create).Stmt
    let tagStmt = bind (db.prepare Blog.Sql.insertTag) [| box (newId ()); box input.Topic; box now |]
    let linkStmt = bind (db.prepare Blog.Sql.linkItemTag) [| box (newId ()); box itemId; box input.Topic |]
    {| Stmts = [| itemStmt; tagStmt; linkStmt |]; ItemId = itemId |}

let private alertsServices (env: Env) : Alerts.Services.Services =
    { DB = env.DB
      NewId = newId
      Now = epochNow
      PromoteToFeed = promoteToFeed env.DB
      // Curator authorization, injected. Takes the request at CALL time, so this constructor stays
      // request-free — the cron builds alertsServices too and must not need a request. Maps the
      // framework's RoleResult onto alerts' own CuratorAuth (alerts names no access-control type).
      AuthorizeCurator = fun request ->
          promise {
              let! r = Server.AccessConfig.authorize env "curator" request
              return
                  match r with
                  | Hedge.AccessControl.Authorized (_, repl) -> Alerts.Services.Allowed repl
                  | Hedge.AccessControl.AuthRequired repl -> Alerts.Services.AuthRequired repl
                  | Hedge.AccessControl.Forbidden (_, repl) -> Alerts.Services.Forbidden repl
          } }

/// Site dispatch = blog + alerts (alerts contributes no routes, so this just falls through to blog).
let dispatch (env: Env) (request: WorkerRequest) (ctx: ExecutionContext) : JS.Promise<WorkerResponse> option =
    Server.Routes.dispatch
        (Blog.Composition.bind (blog env request))
        (Alerts.Composition.bind (alertsServices env))
        request ctx

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

let [<Literal>] private mobileBodyCap = 24000

/// Mobile bearer-session HTTP dispatch (/api/mobile/{bootstrap,me,exchange,signout}) via the composed
/// MobileHttp module's generated RouteContract — the typed replacement for the host's hand-wired route arms.
/// The browser-OAuth /api/mobile/return redirect and the /api/mobile/blobs multipart upload stay hand-wired
/// in Worker.fs. See the default ModuleServices.fs for the full contract: bootstrap/signout (PostEmpty) and
/// me (GET) read no body and dispatch directly; only exchange is bounded to 24,000 bytes (413 on excess) +
/// rebuilt before dispatch (400 on malformed). Exchange authenticates by code + PKCE proof after decode, not
/// a pre-body guest check.
let mobileHttp (env: Env) (request: WorkerRequest) (ctx: ExecutionContext) : JS.Promise<WorkerResponse> option =
    let handlers : MobileHttp.RouteContract.Handlers =
        { bootstrap = fun () _req _ctx -> Server.Handlers.mobileBootstrap env
          me = fun () req _ctx -> Server.Handlers.mobileMe req env
          exchange = fun r req _ctx -> Server.Handlers.mobileExchange r.code r.verifier req env
          signout = fun () req _ctx -> Server.Handlers.mobileSignout req env }
    match parseRoute request with
    | POST path when matchPath "/api/mobile/exchange" path = Some (Exact "/api/mobile/exchange") ->
        Some (promise {
            let! bounded = readBodyCapped request mobileBodyCap
            if isNull (box bounded) then
                return payloadTooLarge ()
            else
                match MobileHttp.RouteContract.dispatch handlers (rebuildRequest request bounded) ctx with
                | Some p -> return! p
                | None -> return notFound ()
        })
    | _ -> MobileHttp.RouteContract.dispatch handlers request ctx

/// The alerts cron — poll enabled feeds, promote approved drafts. Fires on the [env.idealist]
/// [triggers] crons schedule; inert without it.
let scheduled : (ScheduledController -> obj -> ExecutionContext -> JS.Promise<unit>) option =
    Some (fun _controller env ctx -> Alerts.Cron.run (alertsServices (env :?> Env)) ctx)
