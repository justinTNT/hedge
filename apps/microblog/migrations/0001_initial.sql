-- Initial schema for Hedge (Horatio port)
-- D1 is SQLite: no JSONB, soft FK enforcement, TEXT for UUIDs

CREATE TABLE items (
    id TEXT PRIMARY KEY,
    title TEXT NOT NULL,
    link TEXT,
    image TEXT,
    extract TEXT,  -- Rich text JSON from TipTap
    owner_comment TEXT NOT NULL,
    created_at INTEGER NOT NULL
);

CREATE TABLE comments (
    id TEXT PRIMARY KEY,
    item_id TEXT NOT NULL,
    parent_id TEXT,  -- NULL for top-level comments
    author TEXT NOT NULL,
    content TEXT NOT NULL,  -- Rich text JSON
    created_at INTEGER NOT NULL,
    FOREIGN KEY (item_id) REFERENCES items(id),
    FOREIGN KEY (parent_id) REFERENCES comments(id)
);

CREATE TABLE tags (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL UNIQUE
);

CREATE TABLE item_tags (
    item_id TEXT NOT NULL,
    tag_id TEXT NOT NULL,
    PRIMARY KEY (item_id, tag_id),
    FOREIGN KEY (item_id) REFERENCES items(id),
    FOREIGN KEY (tag_id) REFERENCES tags(id)
);

CREATE TABLE guest_sessions (
    guest_id TEXT PRIMARY KEY,
    display_name TEXT NOT NULL,
    created_at INTEGER NOT NULL  -- Unix timestamp
);

-- Indexes for common queries
CREATE INDEX idx_comments_item_id ON comments(item_id);
CREATE INDEX idx_comments_parent_id ON comments(parent_id);
CREATE INDEX idx_item_tags_item_id ON item_tags(item_id);
CREATE INDEX idx_item_tags_tag_id ON item_tags(tag_id);
CREATE INDEX idx_items_created_at ON items(created_at DESC);
