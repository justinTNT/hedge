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
