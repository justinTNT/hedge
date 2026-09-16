module Client.Api

open Fable.Core
open Fable.Core.JsInterop

/// Send a message to the background service worker and get the response.
let private sendMessage (msg: obj) : JS.Promise<obj> =
    HedgeExtension.Chrome.sendMessage msg

/// Render the background proxy's error payload into a human message. The proxy passes
/// through the server's JSON error body verbatim: `{error}` for most failures and
/// `{errors:[{field,message}]}` for validation (e.g. a taken slug). Shown raw, either
/// object stringifies to the useless "[object Object]"; this pulls out the real text.
[<Emit("(function(e){ if (e == null) return 'Request failed'; if (typeof e === 'string') return e; if (e.error) return String(e.error); if (Array.isArray(e.errors)) return e.errors.map(function(x){ return (x.field ? x.field + ': ' : '') + x.message; }).join('; '); try { return JSON.stringify(e); } catch (_) { return String(e); } })($0)")>]
let private errorToString (err: obj) : string = jsNative

[<Emit("encodeURIComponent($0)")>]
let private uriEnc (s: string) : string = jsNative

/// Build a "?k=v&..." query string (values URL-encoded); "" when empty. Used by
/// extensionTransport below to fold a Request's raw query pairs onto the path.
let private buildQuery (pairs: (string * string) list) : string =
    match pairs with
    | [] -> ""
    | _ -> "?" + (pairs |> List.map (fun (k, v) -> k + "=" + uriEnc v) |> String.concat "&")

// -- Transport-neutral extension adapter (C2) --

/// A pinned submission destination: the target site's base URL + its write key. CP-D: typed
/// (was a raw obj) so the popup can't hand the transport a malformed site; serialized to the
/// `{ url, key }` shape the background worker expects at the IPC boundary.
type Destination = { Url: string; Key: string }

/// The extension Transport: brokers a Hedge.Http.Request through the background service
/// worker (which does the real fetch), pinned to one Destination so a whole submission — image,
/// item, snapshot — can't split across tenants if the active site changes mid-flight. The
/// background returns {ok,data} for 2xx, {ok:false,status,error} for an HTTP error, or
/// {ok:false,error} (no status) when the fetch itself threw. We map the first two to a completed
/// Response (status interpreted by Http.sendDecode) and the last to a TransportFailure. Multipart
/// image upload stays a distinct captureImage message, not this.
let extensionTransport (dest: Destination) : Hedge.Http.Transport =
    fun (req: Hedge.Http.Request) ->
        promise {
            let path = req.Path + buildQuery req.Query
            let site = createObj [ "url" ==> dest.Url; "key" ==> dest.Key ]
            // CP-D: forward the Request's declared verb (already sent) and headers (the Transport
            // contract). No submission sends custom headers today, so `headers` is future-proofing.
            let headers = createObj [ for (k, v) in req.Headers -> k ==> box v ]
            let baseFields = [ "type" ==> "api"; "method" ==> req.Method; "path" ==> path; "site" ==> site; "headers" ==> headers ]
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
