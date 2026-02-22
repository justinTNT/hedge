module Server.Worker

open Fable.Core
open Hedge.Workers
open Hedge.Router
open Server.Env

[<ExportDefault>]
let exports = createWorker {
    Routes = fun request env ctx ->
        Server.Routes.dispatch request (env :?> Env) ctx
    Admin = Some (fun request env route ->
        Server.Admin.handleRequest request (env :?> Env) route)
}
