module Hedge.Sigv4

// AWS Signature Version 4 for signing HTTPS requests from a Worker (used by the SES email sender). SigV4
// is entirely HMAC-SHA256 + SHA-256 — no asymmetric crypto — so it needs only WebCrypto's symmetric HMAC
// (its own emits below, returning raw bytes for the key-derivation chain) plus Hedge.MobileSession.sha256Hex
// for the canonical-request and payload hashes. `signingKeyHex` is exposed so the derivation can be tested
// against AWS's documented vector; `authorizationHeader` builds the full `Authorization` header value.

open Fable.Core
open Fable.Core.JsInterop

[<Emit("new TextEncoder().encode($0)")>]
let private utf8 (s: string) : obj = jsNative

// importKey('raw', key, HMAC-SHA256) then sign → the HMAC output as an ArrayBuffer (feeds the next key in
// the derivation chain). `key` is a BufferSource: a Uint8Array (the initial "AWS4"+secret) or a prior buffer.
[<Emit("crypto.subtle.importKey('raw', $0, { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']).then(k => crypto.subtle.sign('HMAC', k, $1))")>]
let private hmacRaw (key: obj) (data: obj) : JS.Promise<obj> = jsNative

[<Emit("Array.from(new Uint8Array($0)).map(b => b.toString(16).padStart(2, '0')).join('')")>]
let private toHex (buffer: obj) : string = jsNative

let private hmac (key: obj) (msg: string) : JS.Promise<obj> = hmacRaw key (utf8 msg)

/// The SigV4 signing key: HMAC chain kDate→kRegion→kService→kSigning ("aws4_request"). Returned as a
/// buffer; `signingKeyHex` wraps it for the deterministic test vector.
let signingKey (secret: string) (dateStamp: string) (region: string) (service: string) : JS.Promise<obj> =
    promise {
        let! kDate = hmac (utf8 ("AWS4" + secret)) dateStamp
        let! kRegion = hmac kDate region
        let! kService = hmac kRegion service
        let! kSigning = hmac kService "aws4_request"
        return kSigning
    }

/// Signing key as lowercase hex — for testing against AWS's published derivation vector.
let signingKeyHex (secret: string) (dateStamp: string) (region: string) (service: string) : JS.Promise<string> =
    promise { let! k = signingKey secret dateStamp region service in return toHex k }

/// Current instant as SigV4 timestamps: (amzDate "YYYYMMDDTHHMMSSZ", dateStamp "YYYYMMDD").
[<Emit("(() => { const d = new Date().toISOString().replace(/[:-]|\\.\\d{3}/g, ''); return [d, d.slice(0,8)]; })()")>]
let now () : string * string = jsNative

/// Build the SigV4 `Authorization` header value for a request. `signedHeaders` is the sorted list of
/// (lowercase-name, value) that must be signed (typically content-type, host, x-amz-date). `payloadHash`
/// is sha256-hex of the body. Deterministic given its inputs (so it is unit-testable).
let authorizationHeader
    (accessKeyId: string) (secretKey: string) (region: string) (service: string)
    (amzDate: string) (dateStamp: string)
    (httpMethod: string) (canonicalUri: string) (canonicalQuery: string)
    (signedHeaders: (string * string) list) (payloadHash: string) : JS.Promise<string> =
    promise {
        let signedNames = signedHeaders |> List.map fst |> String.concat ";"
        let canonicalHeaders =
            signedHeaders
            |> List.map (fun (n, v) -> sprintf "%s:%s\n" n (v.Trim()))
            |> String.concat ""
        let canonicalRequest =
            String.concat "\n"
                [ httpMethod; canonicalUri; canonicalQuery; canonicalHeaders + "\n" + signedNames; payloadHash ]
        // Note: canonicalHeaders already ends in "\n"; the join above yields the required blank line
        // before SignedHeaders (…headers\n + "\n" + signedNames).
        let! canonicalHash = Hedge.MobileSession.sha256Hex canonicalRequest
        let scope = sprintf "%s/%s/%s/aws4_request" dateStamp region service
        let stringToSign =
            String.concat "\n" [ "AWS4-HMAC-SHA256"; amzDate; scope; canonicalHash ]
        let! kSigning = signingKey secretKey dateStamp region service
        let! sigBuf = hmac kSigning stringToSign
        let signature = toHex sigBuf
        return
            sprintf "AWS4-HMAC-SHA256 Credential=%s/%s, SignedHeaders=%s, Signature=%s"
                accessKeyId scope signedNames signature
    }
