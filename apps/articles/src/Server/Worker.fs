module Server.Worker

open Fable.Core
open Fable.Core.JsInterop
open Hedge.Workers
open Hedge.Router
open Server.Env

let private authRoutes (request: WorkerRequest) (env: Env) : JS.Promise<WorkerResponse> option =
    let route = parseRoute request
    match route with
    | POST path when matchPath "/api/auth/activate" path = Some (Exact "/api/auth/activate") ->
        Some (Server.Handlers.activateIdentity request env)
    | POST path when matchPath "/api/auth/revert" path = Some (Exact "/api/auth/revert") ->
        Some (Server.Handlers.revertIdentity request env)
    | POST path when matchPath "/api/auth/disconnect" path = Some (Exact "/api/auth/disconnect") ->
        Some (Server.Handlers.disconnectIdentity request env)
    | GET path when matchPath "/api/auth/identities" path = Some (Exact "/api/auth/identities") ->
        Some (Server.Handlers.getIdentities request env)
    | _ -> None

[<ExportDefault>]
let exports = createWorker {
    Routes = fun request env ctx ->
        let e = env :?> Env
        match authRoutes request e with
        | Some p -> Some p
        | None ->
        // C3: ModuleServices.dispatch binds each composed module's handler record from `env`
        // and composes their (Server.Env-free) dispatches — site-selected (justat vs ndct).
        match Server.ModuleServices.dispatch e request ctx with
        | Some p -> Some p
        | None -> Server.Meta.handleRequest request e
    Admin = Some (fun request env route ->
        Hedge.Admin.handleRequest Server.AdminConfig.adminConfig request (env :?> Env) route)
    OAuth = Some (fun env ->
        let e = env :?> Env
        { Secret = e.OAUTH_SECRET
          Providers = Map.ofList [
              "google", {| ClientId = e.GOOGLE_CLIENT_ID; ClientSecret = e.GOOGLE_CLIENT_SECRET |}
              "github", {| ClientId = e.GITHUB_CLIENT_ID; ClientSecret = e.GITHUB_CLIENT_SECRET |}
          ]
          ResolveIdentity = Server.Handlers.resolveIdentity
          OnOAuthComplete = Server.Handlers.onOAuthComplete })
    // Signed guest cookies (independent of OAuth). Bound per request so the audience is the host;
    // the single builder Server.GuestConfig.deps is shared with the identity handlers and module
    // comment services, so there is one policy for this app.
    GuestSession = Some (fun env request -> Server.GuestConfig.deps (env :?> Env) request)
    // No path-mounts: the unified shell (Stage 2) hosts blog at /blog in-document, so
    // GET /blog[/*] falls through to the single-page-application asset fallback (the
    // shell's index.html — see wrangler.toml [assets] not_found_handling), which routes
    // blog in-SPA. /api/blog/* is still dispatched by Server.Routes above; live comments
    // ride the shared /api/events DO. Meta.fs reserves "blog" so it is never mistaken for
    // an article slug. (ndct composes no blog module, so /blog simply 404s→SPA there.)
    Mounts = []
    // C4: the default build composes blog (whose snapshot HTML uses "archive/"), so deny that
    // public prefix — even though capture is disabled here (no such keys written), it's the
    // correct policy. CP-D: this literal must equal Blog.Snapshots.privatePrefix. Unlike microblog
    // (which references it directly), this Worker is site-shared and ndct composes NO blog module,
    // so Blog.Snapshots isn't in scope in the ndct build — hence the literal, not the constant.
    BlobServing = { PrivatePrefixes = [ "archive/" ] }
    // No cron on articles (C6 alerts is idealist-only, on microblog).
    Scheduled = None
}
