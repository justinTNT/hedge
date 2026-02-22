module Hedge.Workers

open System
open Fable.Core
open Fable.Core.JsInterop

/// Cloudflare Workers environment bindings.
/// These types represent the runtime environment provided by Workers.

/// Workers Request — the incoming HTTP request from the runtime.
type WorkerRequest =
    abstract url: string
    abstract method: string
    abstract headers: obj
    abstract text: unit -> JS.Promise<string>
    abstract json: unit -> JS.Promise<obj>
    abstract formData: unit -> JS.Promise<obj>

/// Workers Response — constructed to send back.
type [<Global>] WorkerResponse =
    [<Emit("new Response($0, $1)")>]
    static member create(body: string, options: obj) : WorkerResponse = jsNative
    member _.status : int = jsNative
    member _.ok : bool = jsNative

/// D1 Database binding
type D1Result<'T> = {
    results: 'T array
    success: bool
    meta: obj
}

type D1PreparedStatement =
    abstract bind: [<ParamArray>] args: obj array -> D1PreparedStatement
    abstract first: unit -> JS.Promise<obj option>
    abstract all: unit -> JS.Promise<D1Result<obj>>
    abstract run: unit -> JS.Promise<D1Result<obj>>

type D1Database =
    abstract prepare: sql: string -> D1PreparedStatement
    abstract batch: statements: D1PreparedStatement array -> JS.Promise<D1Result<obj> array>

/// R2 Object Storage
type R2Object =
    abstract key: string
    abstract size: int
    abstract httpEtag: string

type R2ObjectBody =
    abstract key: string
    abstract size: int
    abstract httpEtag: string
    abstract body: obj
    abstract httpMetadata: obj

type R2Bucket =
    abstract put: key: string * value: obj -> JS.Promise<R2Object>
    abstract get: key: string -> JS.Promise<R2ObjectBody option>
    abstract delete: key: string -> JS.Promise<unit>

/// KV Namespace binding
type KVNamespace =
    abstract get: key: string -> JS.Promise<string option>
    abstract put: key: string * value: string -> JS.Promise<unit>
    abstract delete: key: string -> JS.Promise<unit>

/// WebSocket — the server-side handle for a connected client.
type WebSocket =
    abstract send: msg: string -> unit
    abstract close: code: int * reason: string -> unit

/// Durable Object state — provided by the runtime to the DO constructor.
type DurableObjectState =
    abstract id: obj
    abstract acceptWebSocket: ws: WebSocket -> unit
    abstract getWebSockets: unit -> WebSocket array

type DurableObjectId = interface end

type DurableObjectStub =
    abstract fetch: request: WorkerRequest -> JS.Promise<WorkerResponse>

type DurableObjectNamespace =
    abstract idFromName: name: string -> DurableObjectId
    abstract get: id: DurableObjectId -> DurableObjectStub

/// The execution context
type ExecutionContext =
    abstract waitUntil: promise: JS.Promise<obj> -> unit
    abstract passThroughOnException: unit -> unit

/// Bind parameters to a D1 prepared statement (Emit spread required —
/// Fable's ParamArray on abstract members passes array as single arg).
[<Emit("$0.bind(...$1)")>]
let bind (stmt: D1PreparedStatement) (args: obj array) : D1PreparedStatement = jsNative

/// WebSocket helpers
[<Emit("Object.values(new WebSocketPair())")>]
let createWebSocketPair () : WebSocket array = jsNative

[<Emit("new Response(null, { status: 101, webSocket: $0 })")>]
let upgradeResponse (clientWs: WebSocket) : WorkerResponse = jsNative

[<Emit("new Request($0, { method: $1, body: $2, headers: { 'Content-Type': 'application/json' } })")>]
let createRequest (url: string) (method: string) (body: string) : WorkerRequest = jsNative

[<Emit("new URL($0).searchParams.get($1)")>]
let getQueryParam (url: string) (param: string) : string = jsNative

[<Emit("($0.headers.get('Upgrade') === 'websocket')")>]
let isWebSocketUpgrade (request: WorkerRequest) : bool = jsNative

[<Emit("$0 == null")>]
let isNull (o: obj) : bool = jsNative

// -- JS interop helpers --

[<Emit("null")>]
let jsNull : obj = jsNative

[<Emit("crypto.randomUUID()")>]
let newId () : string = jsNative

[<Emit("Math.floor(Date.now() / 1000)")>]
let epochNow () : int = jsNative

[<Emit("$0[$1]")>]
let getProp (o: obj) (key: string) : obj = jsNative

// -- FormData helpers --

[<Emit("$0.get($1)")>]
let formDataGet (fd: obj) (key: string) : obj = jsNative

[<Emit("$0.name")>]
let fileName (file: obj) : string = jsNative

[<Emit("$0.type")>]
let fileType (file: obj) : string = jsNative

[<Emit("new Response($0, $1)")>]
let streamResponse (body: obj) (options: obj) : WorkerResponse = jsNative

// -- Header helpers --

[<Emit("($0.headers.get($1) || '')")>]
let getHeader (request: WorkerRequest) (name: string) : string = jsNative

// -- Cookie helpers --

[<Emit("($0.headers.get('Cookie') || '')")>]
let getCookieHeader (request: WorkerRequest) : string = jsNative

let parseCookie (name: string) (cookieHeader: string) : string option =
    cookieHeader.Split(';')
    |> Array.map (fun s -> s.Trim())
    |> Array.tryFind (fun s -> s.StartsWith(name + "="))
    |> Option.map (fun s -> s.Substring(name.Length + 1))

// -- D1 row parsing helpers --

let optToDb (v: string option) : obj =
    match v with
    | Some s -> box s
    | None -> jsNull

let rowStr (row: obj) (key: string) : string = getProp row key |> unbox
let rowInt (row: obj) (key: string) : int = getProp row key |> unbox

let rowStrOpt (row: obj) (key: string) : string option =
    let v = getProp row key
    if isNull v then None else Some (unbox v)

let rowIntOpt (row: obj) (key: string) : int option =
    let v = getProp row key
    if isNull v then None else Some (unbox v)

let rowBool (row: obj) (key: string) : bool =
    rowInt row key <> 0

let optIntToDb (v: int option) : obj =
    match v with
    | Some n -> box n
    | None -> jsNull

// ============================================================
// Blob handlers (generic R2 operations)
// ============================================================

let private allowedImageTypes = set [ "image/jpeg"; "image/png"; "image/gif"; "image/webp"; "image/svg+xml" ]

let handleBlobUpload (request: WorkerRequest) (blobs: R2Bucket) : JS.Promise<WorkerResponse> =
    promise {
        let! fd = request.formData()
        let file = formDataGet fd "file"
        if isNull file then
            let options = createObj [ "status" ==> 400; "headers" ==> createObj [ "Content-Type" ==> "application/json"; "Access-Control-Allow-Origin" ==> "*" ] ]
            return WorkerResponse.create("""{"error":"Missing file field"}""", options)
        else
            let mime = fileType file
            if not (allowedImageTypes.Contains mime) then
                let options = createObj [ "status" ==> 400; "headers" ==> createObj [ "Content-Type" ==> "application/json"; "Access-Control-Allow-Origin" ==> "*" ] ]
                return WorkerResponse.create("""{"error":"Unsupported image type"}""", options)
            else
                let name = fileName file
                let key = sprintf "%s/%s" (newId ()) name
                let! _ = blobs.put(key, file)
                let body = sprintf """{"url":"/blobs/%s"}""" key
                let options = createObj [ "status" ==> 200; "headers" ==> createObj [ "Content-Type" ==> "application/json"; "Access-Control-Allow-Origin" ==> "*" ] ]
                return WorkerResponse.create(body, options)
    }

let handleBlobServe (key: string) (blobs: R2Bucket) : JS.Promise<WorkerResponse> =
    promise {
        let! objOpt = blobs.get(key)
        match objOpt with
        | None ->
            let options = createObj [ "status" ==> 404; "headers" ==> createObj [ "Content-Type" ==> "application/json"; "Access-Control-Allow-Origin" ==> "*" ] ]
            return WorkerResponse.create("""{"error":"Not found"}""", options)
        | Some obj ->
            let contentType = getProp obj.httpMetadata "contentType"
            let ct = if isNull contentType then box "application/octet-stream" else contentType
            let options = createObj [
                "status" ==> 200
                "headers" ==> createObj [
                    "Content-Type" ==> ct
                    "Cache-Control" ==> "public, max-age=31536000, immutable"
                ]
            ]
            return streamResponse obj.body options
    }

