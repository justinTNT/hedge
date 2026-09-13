module Articles.Sql

/// The articles module's hand-written SQL. Table names come from the generated
/// `Articles.Db.Tables` constants, so the same statements work whether the module is
/// mounted standalone (`posts`/`comments`) or prefixed in a host (`articles_posts`/
/// `articles_comments`). (`identities` stays unprefixed — the shared, app-level table.)

open Articles.Db

let postBySlug =
    sprintf "SELECT id, title, teaser, body, image, article_date, slug, created_at, updated_at, view_count, deleted_at FROM %s WHERE slug = ? AND deleted_at IS NULL" Tables.post

// Cursor-paginated feed (infinite scroll). Body is deliberately excluded — the list
// doesn't need it and essays can be large. Ordered by (article_date, id) so the
// compound cursor is stable even when many posts share a date, and so backdated
// posts sort to their own date rather than their insert time. Bind: [limit].
let feedFirstPage =
    sprintf "SELECT id, title, teaser, image, article_date, slug FROM %s WHERE deleted_at IS NULL ORDER BY article_date DESC, id DESC LIMIT ?" Tables.post

// Bind: [cursorTs, cursorTs, cursorId, limit].
let feedAfterCursor =
    sprintf "SELECT id, title, teaser, image, article_date, slug FROM %s WHERE deleted_at IS NULL AND (article_date < ? OR (article_date = ? AND id < ?)) ORDER BY article_date DESC, id DESC LIMIT ?" Tables.post

/// Just the columns social previews need (teaser stands in for description).
let postMetaBySlugOrId =
    sprintf "SELECT id, title, teaser, image, slug FROM %s WHERE (slug = ? OR id = ?) AND deleted_at IS NULL" Tables.post

let picturesForPostComments =
    sprintf "SELECT DISTINCT i.id, i.picture FROM identities i JOIN %s c ON c.identity_id = i.id WHERE c.post_id = ? AND c.deleted_at IS NULL" Tables.comment

let insertComment =
    sprintf "INSERT INTO %s (id, post_id, identity_id, parent_id, author, content, removed, created_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?)" Tables.comment
