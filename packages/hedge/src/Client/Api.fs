module Client.Api

// The framework's shared client HTTP helpers. One source, `<Compile Include>`'d by
// every app's Client project (and relied on by content modules, which reference
// `Client.Api`) — do not copy this per app. Typed API functions are in the app's
// generated/ClientGen.fs, which opens Client.Api. Requests are basePath-prefixed so
// the app works mounted under a sub-path; basePath is "" for root deployments.

open Fable.Core
open Fable.Core.JsInterop
open Fetch
open Thoth.Json

/// Framework HTTP helpers — typed API functions are in generated/ClientGen.fs.

/// Sub-path this deployment is served under, e.g. "/st". Empty when at the
/// root. Every request is prefixed so the app works mounted anywhere.
[<Emit("window.BASE_PATH || ''")>]
let basePath : string = jsNative

[<Emit("encodeURIComponent($0)")>]
let private uriEnc (s: string) : string = jsNative

/// Build a "?k=v&..." query string from key/value pairs (values URL-encoded); an
/// empty list yields "". Used by generated GetQuery/GetByQuery client functions.
let buildQuery (pairs: (string * string) list) : string =
    match pairs with
    | [] -> ""
    | _ -> "?" + (pairs |> List.map (fun (k, v) -> k + "=" + uriEnc v) |> String.concat "&")

let fetchJson<'T> (url: string) (decoder: Decoder<'T>) : JS.Promise<Result<'T, string>> =
    promise {
        let! response = fetch (basePath + url) []
        let! text = response.text()
        return Decode.fromString decoder text
    }

let postJson<'T> (url: string) (body: string) (decoder: Decoder<'T>) : JS.Promise<Result<'T, string>> =
    promise {
        let! response = fetch (basePath + url) [
            Method HttpMethod.POST
            requestHeaders [ ContentType "application/json" ]
            Body (BodyInit.Case3 body)
        ]
        let! text = response.text()
        return Decode.fromString decoder text
    }

let postJsonRaw (url: string) (body: string) : JS.Promise<Result<unit, string>> =
    promise {
        let! response = fetch (basePath + url) [
            Method HttpMethod.POST
            requestHeaders [ ContentType "application/json" ]
            Body (BodyInit.Case3 body)
        ]
        if response.Ok then return Ok ()
        else
            let! text = response.text()
            return Error text
    }

let fetchJsonRaw (url: string) : JS.Promise<obj> =
    promise {
        let! response = fetch (basePath + url) []
        let! text = response.text()
        return JS.JSON.parse text
    }

// -- Transport-neutral browser adapter (C2) --

/// The browser Transport: runs a Hedge.Http.Request through fetch, applying the deployment
/// base once and keeping the default same-origin cookie behaviour (identity/guest cookies
/// ride along exactly as the helpers above). A completed HTTP response — whatever its status
/// — comes back as Ok; only a request that never completes (network/CORS) becomes a
/// TransportFailure, leaving status interpretation and decoding to the generated client via
/// Http.sendDecode. Hedge.Http is fully qualified so `Response` never collides with Fetch's.
/// (Custom per-request headers aren't forwarded: no browser endpoint emits any — the
/// extension adapter carries its own credentials by a different path.)
let browserTransport : Hedge.Http.Transport =
    fun (req: Hedge.Http.Request) ->
        promise {
            let url = basePath + req.Path + buildQuery req.Query
            let verb = if req.Method = "POST" then HttpMethod.POST else HttpMethod.GET
            let props =
                match req.Body with
                | Some body ->
                    [ Method verb
                      requestHeaders [ ContentType "application/json" ]
                      Body (BodyInit.Case3 body) ]
                | None ->
                    [ Method verb ]
            try
                let! response = fetch url props
                let! text = response.text()
                return Ok ({ Status = response.Status; Headers = []; Body = text }: Hedge.Http.Response)
            with ex ->
                return Error (Hedge.Http.TransportFailure ex.Message)
        }

// -- WebSocket --

[<Emit("(window.location.protocol === 'https:' ? 'wss://' : 'ws://') + window.location.host + (window.BASE_PATH || '')")>]
let wsBase () : string = jsNative

[<Emit("""
  (function() {
    var ws = new WebSocket($0);
    ws.onmessage = $1;
    ws.onerror = $2;
    return function() { ws.close(); };
  })()
""")>]
let openWebSocket (url: string) (onMessage: obj -> unit) (onError: obj -> unit) : (unit -> unit) = jsNative
