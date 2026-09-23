module Client.Api

// The framework's shared client runtime. One source, `<Compile Include>`'d by every app's
// Client project — do not copy this per app. Since C5 the generated clients are transport-
// neutral (createClient over a Transport) and no longer `open Client.Api`; this module now
// provides the browser Transport (browserTransport) plus the few direct helpers still used:
// the identity /api/auth calls (postJsonRaw/fetchJsonRaw) and a couple of direct external
// fetches (fetchJson — basewatch news, microblog rhymes). Requests are basePath-prefixed so
// the app works mounted under a sub-path; basePath is "" for root deployments.

open Fable.Core
open Fable.Core.JsInterop
open Fetch
open Thoth.Json

/// Sub-path this deployment is served under, e.g. "/st". Empty when at the
/// root. Every request is prefixed so the app works mounted anywhere.
[<Emit("window.BASE_PATH || ''")>]
let basePath : string = jsNative

[<Emit("encodeURIComponent($0)")>]
let private uriEnc (s: string) : string = jsNative

/// Build a "?k=v&..." query string from key/value pairs (values URL-encoded); an empty list
/// yields "". Used by browserTransport to fold a Request's raw query pairs onto the URL.
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

/// Map the Request's HTTP verb string onto Fetch's HttpMethod. CP-D: the adapter honours the
/// verb the Request declares (the generated client emits GET/POST today, but PUT/PATCH/DELETE
/// now pass through too) instead of collapsing everything non-POST to GET.
let private methodOf (m: string) : HttpMethod =
    match m.ToUpperInvariant() with
    | "POST" -> HttpMethod.POST
    | "PUT" -> HttpMethod.PUT
    | "PATCH" -> HttpMethod.PATCH
    | "DELETE" -> HttpMethod.DELETE
    | "HEAD" -> HttpMethod.HEAD
    | "OPTIONS" -> HttpMethod.OPTIONS
    | _ -> HttpMethod.GET

/// The browser Transport: runs a Hedge.Http.Request through fetch, applying the deployment
/// base once and keeping the default same-origin cookie behaviour (identity/guest cookies
/// ride along exactly as the helpers above). A completed HTTP response — whatever its status
/// — comes back as Ok; only a request that never completes (network/CORS) becomes a
/// TransportFailure, leaving status interpretation and decoding to the generated client via
/// Http.sendDecode. Hedge.Http is fully qualified so `Response` never collides with Fetch's.
/// CP-D: the Request's declared verb and headers are honoured (the contract), not dropped; a
/// JSON body still adds Content-Type. No browser endpoint emits custom headers today, so header
/// forwarding is future-proofing rather than a behaviour change.
let browserTransport : Hedge.Http.Transport =
    fun (req: Hedge.Http.Request) ->
        promise {
            let url = basePath + req.Path + buildQuery req.Query
            let headerList =
                [ if req.Body.IsSome then ContentType "application/json"
                  for (k, v) in req.Headers -> HttpRequestHeaders.Custom (k, box v) ]
            let baseProps = [ Method (methodOf req.Method); requestHeaders headerList ]
            let props =
                match req.Body with
                | Some body -> baseProps @ [ Body (BodyInit.Case3 body) ]
                | None -> baseProps
            try
                // GlobalFetch (not Fetch.fetch, which FAILWITHS on any non-2xx) so a 4xx/5xx comes
                // back as a normal Response with its status — Http.sendDecode then interprets it as a
                // typed ApiError. Only a request that never completes (network/CORS) is a
                // TransportFailure. Fetch.fetch's throw-on-!ok would otherwise turn every 4xx into a
                // TransportFailure, hiding real statuses from the generated client.
                let! response = GlobalFetch.fetch(RequestInfo.Url url, requestProps props)
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
