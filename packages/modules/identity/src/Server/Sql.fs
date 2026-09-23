module Identity.Sql

// The shared identity persistence SQL (unprefixed `guests` / `identities`), previously duplicated
// verbatim in every identity host's `Server.Sql`. Table names are plain literals so check-sql.sh can
// EXPLAIN-prepare each against both a fresh schema.sql database and a migrations-built one. Grants SQL
// (access-control) and host-specific glue (e.g. darwin.news over the blog module's tables) stay in the
// host's own `Server.Sql`; only the identity layer lives here.

// ---- Identity policy ----

/// Active identity = most recently activated. Must agree with the client
/// switcher's notion of "active" and with the merge source in Attribution.
let activeIdentityForGuest = """
    SELECT id, guest_id, provider, provider_user_id, name, picture, email, activated_at, created_at
    FROM identities
    WHERE guest_id = ? AND activated_at IS NOT NULL
    ORDER BY activated_at DESC LIMIT 1"""

let listIdentitiesForGuest = """
    SELECT id, guest_id, provider, provider_user_id, name, picture, email, activated_at, created_at
    FROM identities
    WHERE guest_id = ?
    ORDER BY activated_at DESC NULLS LAST, created_at DESC"""

let ensureGuest =
    "INSERT OR IGNORE INTO guests (id, session_id, created_at) VALUES (?, ?, ?)"

/// First comment from an un-identified guest creates an activated anonymous
/// identity — unless the guest already has an active one.
let ensureAnonymousIdentity = """
    INSERT OR IGNORE INTO identities (id, guest_id, provider, provider_user_id, name, picture, email, activated_at, created_at)
    SELECT ?, ?, 'anonymous', '', ?, '', NULL, ?, ?
    WHERE NOT EXISTS (SELECT 1 FROM identities WHERE guest_id = ? AND activated_at IS NOT NULL)"""

let findIdentityByProvider =
    "SELECT id FROM identities WHERE guest_id = ? AND provider = ? AND provider_user_id = ?"

/// Fold one guest's identities into another (their comments follow, since
/// comments are attributed to the identity, not the guest).
let moveIdentitiesToGuest =
    "UPDATE identities SET guest_id = ? WHERE guest_id = ?"

/// Park a single identity on another guest. Disconnect uses this to abandon a
/// credentialed identity onto a fresh empty guest: its comments stay attached,
/// so signing in with that provider again reclaims the whole history.
let moveIdentityToGuest =
    "UPDATE identities SET guest_id = ? WHERE id = ?"

/// The guest's anonymous identity, if they have one. Not guaranteed to exist:
/// it's created by the comment path, so a guest who signed in with a provider
/// before ever commenting has none.
let anonymousIdentityForGuest =
    "SELECT id FROM identities WHERE guest_id = ? AND provider = 'anonymous' ORDER BY created_at LIMIT 1"

/// Unconditional anonymous identity, for disconnect's fallback — unlike
/// ensureAnonymousIdentity this doesn't skip when an active identity exists,
/// because the identity being disconnected is the active one.
let insertAnonymousIdentity = """
    INSERT INTO identities (id, guest_id, provider, provider_user_id, name, picture, email, activated_at, created_at)
    VALUES (?, ?, 'anonymous', '', ?, '', NULL, ?, ?)"""

let countIdentitiesForGuest =
    "SELECT COUNT(*) AS n FROM identities WHERE guest_id = ?"

let deleteIdentityById =
    "DELETE FROM identities WHERE id = ?"

/// New provider identity is inserted un-activated — the user chooses
/// merge/fresh before it becomes active.
let insertProviderIdentity = """
    INSERT INTO identities (id, guest_id, provider, provider_user_id, name, picture, email, activated_at, created_at)
    VALUES (?, ?, ?, ?, ?, ?, ?, NULL, ?)"""

let refreshIdentityProfile =
    "UPDATE identities SET name = ?, picture = ?, email = ? WHERE id = ?"

let identityBelongsToGuest =
    "SELECT id FROM identities WHERE id = ? AND guest_id = ?"

let setIdentityActive =
    "UPDATE identities SET activated_at = ? WHERE id = ?"

// ---- Signed-guest-cookie migration (legacy upgrade eligibility) ----

/// Legacy-cookie migration eligibility: a non-deleted guest created BEFORE migration start whose
/// stored session value exactly matches the presented legacy cookie. Identity creation stores the
/// same value in guests.id and guests.session_id (see ensureGuest), so both must match. An unknown
/// value never matches a row — row existence is required, never fabricated (work-order rule 2/6).
let legacyGuestEligible =
    "SELECT 1 FROM guests WHERE id = ? AND session_id = ? AND created_at < ? AND deleted_at IS NULL LIMIT 1"

/// Whether a guest has any linked (claimed, non-anonymous) identity. Such guests do NOT auto-upgrade
/// an unsigned legacy cookie — they recover a linked provider identity through verified OAuth
/// adoption; only anonymous ownership bridges (work-order rule 5).
let guestHasLinkedIdentity =
    "SELECT 1 FROM identities WHERE guest_id = ? AND provider <> 'anonymous' LIMIT 1"

/// A guest row that is not soft-deleted. Authenticated-subject resolution honors guests.deleted_at so
/// a deleted guest's live session can't authorize (the guest-session cookie policy itself doesn't
/// check it). Core (not grants): subject identity is needed for ownership too, not only role checks.
let guestNotDeleted =
    "SELECT 1 FROM guests WHERE id = ? AND deleted_at IS NULL LIMIT 1"
