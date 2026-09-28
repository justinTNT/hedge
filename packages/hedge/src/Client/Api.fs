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

/// Absolute API origin for a bundled mobile build (window.API_ORIGIN, e.g. https://wt.fail); "" on web.
/// The direct /api/auth helpers and the native transport resolve against it so the app reaches the API
/// host, not the capacitor:// WebView. Declared here (before the helpers) so they can prefix it.
[<Emit("window.API_ORIGIN || ''")>]
let apiOrigin : string = jsNative

/// What the direct helpers prefix: the API origin on a mobile build, else the deployment base path.
let private reqBase = if apiOrigin <> "" then apiOrigin else basePath

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
        let! response = fetch (reqBase + url) []
        let! text = response.text()
        return Decode.fromString decoder text
    }

let postJsonRaw (url: string) (body: string) : JS.Promise<Result<unit, string>> =
    promise {
        let! response = fetch (reqBase + url) [
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
        let! response = fetch (reqBase + url) []
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
[<Emit("$0.cache = 'no-store'")>]
let private disableCache (options: obj) : unit = jsNative

let private browserTransportWithCache (noStore: bool) : Hedge.Http.Transport =
    fun (req: Hedge.Http.Request) ->
        promise {
            let url = basePath + req.Path + buildQuery req.Query
            let headerList =
                [ if req.Body.IsSome then yield ContentType "application/json"
                  for (k, v) in req.Headers do yield HttpRequestHeaders.Custom (k, box v) ]
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
                let options = requestProps props
                if noStore then disableCache options
                let! response = GlobalFetch.fetch(RequestInfo.Url url, options)
                let! text = response.text()
                return Ok ({ Status = response.Status; Headers = []; Body = text }: Hedge.Http.Response)
            with ex ->
                return Error (Hedge.Http.TransportFailure ex.Message)
        }

/// Public requests retain the browser's ordinary caching behaviour.
let browserTransport = browserTransportWithCache false

/// Private reads/writes bypass the browser cache. Compose with GuestSession.transport when needed.
let uncachedBrowserTransport = browserTransportWithCache true

// -- Transport-neutral native adapter (Capacitor POC) --

/// A Hedge.Http.Transport for the BUNDLED mobile app, whose WebView origin (capacitor://localhost) is
/// not wt.fail. It uses ordinary `fetch` — the app enables Capacitor's CapacitorHttp plugin
/// (capacitor.config.json: plugins.CapacitorHttp.enabled), which patches fetch/XHR to route NATIVELY,
/// so this bypasses WebView CORS without any native symbol. Differences from browserTransport: it
/// resolves paths against the explicit `apiOrigin` (mobile talks to the API absolutely, not via
/// basePath) and attaches the opaque bearer, read PER REQUEST ("" = none) so a fresh login / sign-out
/// is reflected without rebuilding the transport. A completed response (any status) is Ok with its
/// status for Http.sendDecode to interpret; only a request that never completes is a TransportFailure.
let capacitorTransport (apiOrigin: string) (bearer: unit -> string) : Hedge.Http.Transport =
    fun (req: Hedge.Http.Request) ->
        promise {
            let url = apiOrigin + req.Path + buildQuery req.Query
            let token = bearer ()
            let headerList =
                [ if req.Body.IsSome then yield ContentType "application/json"
                  for (k, v) in req.Headers do yield HttpRequestHeaders.Custom (k, box v)
                  if token <> "" then yield HttpRequestHeaders.Custom ("Authorization", box ("Bearer " + token)) ]
            let baseProps = [ Method (methodOf req.Method); requestHeaders headerList ]
            let props =
                match req.Body with
                | Some b -> baseProps @ [ Body (BodyInit.Case3 b) ]
                | None -> baseProps
            try
                let options = requestProps props
                disableCache options
                let! response = GlobalFetch.fetch(RequestInfo.Url url, options)
                let! text = response.text()
                return Ok ({ Status = response.Status; Headers = []; Body = text }: Hedge.Http.Response)
            with ex ->
                return Error (Hedge.Http.TransportFailure ex.Message)
        }

// -- Mobile bearer store + default transport selection (Capacitor POC) --

/// The current opaque mobile bearer ("" = none), read live per request from the ONE store owned by
/// guest-session.js (Keychain/Keystore-backed there, with an in-memory cache). Reading through
/// HedgeGuest.currentBearer keeps a single source of truth — the transport never touches storage itself.
[<Emit("(window.HedgeGuest && window.HedgeGuest.currentBearer && window.HedgeGuest.currentBearer()) || ''")>]
let mobileBearer () : string = jsNative

/// Pick the transport: native (Capacitor, bearer-carrying, absolute origin) when a mobile API origin is
/// configured, else the ordinary browser transport. Pure in its inputs so it's unit-testable.
let selectTransport (origin: string) (bearer: unit -> string) : Hedge.Http.Transport =
    if origin <> "" then capacitorTransport origin bearer else browserTransport

/// The app's default transport, chosen once from window config. Generated clients build on this.
let appTransport : Hedge.Http.Transport = selectTransport apiOrigin mobileBearer

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
