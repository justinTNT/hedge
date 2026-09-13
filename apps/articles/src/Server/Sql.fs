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

/// A provider account, regardless of which guest currently holds it. Ordered by
/// history (comment count), not age; earliest created breaks ties.
let findIdentityByProviderGlobal = """
    SELECT i.id, i.guest_id
    FROM identities i
    WHERE i.provider = ? AND i.provider_user_id = ?
    ORDER BY (SELECT COUNT(*) FROM articles_comments c WHERE c.identity_id = i.id) DESC, i.created_at ASC
    LIMIT 1"""

let moveIdentitiesToGuest =
    "UPDATE identities SET guest_id = ? WHERE guest_id = ?"

let countCommentsForIdentity =
    "SELECT COUNT(*) AS n FROM articles_comments WHERE identity_id = ?"

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

let reassignComments = """
    UPDATE articles_comments
    SET identity_id = ?, author = (SELECT name FROM identities WHERE id = ?)
    WHERE identity_id = ?"""

// Content SQL (posts + comments) now lives in the articles module (Articles.Sql,
// Tables-driven). The identity statements above reference `articles_comments` as a
// plain literal — this file compiles before the generated Server.Db, so it can't use
// the `Tables` constants (same reason microblog's identity SQL literals `blog_comments`).
// On justat the blog module owns its own `blog_comments`; re-attributing a merged
// identity's comments across BOTH content tables is a follow-up (this preserves the
// pre-module behaviour, which only ever re-attributed the articles table).
