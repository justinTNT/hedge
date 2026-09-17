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

[<Emit("$0.size")>]
let fileSize (file: obj) : float = jsNative

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

// AVIF is included because negotiating image CDNs (e.g. content.api.news) honor a
// browser's `Accept: image/avif,...` and return AVIF to the extension's fetch — without
// it, tier-1 capture of those images is rejected here and only the server (no Accept
// preference, gets JPEG) can rehost them. Every current browser renders AVIF.
let allowedImageTypes = set [ "image/jpeg"; "image/png"; "image/gif"; "image/webp"; "image/avif"; "image/svg+xml" ]

/// Types allowed for UN-privileged (guest) uploads. Deliberately EXCLUDES image/svg+xml:
/// an SVG served from /blobs/ on our own origin can carry embedded script (stored XSS), so
/// only trusted admin uploads may store SVG. Raster formats only.
let guestImageTypes = set [ "image/jpeg"; "image/png"; "image/gif"; "image/webp"; "image/avif" ]

/// put with the content type recorded, so handleBlobServe can serve it back with
/// the right Content-Type (an <img> won't render an application/octet-stream).
[<Emit("$0.put($1, $2, { httpMetadata: { contentType: $3 } })")>]
let private r2PutTyped (blobs: R2Bucket) (key: string) (body: obj) (contentType: string) : JS.Promise<obj> = jsNative

/// Store a string (e.g. captured/cleaned HTML) in R2 with a content type — the public
/// counterpart to the internal typed put used by the blob-upload / rehost paths.
[<Emit("$0.put($1, $2, { httpMetadata: { contentType: $3 } })")>]
let r2PutText (blobs: R2Bucket) (key: string) (text: string) (contentType: string) : JS.Promise<obj> = jsNative

/// Filenames become part of a URL path, so strip anything that would need
/// percent-encoding (spaces especially) — keeps the stored key and the served
/// URL identical, with no decode round-trip to get wrong.
[<Emit("$0.replace(/[^A-Za-z0-9._-]/g, '-')")>]
let private safeName (s: string) : string = jsNative

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
                let name = safeName (fileName file)
                let key = sprintf "%s/%s" (newId ()) name
                let! _ = r2PutTyped blobs key file mime
                let body = sprintf """{"url":"/blobs/%s"}""" key
                let options = createObj [ "status" ==> 200; "headers" ==> createObj [ "Content-Type" ==> "application/json"; "Access-Control-Allow-Origin" ==> "*" ] ]
                return WorkerResponse.create(body, options)
    }

/// Cap for un-privileged (guest) uploads — bounds R2 storage abuse from the public
/// comment-image path. Admin uploads are uncapped (trusted).
let guestUploadMaxBytes = 5.0 * 1024.0 * 1024.0

/// Guest image upload for comments: no admin key (the route gates on the guest session),
/// raster-only (no SVG — see guestImageTypes) and size-capped. Keyed under
/// comment/<guestId>/… so uploads are attributable for abuse cleanup.
let handleGuestBlobUpload (request: WorkerRequest) (blobs: R2Bucket) (guestId: string) : JS.Promise<WorkerResponse> =
    let errJson (msg: string) (status: int) =
        let options = createObj [ "status" ==> status; "headers" ==> createObj [ "Content-Type" ==> "application/json"; "Access-Control-Allow-Origin" ==> "*" ] ]
        WorkerResponse.create(sprintf """{"error":"%s"}""" msg, options)
    promise {
        let! fd = request.formData()
        let file = formDataGet fd "file"
        if isNull file then
            return errJson "Missing file field" 400
        else
            let mime = fileType file
            if not (guestImageTypes.Contains mime) then
                return errJson "Unsupported image type" 400
            elif fileSize file > guestUploadMaxBytes then
                return errJson "Image too large (max 5 MB)" 413
            else
                let name = safeName (fileName file)
                let key = sprintf "comment/%s/%s/%s" (safeName guestId) (newId ()) name
                let! _ = r2PutTyped blobs key file mime
                let body = sprintf """{"url":"/blobs/%s"}""" key
                let options = createObj [ "status" ==> 200; "headers" ==> createObj [ "Content-Type" ==> "application/json"; "Access-Control-Allow-Origin" ==> "*" ] ]
                return WorkerResponse.create(body, options)
    }

/// Fallback content type from the key's extension, for objects stored without
/// httpMetadata (e.g. uploads from before the type was recorded).
[<Emit("(function(k){var e=(k.split('.').pop()||'').toLowerCase();return ({png:'image/png',jpg:'image/jpeg',jpeg:'image/jpeg',gif:'image/gif',webp:'image/webp',avif:'image/avif',svg:'image/svg+xml'})[e]||'application/octet-stream';})($0)")>]
let private contentTypeFromKey (key: string) : string = jsNative

let handleBlobServe (key: string) (blobs: R2Bucket) : JS.Promise<WorkerResponse> =
    promise {
        let! objOpt = blobs.get(key)
        match objOpt with
        | None ->
            let options = createObj [ "status" ==> 404; "headers" ==> createObj [ "Content-Type" ==> "application/json"; "Access-Control-Allow-Origin" ==> "*" ] ]
            return WorkerResponse.create("""{"error":"Not found"}""", options)
        | Some obj ->
            let contentType = getProp obj.httpMetadata "contentType"
            let ct = if isNull contentType then box (contentTypeFromKey key) else contentType
            let options = createObj [
                "status" ==> 200
                "headers" ==> createObj [
                    "Content-Type" ==> ct
                    "Cache-Control" ==> "public, max-age=31536000, immutable"
                ]
            ]
            return streamResponse obj.body options
    }

// ============================================================
// HMAC-SHA256 helpers (SubtleCrypto)
// ============================================================

[<Emit("new TextEncoder().encode($0)")>]
let private textEncode (s: string) : obj = jsNative

[<Emit("crypto.subtle.importKey('raw', $0, { name: 'HMAC', hash: 'SHA-256' }, false, ['sign', 'verify'])")>]
let private importHmacKey (keyData: obj) : JS.Promise<obj> = jsNative

[<Emit("crypto.subtle.sign('HMAC', $0, $1)")>]
let private hmacSign (key: obj) (data: obj) : JS.Promise<obj> = jsNative

[<Emit("Array.from(new Uint8Array($0)).map(b => b.toString(16).padStart(2, '0')).join('')")>]
let private bufferToHex (buffer: obj) : string = jsNative

[<Emit("fetch($0, $1)")>]
let fetchRaw (url: string) (options: obj) : JS.Promise<WorkerResponse> = jsNative

[<Emit("$0.text()")>]
let responseText (response: WorkerResponse) : JS.Promise<string> = jsNative

[<Emit("$0.json()")>]
let responseJson (response: WorkerResponse) : JS.Promise<obj> = jsNative

[<Emit("$0.arrayBuffer()")>]
let private responseArrayBuffer (response: WorkerResponse) : JS.Promise<obj> = jsNative

[<Emit("$0.headers.get($1)")>]
let private responseHeader (response: WorkerResponse) (name: string) : string = jsNative

/// Sign a message with HMAC-SHA256, returning a hex string.
let hmacSha256 (secret: string) (message: string) : JS.Promise<string> =
    promise {
        let keyData = textEncode secret
        let! key = importHmacKey keyData
        let! signature = hmacSign key (textEncode message)
        return bufferToHex signature
    }

// Bytes from a lowercase-hex string; a malformed (odd-length / non-hex) input yields empty
// bytes, so verification fails rather than throwing (bounded parsing).
[<Emit("(/^[0-9a-f]*$/.test($0) && $0.length % 2 === 0) ? Uint8Array.from($0.match(/../g) || [], h => parseInt(h, 16)) : new Uint8Array(0)")>]
let private hexToBytes (hex: string) : obj = jsNative

[<Emit("crypto.subtle.verify('HMAC', $0, $1, $2)")>]
let private hmacVerifyRaw (key: obj) (signature: obj) (data: obj) : JS.Promise<bool> = jsNative

/// Constant-time verify of a hex HMAC-SHA256 over `message` with `secret` (WebCrypto).
/// A malformed hex mac verifies as false rather than throwing.
let hmacVerify (secret: string) (message: string) (hexMac: string) : JS.Promise<bool> =
    promise {
        let! key = importHmacKey (textEncode secret)
        return! hmacVerifyRaw key (hexToBytes hexMac) (textEncode message)
    }

/// Fetch a remote image and copy it into R2, returning a local "/blobs/<key>" path (or the
/// original url on any failure). Content-addressed by source URL (`keyPrefix/<hash>`), so
/// re-hosting the same URL is idempotent and dedup'd — which keeps handleBlobServe's immutable
/// cache header honest. Best-effort: a bad/non-image/unreachable URL returns the url unchanged,
/// so a flaky third-party host never breaks the caller. `allowed` gates the content type.
/// The shared primitive behind avatar caching and item-image rehosting.
let rehostRemoteImage (blobs: R2Bucket) (keyPrefix: string) (allowed: Set<string>) (url: string) : JS.Promise<string> =
    promise {
        if isNull url || url = "" || not (url.StartsWith "https://") then return url
        else
            try
                // HMAC as a content-addressing hash (not for secrecy); the fixed salt keeps
                // existing avatar keys stable across this refactor.
                let! digest = hmacSha256 "hedge-avatar" url
                let key = sprintf "%s/%s" keyPrefix (digest.Substring(0, 32))
                let! existing = blobs.get key
                match existing with
                | Some _ -> return sprintf "/blobs/%s" key
                | None ->
                    let! response = fetchRaw url (createObj [])
                    if not response.ok then return url
                    else
                        let raw = responseHeader response "content-type"
                        let contentType =
                            if isNull raw then ""
                            else raw.Split(';').[0].Trim().ToLowerInvariant()
                        if not (allowed.Contains contentType) then return url
                        else
                            let! body = responseArrayBuffer response
                            let! _ = r2PutTyped blobs key body contentType
                            return sprintf "/blobs/%s" key
            with ex ->
                JS.console.error ("rehost image failed: " + ex.Message)
                return url
    }

/// Base64url encode a string.
[<Emit("btoa($0).replace(/\\+/g, '-').replace(/\\//g, '_').replace(/=+$/, '')")>]
let base64urlEncode (s: string) : string = jsNative

/// Base64url decode to a string.
[<Emit("atob($0.replace(/-/g, '+').replace(/_/g, '/'))")>]
let base64urlDecode (s: string) : string = jsNative

