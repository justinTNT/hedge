module Hedge.GuestCookie

// Signed guest-credential envelope (the security core of the signed-guest-cookies work).
//
//   token   = "v1.<keyId>.<payload>.<mac>"
//   payload = base64url("<guestId>|<issuedAt>|<expiry>|<audience>")
//   mac     = HMAC-SHA256(keySecret, "hedge.guestcookie.v1|<keyId>|<payload>")   [purpose-separated]
//
// The signature proves the credential was server-issued; it does not encrypt (a guest id is not
// secret to its own owner). What it stops is anyone presenting an *arbitrary* or *harvested* id and
// having it accepted — the whole point vs. the old raw-UUID cookie. Verification is constant-time
// WebCrypto (Hedge.Workers.hmacVerify), parsing is strict + bounded, and a signed-looking token that
// fails NEVER falls back to legacy parsing (work-order rule 6). Clock is injected (`now`) so fixtures
// are deterministic; id generation is the caller's (Workers.newId), also injectable in tests.

open Fable.Core
open Hedge.Workers

let [<Literal>] private Version = "v1"
let [<Literal>] private MacPurpose = "hedge.guestcookie.v1"

/// One signing key: an id (names the key in the token so keys can rotate) and its secret.
type SigningKey = { KeyId: string; Secret: string }

/// Guest signing configuration — independent of OAuth. The active key signs + verifies; previous
/// keys verify only, each until its retirement epoch (so old tokens stay valid across a key roll
/// without the old secret being able to mint new ones).
type Config =
    { Active: SigningKey
      Audience: string
      Previous: (SigningKey * int) list }   // (key, retireAtEpoch)

/// The claims carried in a verified credential.
type Claims =
    { GuestId: string
      IssuedAt: int
      Expiry: int
      Audience: string }

/// Outcome of verifying a cookie value. `Legacy` is a raw non-signed value (bounded, opaque) whose
/// eligibility for a bridge upgrade is decided by policy + the guests table — never here.
type Verification =
    | Signed of Claims
    | Expired
    | Invalid
    | Legacy of string
    | Missing

let private encodePayload (c: Claims) : string =
    base64urlEncode (sprintf "%s|%d|%d|%s" c.GuestId c.IssuedAt c.Expiry c.Audience)

let private macInput (keyId: string) (payload: string) : string =
    sprintf "%s|%s|%s" MacPurpose keyId payload

/// Sign claims with a specific key → the envelope string.
let signWith (key: SigningKey) (claims: Claims) : JS.Promise<string> =
    promise {
        let payload = encodePayload claims
        let! mac = hmacSha256 key.Secret (macInput key.KeyId payload)
        return sprintf "%s.%s.%s.%s" Version key.KeyId payload mac
    }

/// Issue a fresh credential for a guest id, valid for `lifetimeSeconds` from `now`.
let issue (config: Config) (now: int) (lifetimeSeconds: int) (guestId: string) : JS.Promise<string> =
    signWith config.Active
        { GuestId = guestId; IssuedAt = now; Expiry = now + lifetimeSeconds; Audience = config.Audience }

// Strict, bounded decode of a payload → Claims (exactly 4 fields; integer times; non-empty id).
let private tryDecodePayload (payload: string) : Claims option =
    try
        let parts = (base64urlDecode payload).Split('|')
        if parts.Length <> 4 then None
        else
            match System.Int32.TryParse parts.[1], System.Int32.TryParse parts.[2] with
            | (true, iat), (true, exp) when parts.[0] <> "" ->
                Some { GuestId = parts.[0]; IssuedAt = iat; Expiry = exp; Audience = parts.[3] }
            | _ -> None
    with _ -> None

// Resolve the key for a keyId among active + still-unretired previous keys.
let private keyFor (config: Config) (now: int) (keyId: string) : SigningKey option =
    if config.Active.KeyId = keyId then Some config.Active
    else config.Previous |> List.tryFind (fun (k, retireAt) -> k.KeyId = keyId && now < retireAt) |> Option.map fst

/// Verify a raw cookie value against the active + previous keys at time `now`.
let verify (config: Config) (now: int) (cookieValue: string option) : JS.Promise<Verification> =
    promise {
        match cookieValue with
        | None | Some "" -> return Missing
        | Some raw when raw.StartsWith(Version + ".") ->
            // Signed-looking: it must verify as a signed token; it never degrades to legacy.
            let parts = raw.Split('.')
            if parts.Length <> 4 then return Invalid
            else
                let keyId, payload, mac = parts.[1], parts.[2], parts.[3]
                match keyFor config now keyId with
                | None -> return Invalid
                | Some key ->
                    let! ok = hmacVerify key.Secret (macInput keyId payload) mac
                    if not ok then return Invalid
                    else
                        match tryDecodePayload payload with
                        | None -> return Invalid
                        | Some claims when claims.Audience <> config.Audience -> return Invalid
                        | Some claims when now >= claims.Expiry -> return Expired
                        | Some claims -> return Signed claims
        | Some raw ->
            // Not signed-looking → a raw legacy value; bound its length before handing it to policy.
            if raw.Length > 200 then return Invalid else return Legacy raw
    }

/// A still-valid signed credential due for renewal (< `renewWithinSeconds` left before expiry).
let needsRenewal (renewWithinSeconds: int) (now: int) (claims: Claims) : bool =
    claims.Expiry - now < renewWithinSeconds
