-- Independent article date: drives display/sort/grouping, editable and separate
-- from created_at/updated_at. Backfill existing rows from created_at so their
-- current dates are preserved.
ALTER TABLE items ADD COLUMN article_date INTEGER NOT NULL DEFAULT 0;
UPDATE items SET article_date = created_at;
CREATE INDEX idx_items_article_date ON items(article_date DESC);
