module Identity.Mobile

// OPT-IN mobile bearer-session STORAGE (Capacitor POC) — the server side of Hedge.MobileSession's
// injected LookupByHash. A host composes this only if it mints/reads native bearer sessions (add the
// MobileModels gen slice + import identity.mobile.server.props + apply the mobile_sessions migration).
// Self-contained: its own SQL over the generated `mobile_sessions` table + Hedge primitives (secret
// generation, SHA-256). Stores ONLY the hash of the opaque bearer; the secret is returned once to the
// caller and never persisted server-side.

open Fable.Core
open Fable.Core.JsInterop
open Hedge.Workers

let insertSession = "INSERT INTO mobile_sessions (id, guest_id, expires_at, created_at) VALUES (?, ?, ?, ?)"
let sessionByHash = "SELECT guest_id, expires_at FROM mobile_sessions WHERE id = ? LIMIT 1"
let deleteSession = "DELETE FROM mobile_sessions WHERE id = ?"
let deleteExpiredSessions = "DELETE FROM mobile_sessions WHERE expires_at <= ?"

/// Resolve a bearer HASH to its guest id when a matching, UNEXPIRED session exists. Fail-closed:
/// unknown or past-expiry -> None. Bind into Hedge.MobileSession.Deps.LookupByHash with `now` fixed
/// per request. Fresh lookup per call, so revocation/expiry take effect at once.
let resolveByHash (db: D1Database) (now: int) (tokenHash: string) : JS.Promise<string option> =
    promise {
        let! row = (bind (db.prepare sessionByHash) [| box tokenHash |]).first()
        if isNull (box row) then return None
        else
            let expiresAt : int = row?expires_at
            if expiresAt <= now then return None
            else return Some (row?guest_id : string)
    }

/// Mint a new opaque bearer for `guestId`: generate a high-entropy secret, store ONLY its SHA-256 hash
/// with an absolute expiry, and RETURN THE SECRET (the only time it exists server-side). The caller
/// stores it in the device Keychain/Keystore and presents it as `Authorization: Bearer <secret>`.
let mintSession (db: D1Database) (guestId: string) (now: int) (ttlSeconds: int) : JS.Promise<string> =
    promise {
        let secret = newId () + newId ()
        let! hash = Hedge.MobileSession.sha256Hex secret
        let! _ = (bind (db.prepare insertSession) [| box hash; box guestId; box (now + ttlSeconds); box now |]).run()
        return secret
    }

/// Revoke one session by its bearer HASH (mobile sign-out, or rotate on login).
let revokeByHash (db: D1Database) (tokenHash: string) : JS.Promise<unit> =
    promise { let! _ = (bind (db.prepare deleteSession) [| box tokenHash |]).run() in return () }

/// Best-effort cleanup of expired rows — call opportunistically on bootstrap/exchange.
let purgeExpired (db: D1Database) (now: int) : JS.Promise<unit> =
    promise { let! _ = (bind (db.prepare deleteExpiredSessions) [| box now |]).run() in return () }

// ---- PKCE one-time codes (browser-OAuth login handoff) ----

let [<Literal>] CodeTtlSeconds = 300   // 5 minutes: the login round trip, not a session
let insertCode = "INSERT INTO mobile_auth_codes (id, guest_id, challenge, expires_at, created_at) VALUES (?, ?, ?, ?, ?)"
// Consume ONLY a code that matches its PKCE proof AND is unexpired — the challenge check lives in the
// DELETE so it is atomic: a wrong verifier finds no row and never burns a legitimate code, a replay
// finds nothing, and an expired code is ignored (cleaned separately). One writer at a time in SQLite,
// so this can't race a concurrent consume.
let consumeCodeSql = "DELETE FROM mobile_auth_codes WHERE id = ? AND challenge = ? AND expires_at > ? RETURNING guest_id"

/// Mint a one-time authorization code for `guestId`, bound to the app's PKCE `challenge`
/// (= sha256 of its verifier). Stores only the code's SHA-256 hash + a short expiry; returns the raw
/// code once (handed to the app via the deeplink). Consumed at exchange.
let mintCode (db: D1Database) (guestId: string) (challenge: string) (now: int) : JS.Promise<string> =
    promise {
        let code = newId () + newId ()
        let! hash = Hedge.MobileSession.sha256Hex code
        let! _ = (bind (db.prepare insertCode) [| box hash; box guestId; box challenge; box (now + CodeTtlSeconds); box now |]).run()
        return code
    }

/// Consume a one-time code + verify its PKCE proof ATOMICALLY. Returns the bound guestId only when the
/// code exists, is unexpired, and sha256(verifier) equals its stored challenge; None otherwise. A wrong
/// verifier does NOT consume the code (the DELETE's challenge predicate misses), so a legitimate later
/// exchange with the right verifier still works.
let consumeCode (db: D1Database) (now: int) (rawCode: string) (verifier: string) : JS.Promise<string option> =
    promise {
        let! hash = Hedge.MobileSession.sha256Hex rawCode
        let! proof = Hedge.MobileSession.sha256Hex verifier
        let! row = (bind (db.prepare consumeCodeSql) [| box hash; box proof; box now |]).first()
        if isNull (box row) then return None
        else return Some (row?guest_id : string)
    }

/// "Activate + merge": reassign the APP's anonymous guest's content to the VERIFIED guest's active
/// identity. Comments are attributed by identity_id (guest_id was dropped in migration 0009), so this
/// resolves the anon guest's anonymous identity and the verified guest's active identity and reassigns
/// between them (cross-guest — the two identities sit under different guests in the mobile flow). A
/// no-op when the guests are the same, the anon guest never commented (no anon identity), or the
/// verified guest has no active identity. `reassignStatements` is the host's attribution policy.
let mergeAnonInto (db: D1Database) (reassignStatements: string list) (anonGuestId: string) (verifiedGuestId: string) : JS.Promise<unit> =
    promise {
        if anonGuestId = verifiedGuestId || anonGuestId = "" then return ()
        else
            let! anonRow = (bind (db.prepare Identity.Sql.anonymousIdentityForGuest) [| box anonGuestId |]).first()
            let! verifiedRow = (bind (db.prepare Identity.Sql.activeIdentityForGuest) [| box verifiedGuestId |]).first()
            if isNull (box anonRow) || isNull (box verifiedRow) then return ()
            else
                do! Identity.Attribution.reassign db reassignStatements (anonRow?id : string) (verifiedRow?id : string)
    }
