module Client.Api

open Fable.Core
open Fable.Core.JsInterop
open Fetch
open Thoth.Json

/// Framework HTTP helpers — typed API functions are in generated/ClientGen.fs.

/// Sub-path this deployment is served under, e.g. "/st". Empty when at the
/// root. Every request is prefixed so the app works mounted anywhere.
[<Emit("window.BASE_PATH || ''")>]
let basePath : string = jsNative

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
