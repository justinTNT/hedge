module Server.Router

open Fable.Core
open Fable.Core.JsInterop
open Browser.Types

/// Minimal router for Workers.
/// Pattern matches on method + path to dispatch to handlers.

type Route =
    | GET of string
    | POST of string
    | OPTIONS of string

type RouteMatch =
    | Exact of string
    | WithParam of prefix: string * param: string

let parseRoute (request: Request) : Route =
    let url = createNew (jsConstructor<URL>) (request.url)
    let path = url?pathname |> string
    match request.method with
    | "GET" -> GET path
    | "POST" -> POST path
    | "OPTIONS" -> OPTIONS path
    | _ -> GET path  // fallback

let matchPath (pattern: string) (path: string) : RouteMatch option =
    if pattern.EndsWith("/:id") then
        let prefix = pattern.Replace("/:id", "")
        if path.StartsWith(prefix + "/") then
            let param = path.Substring(prefix.Length + 1)
            Some (WithParam (prefix, param))
        else None
    elif pattern = path then
        Some (Exact path)
    else None

/// Response helpers
let jsonResponse (body: string) (status: int) : Response =
    let options = createObj [
        "status" ==> status
        "headers" ==> createObj [
            "Content-Type" ==> "application/json"
            "Access-Control-Allow-Origin" ==> "*"
        ]
    ]
    Response.create(body, options)

let okJson body = jsonResponse body 200
let notFound () = jsonResponse """{"error":"Not found"}""" 404
let badRequest msg = jsonResponse (sprintf """{"error":"%s"}""" msg) 400
let serverError msg = jsonResponse (sprintf """{"error":"%s"}""" msg) 500

let corsPreflightResponse () : Response =
    let options = createObj [
        "status" ==> 204
        "headers" ==> createObj [
            "Access-Control-Allow-Origin" ==> "*"
            "Access-Control-Allow-Methods" ==> "GET, POST, OPTIONS"
            "Access-Control-Allow-Headers" ==> "Content-Type"
        ]
    ]
    Response.create("", options)
