module Blog.Sql

/// The blog module's hand-written SQL. Table names come from the generated
/// `Server.Db.Tables` constants, so the same statements work whether the module
/// is mounted standalone (`items`) or prefixed in a host (`blog_items`). This is
/// the "module SQL hygiene" cost: a module must never hardcode a table name.
/// (`identities` stays unprefixed — it's the shared, app-level table.)

open Server.Db

let itemBySlug =
    sprintf "SELECT id, title, link, image, extract, owner_comment, slug, created_at, updated_at, view_count, deleted_at FROM %s WHERE slug = ? AND deleted_at IS NULL" Tables.item

// Cursor-paginated feed (infinite scroll). Ordered by (article_date, id) so the
// compound cursor is stable even when many items share a date, and so backdated
// articles sort to their own date rather than their insert time. Bind: [limit].
let feedFirstPage =
    sprintf "SELECT id, title, link, image, extract, owner_comment, article_date, slug, created_at, updated_at, view_count, deleted_at FROM %s WHERE deleted_at IS NULL ORDER BY article_date DESC, id DESC LIMIT ?" Tables.item

// Bind: [cursorTs, cursorTs, cursorId, limit].
let feedAfterCursor =
    sprintf "SELECT id, title, link, image, extract, owner_comment, article_date, slug, created_at, updated_at, view_count, deleted_at FROM %s WHERE deleted_at IS NULL AND (article_date < ? OR (article_date = ? AND id < ?)) ORDER BY article_date DESC, id DESC LIMIT ?" Tables.item

let tagsForItem =
    sprintf "SELECT t.name FROM %s t JOIN %s it ON t.id = it.tag_id WHERE it.item_id = ?" Tables.tag Tables.itemTag

let picturesForItemComments =
    sprintf "SELECT DISTINCT i.id, i.picture FROM identities i JOIN %s c ON c.identity_id = i.id WHERE c.item_id = ?" Tables.itemComment

let insertComment =
    sprintf "INSERT INTO %s (id, item_id, identity_id, parent_id, author, content, removed, created_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?)" Tables.itemComment

let tagNames =
    sprintf "SELECT name FROM %s ORDER BY name" Tables.tag

// Cursor-paginated tag feed (bind: [tag, limit]).
let itemsByTag =
    sprintf "SELECT i.* FROM %s i JOIN %s it ON i.id = it.item_id JOIN %s t ON it.tag_id = t.id WHERE t.name = ? AND i.deleted_at IS NULL ORDER BY i.article_date DESC, i.id DESC LIMIT ?" Tables.item Tables.itemTag Tables.tag

// Bind: [tag, cursorTs, cursorTs, cursorId, limit].
let itemsByTagAfter =
    sprintf "SELECT i.* FROM %s i JOIN %s it ON i.id = it.item_id JOIN %s t ON it.tag_id = t.id WHERE t.name = ? AND i.deleted_at IS NULL AND (i.article_date < ? OR (i.article_date = ? AND i.id < ?)) ORDER BY i.article_date DESC, i.id DESC LIMIT ?" Tables.item Tables.itemTag Tables.tag

let insertTag =
    sprintf "INSERT OR IGNORE INTO %s (id, name, created_at) VALUES (?, ?, ?)" Tables.tag

// Bind: [linkId, itemId, tagName]. linkId is the surrogate PK (see ItemTag).
let linkItemTag =
    sprintf "INSERT INTO %s (id, item_id, tag_id) SELECT ?, ?, id FROM %s WHERE name = ?" Tables.itemTag Tables.tag
