module Server.Worker

open Fable.Core
open Fable.Core.JsInterop
open Hedge.Workers
open Hedge.Router
open Server.Env

// Read-only archive: just the generated API routes + the generic admin. No
// OAuth, no guest/comment routes, no Open Graph rewriter (yet).
[<ExportDefault>]
let exports = createWorker {
    Routes = fun request env ctx ->
        Server.Routes.dispatch request (env :?> Env) ctx
    Admin = Some (fun request env route ->
        Server.Admin.handleRequest request (env :?> Env) route)
    OAuth = None
}
