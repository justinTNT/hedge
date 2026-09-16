module Client.Api

open Fable.Core
open Fable.Core.JsInterop
open Thoth.Json

/// Send a message to the background service worker and get the response.
let private sendMessage (msg: obj) : JS.Promise<obj> =
    HedgeExtension.Chrome.sendMessage msg

/// Render the background proxy's error payload into a human message. The proxy passes
/// through the server's JSON error body verbatim: `{error}` for most failures and
/// `{errors:[{field,message}]}` for validation (e.g. a taken slug). Shown raw, either
/// object stringifies to the useless "[object Object]"; this pulls out the real text.
[<Emit("(function(e){ if (e == null) return 'Request failed'; if (typeof e === 'string') return e; if (e.error) return String(e.error); if (Array.isArray(e.errors)) return e.errors.map(function(x){ return (x.field ? x.field + ': ' : '') + x.message; }).join('; '); try { return JSON.stringify(e); } catch (_) { return String(e); } })($0)")>]
let private errorToString (err: obj) : string = jsNative

let fetchJson<'T> (url: string) (decoder: Decoder<'T>) : JS.Promise<Result<'T, string>> =
    promise {
        let! raw = sendMessage (createObj [ "type" ==> "api"; "method" ==> "GET"; "path" ==> url ])
        let ok = raw?ok : bool
        if ok then
            let data = raw?data
            let json = JS.JSON.stringify data
            return Decode.fromString decoder json
        else
            let msg = errorToString (raw?error)
            return Error msg
    }

let postJson<'T> (url: string) (body: string) (decoder: Decoder<'T>) : JS.Promise<Result<'T, string>> =
    promise {
        let parsed = JS.JSON.parse body
        let! raw = sendMessage (createObj [ "type" ==> "api"; "method" ==> "POST"; "path" ==> url; "body" ==> parsed ])
        let ok = raw?ok : bool
        if ok then
            let data = raw?data
            let json = JS.JSON.stringify data
            return Decode.fromString decoder json
        else
            let msg = errorToString (raw?error)
            return Error msg
    }

/// Like postJson but pins the request to an explicit site ({url, key}) instead of letting
/// the background resolve the current active site. A multi-request submission (image upload,
/// item POST, archive POST) passes one pinned site so it can't be split across tenants if
/// the active site changes mid-flight.
let postJsonPinned<'T> (site: obj) (url: string) (body: string) (decoder: Decoder<'T>) : JS.Promise<Result<'T, string>> =
    promise {
        let parsed = JS.JSON.parse body
        let! raw = sendMessage (createObj [ "type" ==> "api"; "method" ==> "POST"; "path" ==> url; "body" ==> parsed; "site" ==> site ])
        let ok = raw?ok : bool
        if ok then
            let data = raw?data
            let json = JS.JSON.stringify data
            return Decode.fromString decoder json
        else
            let msg = errorToString (raw?error)
            return Error msg
    }

[<Emit("encodeURIComponent($0)")>]
let private uriEnc (s: string) : string = jsNative

/// Build a "?k=v&..." query string (values URL-encoded); "" when empty. Matches the browser
/// Client.Api.buildQuery so the generated bare ClientGen functions — which reference it via
/// `open Client.Api` — compile against this extension Client.Api too. (The extension uses the
/// transport-neutral Client record below, not the bare functions, but the file must compile.)
let buildQuery (pairs: (string * string) list) : string =
    match pairs with
    | [] -> ""
    | _ -> "?" + (pairs |> List.map (fun (k, v) -> k + "=" + uriEnc v) |> String.concat "&")

// -- Transport-neutral extension adapter (C2) --

/// The extension Transport: brokers a Hedge.Http.Request through the background service
/// worker (which does the real fetch), pinned to one destination (`site` = {url, key}) so a
/// whole submission — image, item, snapshot — can't split across tenants if the active site
/// changes mid-flight. The background returns {ok,data} for 2xx, {ok:false,status,error} for
/// an HTTP error, or {ok:false,error} (no status) when the fetch itself threw. We map the
/// first two to a completed Response (status interpreted by Http.sendDecode) and the last to
/// a TransportFailure. Multipart image upload stays a distinct captureImage message, not this.
let extensionTransport (site: obj) : Hedge.Http.Transport =
    fun (req: Hedge.Http.Request) ->
        promise {
            let path = req.Path + buildQuery req.Query
            let baseFields = [ "type" ==> "api"; "method" ==> req.Method; "path" ==> path; "site" ==> site ]
            let msg =
                match req.Body with
                | Some body -> createObj (baseFields @ [ "body" ==> JS.JSON.parse body ])
                | None -> createObj baseFields
            try
                let! raw = sendMessage msg
                let ok = raw?ok : bool
                if ok then
                    return Ok ({ Status = 200; Headers = []; Body = JS.JSON.stringify (raw?data) }: Hedge.Http.Response)
                else
                    let statusVal : obj = raw?status
                    if isNullOrUndefined statusVal then
                        // No HTTP status → the background fetch itself failed (network/IPC).
                        return Error (Hedge.Http.TransportFailure (errorToString (raw?error)))
                    else
                        return Ok ({ Status = unbox<int> statusVal; Headers = []; Body = JS.JSON.stringify (raw?error) }: Hedge.Http.Response)
            with ex ->
                return Error (Hedge.Http.TransportFailure ex.Message)
        }
