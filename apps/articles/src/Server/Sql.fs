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
    ORDER BY (SELECT COUNT(*) FROM comments c WHERE c.identity_id = i.id) DESC, i.created_at ASC
    LIMIT 1"""

let moveIdentitiesToGuest =
    "UPDATE identities SET guest_id = ? WHERE guest_id = ?"

let countCommentsForIdentity =
    "SELECT COUNT(*) AS n FROM comments WHERE identity_id = ?"

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
    UPDATE comments
    SET identity_id = ?, author = (SELECT name FROM identities WHERE id = ?)
    WHERE identity_id = ?"""

// ---- Articles / comments ----

let articleBySlug =
    "SELECT id, title, teaser, body, image, article_date, slug, created_at, updated_at, view_count, deleted_at FROM articles WHERE slug = ?"

// Cursor-paginated list (infinite scroll). Ordered by (article_date, id) so the
// compound cursor is stable across shared dates. Body is deliberately excluded —
// the list doesn't need it and essays can be large. Bind: [limit].
let listFirstPage = """
    SELECT id, title, teaser, image, article_date, slug
    FROM articles
    WHERE deleted_at IS NULL
    ORDER BY article_date DESC, id DESC
    LIMIT ?"""

// Bind: [cursorTs, cursorTs, cursorId, limit].
let listAfterCursor = """
    SELECT id, title, teaser, image, article_date, slug
    FROM articles
    WHERE deleted_at IS NULL
      AND (article_date < ? OR (article_date = ? AND id < ?))
    ORDER BY article_date DESC, id DESC
    LIMIT ?"""

/// Just the columns social previews need (teaser stands in for description).
let articleMetaBySlugOrId =
    "SELECT id, title, teaser, image, slug FROM articles WHERE (slug = ? OR id = ?) AND deleted_at IS NULL"

let picturesForArticleComments =
    "SELECT DISTINCT i.id, i.picture FROM identities i JOIN comments c ON c.identity_id = i.id WHERE c.article_id = ?"

let insertComment = """
    INSERT INTO comments (id, article_id, identity_id, parent_id, author, content, removed, created_at)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?)"""
