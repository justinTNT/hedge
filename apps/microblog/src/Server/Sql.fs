module Server.Sql

/// Every hand-written SQL statement in the app, named and in one place.
/// check-sql.sh EXPLAIN-prepares each of these against both a fresh
/// schema.sql database and a migrations-built database at test time —
/// keep statements as plain literals (no string concatenation).

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

/// A provider account, regardless of which guest currently holds it. This is
/// what makes one identity usable from several machines: the second machine
/// finds the existing identity instead of minting a parallel one.
/// Ordered by history, not age: pre-fix data can hold several rows for one
/// provider account (one per browser that ever signed in), and the canonical
/// one is whichever actually carries the comments — which is not necessarily
/// the oldest. Earliest created breaks ties.
let findIdentityByProviderGlobal = """
    SELECT i.id, i.guest_id
    FROM identities i
    WHERE i.provider = ? AND i.provider_user_id = ?
    ORDER BY (SELECT COUNT(*) FROM comments c WHERE c.identity_id = i.id) DESC, i.created_at ASC
    LIMIT 1"""

/// Fold one guest's identities into another (their comments follow, since
/// comments are attributed to the identity, not the guest).
let moveIdentitiesToGuest =
    "UPDATE identities SET guest_id = ? WHERE guest_id = ?"

let countCommentsForIdentity =
    "SELECT COUNT(*) AS n FROM comments WHERE identity_id = ?"

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

// ---- Attribution (see Server.Attribution) ----

let reassignComments = """
    UPDATE comments
    SET identity_id = ?, author = (SELECT name FROM identities WHERE id = ?)
    WHERE identity_id = ?"""

// ---- Items / comments / tags ----

let itemBySlug =
    "SELECT id, title, link, image, extract, owner_comment, slug, created_at, updated_at, view_count, deleted_at FROM items WHERE slug = ?"

let tagsForItem =
    "SELECT t.name FROM tags t JOIN item_tags it ON t.id = it.tag_id WHERE it.item_id = ?"

let picturesForItemComments =
    "SELECT DISTINCT i.id, i.picture FROM identities i JOIN comments c ON c.identity_id = i.id WHERE c.item_id = ?"

let insertComment = """
    INSERT INTO comments (id, item_id, identity_id, parent_id, author, content, removed, created_at)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?)"""

let tagNames =
    "SELECT name FROM tags ORDER BY name"

let itemsByTag = """
    SELECT i.*
    FROM items i
    JOIN item_tags it ON i.id = it.item_id
    JOIN tags t ON it.tag_id = t.id
    WHERE t.name = ?
    ORDER BY i.created_at DESC LIMIT 50"""

let insertTag =
    "INSERT OR IGNORE INTO tags (id, name, created_at) VALUES (?, ?, ?)"

let linkItemTag =
    "INSERT INTO item_tags (item_id, tag_id) SELECT ?, id FROM tags WHERE name = ?"
