-- Auto-generated migration for site: default (db: hedge-db)

-- New table: blog_snapshots
CREATE TABLE blog_snapshots (
    id TEXT PRIMARY KEY,
    item_id TEXT NOT NULL,
    kind TEXT NOT NULL,
    blob_key TEXT NOT NULL,
    source_url TEXT NOT NULL,
    status TEXT NOT NULL,
    error TEXT,
    created_at INTEGER NOT NULL,
    deleted_at INTEGER,
    FOREIGN KEY (item_id) REFERENCES blog_items(id)
);
CREATE INDEX idx_blog_snapshots_item_id ON blog_snapshots(item_id);
CREATE INDEX idx_blog_snapshots_created_at ON blog_snapshots(created_at DESC);
