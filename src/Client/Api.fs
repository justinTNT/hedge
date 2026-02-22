module Client.Api

open Fable.Core
open Fable.Core.JsInterop
open Fetch
open Thoth.Json

/// Framework HTTP helpers — typed API functions are in generated/ClientGen.fs.

let fetchJson<'T> (url: string) (decoder: Decoder<'T>) : JS.Promise<Result<'T, string>> =
    promise {
        let! response = fetch url []
        let! text = response.text()
        return Decode.fromString decoder text
    }

let postJson<'T> (url: string) (body: string) (decoder: Decoder<'T>) : JS.Promise<Result<'T, string>> =
    promise {
        let! response = fetch url [
            Method HttpMethod.POST
            requestHeaders [ ContentType "application/json" ]
            Body (BodyInit.Case3 body)
        ]
        let! text = response.text()
        return Decode.fromString decoder text
    }

// -- WebSocket --

[<Emit("'ws://' + window.location.host")>]
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
