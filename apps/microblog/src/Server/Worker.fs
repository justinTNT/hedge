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
    | GET path when matchPath "/api/auth/identities" path = Some (Exact "/api/auth/identities") ->
        Some (Server.Handlers.getIdentities request env)
    | _ -> None

[<ExportDefault>]
let exports = createWorker {
    Routes = fun request env ctx ->
        let e = env :?> Env
        match authRoutes request e with
        | Some p -> Some p
        | None -> Server.Routes.dispatch request e ctx
    Admin = Some (fun request env route ->
        Server.Admin.handleRequest request (env :?> Env) route)
    OAuth = Some (fun env ->
        let e = env :?> Env
        { Secret = e.OAUTH_SECRET
          Providers = Map.ofList [
              "google", {| ClientId = e.GOOGLE_CLIENT_ID; ClientSecret = e.GOOGLE_CLIENT_SECRET |}
              "github", {| ClientId = e.GITHUB_CLIENT_ID; ClientSecret = e.GITHUB_CLIENT_SECRET |}
          ]
          ResolveIdentity = Server.Handlers.resolveIdentity
          OnOAuthComplete = Server.Handlers.onOAuthComplete })
}
