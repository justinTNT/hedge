module Server.Worker

open Fable.Core
open Fable.Core.JsInterop
open Hedge.Workers
open Hedge.Router
open Server.Env
open Server.Handlers
open Server.Admin

/// Cloudflare Worker entry point.
/// Compiles to JS that Workers can execute.

let private handleRequest (request: WorkerRequest) (env: Env) (ctx: ExecutionContext) : JS.Promise<WorkerResponse> =
    promise {
        let route = parseRoute request

        // Try admin routes first
        match Admin.handleRequest request env route with
        | Some p -> return! p
        | None ->

        match route with
        // CORS preflight
        | OPTIONS _ ->
            return corsPreflightResponse ()

        // Feed
        | GET path when matchPath "/api/feed" path = Some (Exact "/api/feed") ->
            return! getFeed env

        // WebSocket events
        | GET path when matchPath "/api/events" path = Some (Exact "/api/events") ->
            if not (isWebSocketUpgrade request) then
                return badRequest "WebSocket upgrade required"
            else
                let itemId = getQueryParam request.url "itemId"
                if isNull (box itemId) || itemId = "" then
                    return badRequest "Missing itemId query parameter"
                else
                    let doId = env.EVENTS.idFromName(itemId)
                    let stub = env.EVENTS.get(doId)
                    return! stub.fetch(request)

        // Tags
        | GET path when matchPath "/api/tags" path = Some (Exact "/api/tags") ->
            return! getTags env

        // Serve blob
        | GET path when path.StartsWith("/blobs/") ->
            let key = path.Substring("/blobs/".Length)
            return! getBlob key env

        // Parameterized GET routes
        | GET path ->
            match matchPath "/api/tags/:id" path with
            | Some (WithParam (_, param)) when param.EndsWith("/items") ->
                let tag = param.Substring(0, param.Length - "/items".Length)
                return! getItemsByTag tag env
            | _ ->
            match matchPath "/api/item/:id" path with
            | Some (WithParam (_, itemId)) ->
                return! getItem itemId env
            | _ ->
                return notFound ()

        // Upload blob
        | POST path when matchPath "/api/blobs" path = Some (Exact "/api/blobs") ->
            return! uploadBlob request env

        // Submit comment
        | POST path when matchPath "/api/comment" path = Some (Exact "/api/comment") ->
            let guest = resolveGuest request
            return! submitComment request guest env ctx

        // Submit item
        | POST path when matchPath "/api/item" path = Some (Exact "/api/item") ->
            return! submitItem request env

        // Not found
        | _ ->
            return notFound ()
    }

/// Export the fetch handler for Cloudflare Workers
type WorkerExports = {
    fetch: WorkerRequest -> Env -> ExecutionContext -> JS.Promise<WorkerResponse>
}

[<ExportDefault>]
let exports : WorkerExports = {
    fetch = handleRequest
}
