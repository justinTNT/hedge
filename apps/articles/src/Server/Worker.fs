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
    // The blog module's path-mount: GET /blog[/*] is served the blog SPA shell
    // (its own client bundle). /api/blog/* is dispatched by Server.Routes above;
    // live comments ride the shared framework /api/events DO (keyed by itemId, so
    // blog and article items never collide). The blog is compiled into every env
    // of this app, but `When` mounts the client only where SITE = "justat" — ndct
    // leaves it dormant (no /blog shell, no menu link, blog_* tables unmigrated).
    Mounts = [
        { On = OnPath "/blog"
          Shell = "/blog.html"
          When = fun env -> (env?SITE |> unbox<string>) = "justat" }
    ]
}
