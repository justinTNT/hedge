module Server.Sql

/// Every hand-written SQL statement in the app, named and in one place.
/// Keep statements as plain literals (no string concatenation).

// ---- Identity policy (ported verbatim from microblog) ----

/// Active identity = most recently activated.
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

// `findIdentityByProviderGlobal` (provider-account lookup ranked by comment history) and
// `countCommentsForIdentity` (an identity's comment count) are built in Server.Attribution
// from `AttributionPolicy.commentTables`, so they sum across EVERY content module the site
// composes (articles + blog on justat), not just articles_comments. They were single-table
// literals here; a cross-module SUM can't be a static literal (the table set is per-site).

let moveIdentitiesToGuest =
    "UPDATE identities SET guest_id = ? WHERE guest_id = ?"

let moveIdentityToGuest =
    "UPDATE identities SET guest_id = ? WHERE id = ?"

let anonymousIdentityForGuest =
    "SELECT id FROM identities WHERE guest_id = ? AND provider = 'anonymous' ORDER BY created_at LIMIT 1"

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

// ---- Attribution ----
//
// Comment re-attribution on a merge is now module-owned: each content module exposes a
// Tables-driven `reassignComments` (Articles.Sql / Blog.Sql), composed per-site into the
// merge policy by `Server.AttributionPolicy` and run by `Server.Attribution.reassign`.
// This closes the pre-module gap where only `articles_comments` was re-attributed — on
// justat a merged identity's `blog_comments` now move too. The provider-lookup ranking +
// comment counts (see the note where those literals used to be) are likewise summed across
// all composed content tables, so every identity-history path is now module-aware.
