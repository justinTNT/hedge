-- Alert monitoring: registered feeds + a curation queue for polled entries.

-- 1. Promotion idempotency key on items (nullable-unique: hand-authored items
--    are NULL — multiple NULLs allowed; a promoted item carries its source
--    entry key, so a second promotion of the same draft violates the index).
--    migrate:dry adds the column but not the index for an existing table, so
--    the CREATE UNIQUE INDEX is hand-added here to match schema.sql (from gen).
ALTER TABLE items ADD COLUMN origin_entry_key TEXT;
CREATE UNIQUE INDEX idx_items_origin_entry_key ON items(origin_entry_key);

-- 2. Registered alert feeds.
CREATE TABLE alert_sources (
    id TEXT PRIMARY KEY,
    topic TEXT NOT NULL,
    feed_url TEXT NOT NULL,
    enabled INTEGER NOT NULL,
    created_at INTEGER NOT NULL
);
CREATE UNIQUE INDEX idx_alert_sources_feed_url ON alert_sources(feed_url);
CREATE INDEX idx_alert_sources_created_at ON alert_sources(created_at DESC);

-- 3. Curation queue. Promotion is derived (entry_key in items.origin_entry_key);
--    rejected rows persist as tombstones so the unique entry_key blocks re-import.
CREATE TABLE pending_posts (
    id TEXT PRIMARY KEY,
    source_id TEXT NOT NULL,
    entry_key TEXT NOT NULL,
    title TEXT NOT NULL,
    link TEXT NOT NULL,
    snippet TEXT NOT NULL,
    published_at INTEGER NOT NULL,
    approved INTEGER NOT NULL,
    rejected INTEGER NOT NULL,
    owner_comment TEXT NOT NULL,
    created_at INTEGER NOT NULL,
    FOREIGN KEY (source_id) REFERENCES alert_sources(id)
);
CREATE INDEX idx_pending_posts_source_id ON pending_posts(source_id);
CREATE UNIQUE INDEX idx_pending_posts_entry_key ON pending_posts(entry_key);
CREATE INDEX idx_pending_posts_created_at ON pending_posts(created_at DESC);
