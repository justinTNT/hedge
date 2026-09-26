module Hedge.MobileSession

// Bearer-session resolution for native (Capacitor) clients — the counterpart to the signed guest
// COOKIE that a cross-origin app WebView cannot carry. A native client presents an opaque bearer
// secret in `Authorization: Bearer <secret>`; the server stores only its SHA-256 hash (see the
// opt-in `mobile_sessions` table) and resolves the hash to the guest that session authenticates.
// Framework auth infrastructure, sibling of GuestSession: it names no identity module and no D1 — the
// hash LOOKUP is injected (an app binds its mobile_sessions query). Resolution is FAIL-CLOSED: a
// present-but-unresolvable/expired bearer REJECTS and never silently falls back to the cookie. Minting
// bearer rows (bootstrap / OAuth exchange) is a separate, host-owned concern; this module only reads.

open Fable.Core
open Fable.Core.JsInterop
open Hedge.Workers
open Hedge.GuestSession

/// App-bound primitive: a bearer's SHA-256 hash -> the guest id it authenticates, or None when no
/// unexpired session matches. An unknown/expired hash MUST return None (never conjure a session); the
/// app also enforces the absolute expiry inside this lookup so resolution stays fail-closed.
type Deps = { LookupByHash: string -> JS.Promise<string option> }

let [<Literal>] private BearerPrefix = "Bearer "

/// The raw bearer secret from `Authorization: Bearer <secret>`, or None when the header is absent or
/// not a non-empty Bearer credential. Transport only — the secret is hashed before any lookup.
let readBearer (request: WorkerRequest) : string option =
    let header = getHeader request "Authorization"
    if isNull (box header) || not (header.StartsWith BearerPrefix) then None
    else
        let token = header.Substring(BearerPrefix.Length).Trim()
        if token = "" then None else Some token

/// SHA-256 hex of a bearer secret. The stored `mobile_sessions.id` is this hash; the secret itself
/// only ever lives in the device Keychain/Keystore, never server-side.
[<Emit("crypto.subtle.digest('SHA-256', new TextEncoder().encode($0)).then(b => Array.from(new Uint8Array(b)).map(x => x.toString(16).padStart(2,'0')).join(''))")>]
let sha256Hex (secret: string) : JS.Promise<string> = jsNative

/// The outcome of inspecting a request for a bearer credential.
type BearerOutcome =
    /// No `Authorization: Bearer` header — the caller should try the cookie instead.
    | NoBearer
    /// A bearer that resolved to a live session for this guest.
    | Valid of guestId: string
    /// A bearer was presented but did not resolve (unknown, revoked or expired) — reject, do not
    /// fall back to a cookie: presenting a bad bearer must fail closed.
    | Invalid

/// Read + hash + resolve a request's bearer credential. Never throws for a missing header (returns
/// NoBearer); a present bearer that does not resolve is Invalid, not NoBearer.
let resolve (deps: Deps) (request: WorkerRequest) : JS.Promise<BearerOutcome> =
    promise {
        match readBearer request with
        | None -> return NoBearer
        | Some secret ->
            let! hash = sha256Hex secret
            let! guestId = deps.LookupByHash hash
            match guestId with
            | Some g -> return Valid g
            | None -> return Invalid
    }

/// Resolve a request to an accepted session, BEARER FIRST then cookie. A valid bearer is accepted
/// (mobile sessions carry no renewal cookie — the client refreshes its own bearer); a present-but-bad
/// bearer is REJECTED without consulting `cookieResolve` (fail closed, no silent downgrade); only the
/// absence of a bearer falls through to the cookie path. `cookieResolve` is the existing guest policy
/// (`fun req -> GuestSession.requireGuest deps (readCookie req)`), passed as a function so this stays
/// independent of GuestSession.Deps and unit-testable.
let requireGuestOrBearer
    (mobile: Deps)
    (cookieResolve: WorkerRequest -> JS.Promise<RequireResult>)
    (request: WorkerRequest)
    : JS.Promise<RequireResult> =
    promise {
        match! resolve mobile request with
        | Valid guestId -> return Accepted { GuestId = guestId; Replacement = None }
        | Invalid -> return Rejected
        | NoBearer -> return! cookieResolve request
    }

/// A bearer-aware guest WRITE service: bearer-first (fail-closed), else the app's cookie policy. A
/// drop-in for GuestSession.service where a host also accepts native bearers — a web request (no
/// bearer) behaves exactly as the cookie-only service, so existing browser writes are unchanged.
let service (mobile: Deps) (getGuestDeps: unit -> GuestSession.Deps) : GuestSession.Service =
    { Require = fun request ->
        requireGuestOrBearer mobile (fun req -> requireGuest (getGuestDeps ()) (readCookie req)) request }
