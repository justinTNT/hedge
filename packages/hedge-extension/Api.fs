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
