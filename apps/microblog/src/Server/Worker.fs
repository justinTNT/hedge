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
    // darwin.news rhyming — a bespoke route over the composed blog module's tables,
    // deliberately hand-written (not a reflected/gen endpoint).
    | GET path when matchPath "/api/rhymes" path = Some (Exact "/api/rhymes") ->
        Some (Server.Handlers.getRhymes env)
    // Source-page archive (C4): capture is now the reflected blog endpoint POST
    // /api/blog/snapshot (dispatched via Server.ModuleServices below); only the raw-HTML
    // reference serve is mounted here, isolated in a CSP sandbox by the blog module over its
    // Services. (Justat mounts no /archive route — capture disabled there.)
    | GET path when path.StartsWith("/archive/") ->
        Some (Blog.Snapshots.serveArchive (path.Substring(9)) (Server.ModuleServices.blog env request))
    // NB: blocking raw /blobs/archive/* is NOT done here — authRoutes runs inside
    // config.Routes, which the framework reaches AFTER its own /blobs/ handler, so a guard
    // here is unreachable. The block lives in the framework blob route (Router.fs), before
    // generic serving; /archive/<id> above is the only sanctioned (sandboxed) path.
    | _ -> None

[<ExportDefault>]
let exports = createWorker {
    Routes = fun request env ctx ->
        let e = env :?> Env
        match authRoutes request e with
        | Some p -> Some p
        | None ->
        // After the API routes, before the framework's SPA fallback: item URLs
        // get the shell with Open Graph tags, everything else falls through.
        // C3/C6: the composed site dispatch is site-selected in Server.ModuleServices (default =
        // blog only; idealist = blog + alerts), so this Worker stays site-agnostic.
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
    // darwin.news/rhymes: a second view over the same items, paired by rhyme-* tags.
    Mounts = [ { On = OnPath "/rhymes"; Shell = "/rhyming.html"; When = fun _ -> true } ]
    // C4: blog snapshot HTML lives under the blog feature's own private prefix — never served
    // through the public /blobs/ route (only via the sandboxed /archive/<id> feature route).
    // CP-D: the prefix is owned by the feature (Blog.Snapshots.privatePrefix), not a literal here.
    BlobServing = { PrivatePrefixes = [ Blog.Snapshots.privatePrefix ] }
    // C6: site-selected in Server.ModuleServices — idealist runs the alerts cron; every other
    // microblog tenant resolves to None (inert). Fires only with an [env.<site>.triggers] crons block.
    Scheduled = Server.ModuleServices.scheduled
}
