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
        Hedge.Admin.handleRequest Server.AdminConfig.adminConfig request (env :?> Env) route)
    OAuth = None
    // No guest identity/comments by default; wire Server.GuestConfig.deps (and a GUEST_SECRET
    // binding) when the app adds guest commenting/uploads.
    GuestSession = None
    AllowGuestUploads = false
    Mounts = []
    BlobServing = { PrivatePrefixes = [] }
    // No cron handler by default; set to Some to run a scheduled job (needs a [triggers] block).
    Scheduled = None
}
