module Server.Worker
open Fable.Core
open Fable.Core.JsInterop
open Hedge.Workers
open Hedge.Router
open Server.Env

[<ExportDefault>]
let exports = createWorker {
    Routes = fun request env ctx ->
        let result =
            match Server.Contributions.dispatch request (env :?> Env) with
            | Some action -> Some action
            | None -> Server.Routes.dispatch request (env :?> Env) ctx
        match result with
        | Some result -> Some (promise {
            try
                let! response = result
                if not (response?headers?has("Cache-Control")) then
                    response?headers?set("Cache-Control", "no-store") |> ignore
                return response
            with _ -> return jsonResponse "{\"error\":\"The catalogue is temporarily unavailable. Please try again.\"}" 503
          })
        | None when request.url.Contains("/api/") -> Some (promise { return notFound () })
        | None -> None
    Admin = Some (fun request env route ->
        let env = env :?> Env
        // The shared admin enforces authorization for discovery and every CRUD operation.
        Hedge.Admin.handleRequest Server.AdminConfig.adminConfig request env route
        |> Option.map (fun action -> promise {
            let! response = action
            if response.status >= 200 && response.status < 300 && request.method <> "GET" then
                Server.CatalogueStore.invalidate env
            return response
        }))
    OAuth = Some (fun env -> Server.AuthConfig.oauth (env :?> Env))
    GuestSession = Some (fun env request -> Server.AuthConfig.deps (env :?> Env) request)
    AllowGuestUploads = false
    Mounts = []
    BlobServing = { PrivatePrefixes = ["private/"] }
    Scheduled = None
}
