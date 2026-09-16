module Hedge.Http

// C2 — a platform-independent HTTP runtime and the shared client error contract.
//
// Today the generated module clients `open Client.Api` and call bare `fetchJson`/`postJson`;
// which `Client.Api` is in the compilation unit (the app's fetch-based one, or the
// extension's Chrome-IPC one, both `module Client.Api`) is the de-facto transport swap. This
// module replaces that name-collision trick with an explicit `Transport` value passed into a
// generated `Client` record. Nothing here touches window globals, the DOM, Chrome APIs or an
// app's Models assembly — a platform adapter supplies the Transport; the generated client
// owns path/query/codec construction; this runtime owns status interpretation + error
// decoding, so every consumer decodes wire failures the same way.

open Fable.Core
open Thoth.Json

/// A wire request in transport-neutral form. `Path` is relative (no deployment base — the
/// browser adapter applies that once); `Query` pairs are raw strings (the adapter/URL
/// builder percent-encodes them); `Body` is the already-serialized request text (JSON for
/// our endpoints — binary/multipart uploads are a separate transport concern, not this).
type Request =
    { Method: string
      Path: string
      Query: (string * string) list
      Headers: (string * string) list
      Body: string option }

/// A completed HTTP response, still in text form — decoding is the generated client's job.
type Response =
    { Status: int
      Headers: (string * string) list
      Body: string }

/// The distinguishable failure modes of an endpoint call. Kept as separate cases so a UI can
/// tell a network/IPC failure from a server rejection, a field-validation rejection, and a
/// success body that failed to decode — the C2 exit check requires these four to stay
/// distinguishable rather than collapsing into one opaque string.
type ApiError =
    /// The request never completed — network down, CORS, or an IPC broker failure. No HTTP
    /// status exists.
    | TransportFailure of string
    /// A non-2xx status carrying a `{ "error": msg }` envelope (or, as a fallback, raw text).
    | HttpFailure of status: int * message: string
    /// A non-2xx status carrying a `{ "errors": [ { field, message } ] }` validation envelope.
    | ValidationFailure of status: int * errors: Validate.ValidationError list
    /// A 2xx response whose body did not decode to the expected type.
    | DecodeFailure of string

/// A transport executes a Request and yields either the completed Response (whatever its
/// status) or a TransportFailure (the request never completed). HTTP-status interpretation
/// and body decoding are the generated client's responsibility (see `sendDecode`), never the
/// transport's — that keeps browser fetch, extension IPC and a test fake interchangeable.
type Transport = Request -> JS.Promise<Result<Response, ApiError>>

/// Render an ApiError as a single human-readable line, for UIs that just want a message.
/// Mirrors the extension's former `errorToString`: validation errors join as "field: message".
let renderError (error: ApiError) : string =
    match error with
    | TransportFailure msg -> msg
    | HttpFailure (_, msg) -> msg
    | ValidationFailure (_, errors) ->
        errors
        |> List.map (fun (e: Validate.ValidationError) -> sprintf "%s: %s" e.Field e.Message)
        |> String.concat "; "
    | DecodeFailure msg -> msg

/// Interpret a non-2xx Response body as the appropriate ApiError. Recognises the two server
/// envelopes — `{ "errors": [...] }` (field validation) and `{ "error": msg }` — and falls
/// back to the raw body text. This is the single place HTTP error decoding lives.
let errorFromResponse (resp: Response) : ApiError =
    let validationErrorDecoder : Decoder<Validate.ValidationError> =
        Decode.map2
            (fun field message -> { Validate.ValidationError.Field = field; Message = message })
            (Decode.field "field" Decode.string)
            (Decode.field "message" Decode.string)
    let validationDecoder : Decoder<Validate.ValidationError list> =
        Decode.field "errors" (Decode.list validationErrorDecoder)
    match Decode.fromString validationDecoder resp.Body with
    | Ok errors -> ValidationFailure (resp.Status, errors)
    | Error _ ->
        match Decode.fromString (Decode.field "error" Decode.string) resp.Body with
        | Ok msg -> HttpFailure (resp.Status, msg)
        | Error _ -> HttpFailure (resp.Status, resp.Body)

/// Run a request through the transport and decode a 2xx JSON body, mapping every failure mode
/// to the typed ApiError. The generated per-endpoint functions call this with the request they
/// built and the response decoder they own — it is the only authored-to-wire glue they need.
let sendDecode (transport: Transport) (req: Request) (decoder: Decoder<'T>) : JS.Promise<Result<'T, ApiError>> =
    promise {
        let! outcome = transport req
        match outcome with
        | Error apiError -> return Error apiError
        | Ok resp ->
            if resp.Status >= 200 && resp.Status < 300 then
                match Decode.fromString decoder resp.Body with
                | Ok value -> return Ok value
                | Error msg -> return Error (DecodeFailure msg)
            else
                return Error (errorFromResponse resp)
    }

/// As `sendDecode`, for endpoints whose success carries no body worth decoding (the caller
/// only needs to know it succeeded). A 2xx yields `Ok ()`; every failure is a typed ApiError.
let sendUnit (transport: Transport) (req: Request) : JS.Promise<Result<unit, ApiError>> =
    promise {
        let! outcome = transport req
        match outcome with
        | Error apiError -> return Error apiError
        | Ok resp ->
            if resp.Status >= 200 && resp.Status < 300 then return Ok ()
            else return Error (errorFromResponse resp)
    }
