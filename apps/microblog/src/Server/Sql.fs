module Server.Sql

/// The app's hand-written SQL: the shared identity layer, plus the two darwin.news
/// glue statements (rhyming + social-preview) that read the composed blog module's
/// content tables. check-sql.sh EXPLAIN-prepares each of these against both a fresh
/// schema.sql database and a migrations-built database at test time — keep statements
/// as plain literals (no string concatenation).
///
/// Where identity/attribution touch comments, they name the app's own content table
/// (blog_comments) directly: this app composes [identity, blog], so its comments live
/// there. The blog *module* stays table-name-agnostic (Blog.Sql via generated Tables);
/// this is the app's glue naming the app's own composition.

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
    ORDER BY (SELECT COUNT(*) FROM blog_comments c WHERE c.identity_id = i.id) DESC, i.created_at ASC
    LIMIT 1"""

/// Fold one guest's identities into another (their comments follow, since
/// comments are attributed to the identity, not the guest).
let moveIdentitiesToGuest =
    "UPDATE identities SET guest_id = ? WHERE guest_id = ?"

let countCommentsForIdentity =
    "SELECT COUNT(*) AS n FROM blog_comments WHERE identity_id = ?"

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

// ---- Attribution (see Server.Attribution) ----

let reassignComments = """
    UPDATE blog_comments
    SET identity_id = ?, author = (SELECT name FROM identities WHERE id = ?)
    WHERE identity_id = ?"""

// ---- darwin.news glue over the blog module's content tables ----

/// Just the columns social previews need (Server.Meta). Deliberately narrow so it
/// stays valid on branches that add item columns.
let itemMetaBySlugOrId =
    "SELECT id, title, image, extract, slug FROM blog_items WHERE (slug = ? OR id = ?) AND deleted_at IS NULL"

// rhyme-* tags in numeric-ish order (rhyme-1, rhyme-2, ...) for rhyming.darwin.news.
let rhymeTags =
    "SELECT name FROM blog_tags WHERE name LIKE 'rhyme-%' ORDER BY name"
