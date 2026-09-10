-- Blog module tables for justat-db (additive; the articles tables are untouched).
--
-- Applied directly against the live DB, matching how justat-db was seeded from
-- schema.sql (articles never used the migrations-apply/d1_migrations flow):
--   npx wrangler d1 execute justat-db --remote --file=migrations/0001_blog_module.sql
--
-- These are the blog_* slice of the composed schema.sql; blog_comments and
-- blog_item_tags FK the shared, already-present identities/tables. FK-ordered so
-- a fresh run succeeds in one pass.

CREATE TABLE blog_items (
    id TEXT PRIMARY KEY,
    title TEXT NOT NULL,
    link TEXT,
    image TEXT,
    extract TEXT,
    owner_comment TEXT NOT NULL,
    article_date INTEGER NOT NULL,
    slug TEXT,
    created_at INTEGER NOT NULL,
    updated_at INTEGER,
    view_count INTEGER NOT NULL,
    deleted_at INTEGER
);
CREATE INDEX idx_blog_items_created_at ON blog_items(created_at DESC);

CREATE TABLE blog_comments (
    id TEXT PRIMARY KEY,
    item_id TEXT NOT NULL,
    identity_id TEXT NOT NULL,
    parent_id TEXT,
    author TEXT NOT NULL,
    content TEXT NOT NULL,
    removed INTEGER NOT NULL,
    created_at INTEGER NOT NULL,
    deleted_at INTEGER,
    FOREIGN KEY (item_id) REFERENCES blog_items(id),
    FOREIGN KEY (identity_id) REFERENCES identities(id)
);
CREATE INDEX idx_blog_comments_item_id ON blog_comments(item_id);
CREATE INDEX idx_blog_comments_identity_id ON blog_comments(identity_id);
CREATE INDEX idx_blog_comments_created_at ON blog_comments(created_at DESC);

CREATE TABLE blog_tags (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    created_at INTEGER NOT NULL,
    deleted_at INTEGER
);
CREATE UNIQUE INDEX idx_blog_tags_name ON blog_tags(name);
CREATE INDEX idx_blog_tags_created_at ON blog_tags(created_at DESC);

CREATE TABLE blog_item_tags (
    id TEXT PRIMARY KEY,
    item_id TEXT NOT NULL,
    tag_id TEXT NOT NULL,
    deleted_at INTEGER,
    FOREIGN KEY (item_id) REFERENCES blog_items(id),
    FOREIGN KEY (tag_id) REFERENCES blog_tags(id)
);
CREATE INDEX idx_blog_item_tags_item_id ON blog_item_tags(item_id);
CREATE INDEX idx_blog_item_tags_tag_id ON blog_item_tags(tag_id);
