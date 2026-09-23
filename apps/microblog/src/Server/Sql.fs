module Server.Sql

/// The app's hand-written SQL that is neither shared identity persistence nor grant lookups (both now
/// module-owned: Identity.Sql and Identity.Grants): just the two darwin.news glue statements (rhyming
/// + social-preview) that read the composed blog module's content tables.
///
/// check-sql.sh EXPLAIN-prepares each of these against both a fresh schema.sql database and a
/// migrations-built database at test time — keep statements as plain literals (no concatenation).

// ---- darwin.news glue over the blog module's content tables ----

/// Just the columns social previews need (Server.Meta). Deliberately narrow so it
/// stays valid on branches that add item columns.
let itemMetaBySlugOrId =
    "SELECT id, title, image, extract, slug FROM blog_items WHERE (slug = ? OR id = ?) AND deleted_at IS NULL"

// rhyme-* tags in numeric-ish order (rhyme-1, rhyme-2, ...) for rhyming.darwin.news.
let rhymeTags =
    "SELECT name FROM blog_tags WHERE name LIKE 'rhyme-%' ORDER BY name"
