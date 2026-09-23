module Identity.Grants

// The OPTIONAL access-control (role grant) sub-surface of the identity module. A host composes this
// ONLY if it wants delegated roles — by importing identity.grants.server.props AND adding the
// GrantModels gen slice AND applying the grants migration. Adding plain identity to an app never
// enables it (article hosts omit all three). Self-contained: reads the shared Identity.Server + its
// own grant SQL + Hedge primitives; names no app Env / Server.Db. A host binds these into
// Hedge.AccessControl.Deps (see the app's AccessConfig).

open Fable.Core
open Hedge.Workers
open Identity.Db

/// A guest row that is not soft-deleted. Grant resolution honors guests.deleted_at so a deleted
/// guest's live session can't authorize (the guest-session cookie policy itself does not check it).
let guestNotDeleted =
    "SELECT 1 FROM guests WHERE id = ? AND deleted_at IS NULL LIMIT 1"

/// Is there an ENABLED grant for this OAuth subject (provider, provider_user_id) + role? Absent or
/// disabled → no row → not authorized. Keyed on the pair (stable across identity merges).
let grantEnabled =
    "SELECT 1 FROM grants WHERE provider = ? AND provider_user_id = ? AND role = ? AND enabled = 1 LIMIT 1"

/// The guest's ACTIVE identity as its durable OAuth subject (provider, provider_user_id) — the key a
/// grant is checked against. None when the guest is soft-deleted, has no active identity, or its
/// active identity is anonymous (which must never hold a role). Bind into AccessControl.Deps.ActiveSubject.
let activeSubject (db: D1Database) (guestId: string) : JS.Promise<(string * string) option> =
    promise {
        let! live = (bind (db.prepare guestNotDeleted) [| box guestId |]).first()
        if isNull (box live) then return None
        else
            let! id = Identity.Server.activeFor db guestId
            match id with
            | Some i when i.Provider <> "anonymous" -> return Some (i.Provider, i.ProviderUserId)
            | _ -> return None
    }

/// Is there an enabled grant for this OAuth subject + role? Unknown/absent/disabled → false. Fresh
/// lookup per call, so revocation takes effect at once. Bind into AccessControl.Deps.HasGrant.
let hasGrant (db: D1Database) (provider: string) (providerUserId: string) (role: string) : JS.Promise<bool> =
    promise {
        let! row = (bind (db.prepare grantEnabled) [| box provider; box providerUserId; box role |]).first()
        return not (isNull (box row))
    }
