-- Add a surrogate primary key to item_tags so the generic (single-id) admin can
-- create/edit tag links (the only way to tag an existing item). Reads join on
-- item_id/tag_id, never on id; pre-migration rows keep a NULL id and stay valid.
--
-- Hand-written, NOT auto-generated: `npm run migrate` wanted to rebuild items +
-- comments (drift on origin_entry_key / extra tables) and its items rebuild
-- omitted article_date (a NOT NULL column) — unsafe. This is the only change the
-- ItemTag model edit actually requires.
ALTER TABLE item_tags ADD COLUMN id TEXT;
