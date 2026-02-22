-- Add guests table and deleted_at to item_tags
CREATE TABLE IF NOT EXISTS guests (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    picture TEXT NOT NULL DEFAULT '',
    session_id TEXT NOT NULL,
    created_at INTEGER NOT NULL,
    deleted_at INTEGER
);

ALTER TABLE item_tags ADD COLUMN deleted_at INTEGER;
