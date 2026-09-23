module Identity.Grants

// The OPTIONAL access-control (role grant) sub-surface of the identity module — ROLE CHECKING ONLY. A
// host composes this ONLY if it wants delegated roles (import identity.grants.server.props + add the
// GrantModels gen slice + apply the grants migration). Authenticated-SUBJECT resolution is NOT here —
// it is core (Identity.Server.activeSubject), available for ownership without composing grants; role
// checking layers on top of it. Self-contained: reads its own grant SQL + Hedge primitives; a host
// binds these into Hedge.AccessControl.Deps (see the app's AccessConfig).

open Fable.Core
open Hedge.Workers

/// Is there an ENABLED grant for this OAuth subject (provider, provider_user_id) + role? Absent or
/// disabled → no row → not authorized. Keyed on the pair (stable across identity merges).
let grantEnabled =
    "SELECT 1 FROM grants WHERE provider = ? AND provider_user_id = ? AND role = ? AND enabled = 1 LIMIT 1"

/// Is there an enabled grant for this OAuth subject + role? Unknown/absent/disabled → false. Fresh
/// lookup per call, so revocation takes effect at once. Bind into AccessControl.Deps.HasGrant.
let hasGrant (db: D1Database) (provider: string) (providerUserId: string) (role: string) : JS.Promise<bool> =
    promise {
        let! row = (bind (db.prepare grantEnabled) [| box provider; box providerUserId; box role |]).first()
        return not (isNull (box row))
    }
