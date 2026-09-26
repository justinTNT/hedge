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
