module Hedge.Router

open Fable.Core
open Fable.Core.JsInterop
open Thoth.Json
open Hedge.Workers
open Hedge.Validate

/// Minimal router for Workers.
/// Pattern matches on method + path to dispatch to handlers.

type Route =
    | GET of string
    | POST of string
    | OPTIONS of string

type RouteMatch =
    | Exact of string
    | WithParam of prefix: string * param: string

[<Emit("new URL($0)")>]
let private createUrl (url: string) : obj = jsNative

let parseRoute (request: WorkerRequest) : Route =
    let url = createUrl request.url
    let path : string = url?pathname
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
let jsonResponse (body: string) (status: int) : WorkerResponse =
    let options = createObj [
        "status" ==> status
        "headers" ==> createObj [
            "Content-Type" ==> "application/json"
            "Access-Control-Allow-Origin" ==> "*"
        ]
    ]
    WorkerResponse.create(body, options)

let okJson body = jsonResponse body 200
let notFound () = jsonResponse """{"error":"Not found"}""" 404
let badRequest msg = jsonResponse (sprintf """{"error":"%s"}""" msg) 400
let serverError msg = jsonResponse (sprintf """{"error":"%s"}""" msg) 500

let corsPreflightResponse () : WorkerResponse =
    let options = createObj [
        "status" ==> 204
        "headers" ==> createObj [
            "Access-Control-Allow-Origin" ==> "*"
            "Access-Control-Allow-Methods" ==> "GET, POST, OPTIONS"
            "Access-Control-Allow-Headers" ==> "Content-Type"
        ]
    ]
    WorkerResponse.create("", options)

let validationErrorResponse (errors: ValidationError list) =
    let body =
        Encode.object [
            "errors", Encode.list (errors |> List.map (fun e ->
                Encode.object [
                    "field", Encode.string e.Field
                    "message", Encode.string e.Message
                ]))
        ] |> Encode.toString 0
    jsonResponse body 422
