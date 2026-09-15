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
    // Source-page archive: the extension POSTs a captured snapshot; readers view it sandboxed.
    | POST path when matchPath "/api/blog/snapshot" path = Some (Exact "/api/blog/snapshot") ->
        Some (Server.Archive.handleSnapshot request env)
    | GET path when path.StartsWith("/archive/") ->
        Some (Server.Archive.handleArchiveServe (path.Substring(9)) env)
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
        match Server.Routes.dispatch request e ctx with
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
    // darwin.news/rhymes: a second view over the same items, paired by rhyme-* tags.
    Mounts = [ { On = OnPath "/rhymes"; Shell = "/rhyming.html"; When = fun _ -> true } ]
}
