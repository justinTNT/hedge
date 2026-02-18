module Server.Worker

open Fable.Core
open Fable.Core.JsInterop
open Browser.Types
open Server.Env
open Server.Router
open Server.Handlers

/// Cloudflare Worker entry point.
/// Compiles to JS that Workers can execute.

let private handleRequest (request: Request) (env: Env) (ctx: ExecutionContext) : JS.Promise<Response> =
    promise {
        let route = parseRoute request

        match route with
        // CORS preflight
        | OPTIONS _ ->
            return corsPreflightResponse ()

        // Feed
        | GET path when matchPath "/api/feed" path = Some (Exact "/api/feed") ->
            return! getFeed env

        // Get item by ID
        | GET path ->
            match matchPath "/api/item/:id" path with
            | Some (WithParam (_, itemId)) ->
                return! getItem itemId env
            | _ ->
                return notFound ()

        // Submit comment
        | POST path when matchPath "/api/comment" path = Some (Exact "/api/comment") ->
            return! submitComment request env

        // Submit item
        | POST path when matchPath "/api/item" path = Some (Exact "/api/item") ->
            return! submitItem request env

        // Not found
        | _ ->
            return notFound ()
    }

/// Export the fetch handler for Cloudflare Workers
type WorkerExports = {
    fetch: Request -> Env -> ExecutionContext -> JS.Promise<Response>
}

[<ExportDefault>]
let exports : WorkerExports = {
    fetch = handleRequest
}
