module Server.Env

open Fable.Core
open Fable.Core.JsInterop

/// Cloudflare Workers environment bindings.
/// These types represent the runtime environment provided by Workers.

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

/// The environment object passed to the Worker
type Env = {
    DB: D1Database
    // KV: KVNamespace  // Uncomment when needed
    ENVIRONMENT: string
}

/// The execution context
type ExecutionContext =
    abstract waitUntil: promise: JS.Promise<obj> -> unit
    abstract passThroughOnException: unit -> unit
