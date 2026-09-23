module Identity.Server

// The shared identity persistence functions (previously duplicated verbatim as each host's
// `Server.Identity`). Self-contained: reads its own `Identity.Sql` (sibling module) + `Identity.Db`
// row projection + the framework `Hedge.Workers` primitives (bind, D1) — never a host's Server.Db /
// Server.Sql. Hosts compile this via `identity.server.props` and call `Identity.Server.*`.
//
// Access-control (grants) resolution and attribution/merge policy are NOT here: those are the host's
// own concern (grants are an opt-in sub-surface; attribution is injected), so they stay host-side.

open Fable.Core
open Hedge.Workers
open Identity.Db

/// The guest's active identity — most recently activated, or None if the
/// guest has never been identified (no row, or nothing activated yet).
let activeFor (db: D1Database) (guestId: string) : JS.Promise<IdentityRow option> =
    promise {
        let! row = (bind (db.prepare Sql.activeIdentityForGuest) [| box guestId |]).first()
        return if isNull (box row) then None else Some (parseIdentityRow row)
    }

/// All identities a guest can switch between, active-first.
let listFor (db: D1Database) (guestId: string) : JS.Promise<IdentityRow array> =
    promise {
        let! result = (bind (db.prepare Sql.listIdentitiesForGuest) [| box guestId |]).all()
        return result.results |> Array.map parseIdentityRow
    }

/// Ownership check — every identity mutation must verify the identity
/// belongs to the requesting guest.
let belongsToGuest (db: D1Database) (identityId: string) (guestId: string) : JS.Promise<bool> =
    promise {
        let! row = (bind (db.prepare Sql.identityBelongsToGuest) [| box identityId; box guestId |]).first()
        return not (isNull (box row))
    }

/// Activation is history — most recent activated_at wins (see activeFor).
let setActive (db: D1Database) (identityId: string) (now: int) : JS.Promise<unit> =
    promise {
        let! _ = (bind (db.prepare Sql.setIdentityActive) [| box now; box identityId |]).run()
        return ()
    }

/// Bound statement (not run) so callers can batch it with other writes.
let ensureGuestStmt (db: D1Database) (guestId: string) (now: int) : D1PreparedStatement =
    bind (db.prepare Sql.ensureGuest) [| box guestId; box guestId; box now |]

/// Bound statement: create an activated anonymous identity unless the
/// guest already has an active one.
let ensureAnonymousStmt (db: D1Database) (identityId: string) (guestId: string) (name: string) (now: int) : D1PreparedStatement =
    bind (db.prepare Sql.ensureAnonymousIdentity) [| box identityId; box guestId; box name; box now; box now; box guestId |]

/// Signed-cookie migration: is this presented legacy cookie value an eligible existing guest — a
/// non-deleted row created before `migrationStartEpoch` whose stored session value matches? Injected
/// into Hedge.GuestSession.Deps.LegacyEligible; only ever consulted under an active Bridge.
let legacyEligible (db: D1Database) (value: string) (migrationStartEpoch: int) : JS.Promise<bool> =
    promise {
        let! row = (bind (db.prepare Sql.legacyGuestEligible) [| box value; box value; box migrationStartEpoch |]).first()
        return not (isNull (box row))
    }

/// Signed-cookie migration: does this guest have a linked (non-anonymous) identity? Injected into
/// Hedge.GuestSession.Deps.LegacyHasLinkedIdentity — linked guests re-login instead of auto-upgrading.
let hasLinkedIdentity (db: D1Database) (guestId: string) : JS.Promise<bool> =
    promise {
        let! row = (bind (db.prepare Sql.guestHasLinkedIdentity) [| box guestId |]).first()
        return not (isNull (box row))
    }
