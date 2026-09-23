module Server.Sql

/// The app's hand-written SQL that is NOT shared identity persistence: the access-control grant
/// lookups (an opt-in sub-surface this app composes) plus the two darwin.news glue statements
/// (rhyming + social-preview) that read the composed blog module's content tables. The shared
/// identity layer moved to Identity.Sql (packages/modules/identity, via identity.server.props).
///
/// check-sql.sh EXPLAIN-prepares each of these against both a fresh schema.sql database and a
/// migrations-built database at test time — keep statements as plain literals (no concatenation).

// ---- Access control (curator role) ----

/// A guest row that is not soft-deleted. Access-control resolution honors guests.deleted_at so a
/// deleted guest's live session can't authorize (the guest-session cookie policy itself does not
/// check it).
let guestNotDeleted =
    "SELECT 1 FROM guests WHERE id = ? AND deleted_at IS NULL LIMIT 1"

/// Is there an ENABLED grant for this OAuth subject (provider, provider_user_id) + role? Absent or
/// disabled → no row → not authorized. Keyed on the pair (stable across identity merges).
let grantEnabled =
    "SELECT 1 FROM grants WHERE provider = ? AND provider_user_id = ? AND role = ? AND enabled = 1 LIMIT 1"

// ---- darwin.news glue over the blog module's content tables ----

/// Just the columns social previews need (Server.Meta). Deliberately narrow so it
/// stays valid on branches that add item columns.
let itemMetaBySlugOrId =
    "SELECT id, title, image, extract, slug FROM blog_items WHERE (slug = ? OR id = ?) AND deleted_at IS NULL"

// rhyme-* tags in numeric-ish order (rhyme-1, rhyme-2, ...) for rhyming.darwin.news.
let rhymeTags =
    "SELECT name FROM blog_tags WHERE name LIKE 'rhyme-%' ORDER BY name"
