-- Add columns from Domain.fs that are missing from existing tables

-- tags: add created_at and deleted_at
ALTER TABLE tags ADD COLUMN created_at INTEGER NOT NULL DEFAULT 0;
ALTER TABLE tags ADD COLUMN deleted_at INTEGER;

-- comments: add removed flag and deleted_at
ALTER TABLE comments ADD COLUMN removed INTEGER NOT NULL DEFAULT 0;
ALTER TABLE comments ADD COLUMN deleted_at INTEGER;

-- items: add updated_at, view_count, deleted_at
ALTER TABLE items ADD COLUMN updated_at INTEGER;
ALTER TABLE items ADD COLUMN view_count INTEGER NOT NULL DEFAULT 0;
ALTER TABLE items ADD COLUMN deleted_at INTEGER;
