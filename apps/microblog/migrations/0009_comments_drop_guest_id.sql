-- Migration 0007 added comments.identity_id but left the pre-identity guest_id
-- column in place, still NOT NULL with no default. The identity-era insert
-- (id, item_id, identity_id, parent_id, author, content, removed, created_at)
-- never supplies it, so every new comment violates the constraint and the
-- worker throws — a 500 on POST /api/comment with a Cloudflare HTML error page
-- rather than the app's JSON. Reads keep working, which makes it look like a
-- handler bug rather than a schema one.
--
-- SQLite can't drop a column that participates in a table constraint, so
-- rebuild to the golden schema (see schema.sql).
--
-- Safe on a database whose comments table was already rebuilt by hand: every
-- copied column exists in both the legacy and golden shapes, so this is then
-- just a shape-preserving rebuild.

CREATE TABLE comments_rebuilt (
    id TEXT PRIMARY KEY,
    item_id TEXT NOT NULL,
    identity_id TEXT NOT NULL,
    parent_id TEXT,
    author TEXT NOT NULL,
    content TEXT NOT NULL,
    removed INTEGER NOT NULL,
    created_at INTEGER NOT NULL,
    deleted_at INTEGER,
    FOREIGN KEY (item_id) REFERENCES items(id),
    FOREIGN KEY (identity_id) REFERENCES identities(id)
);

-- No WHERE filter: 0007 backfills identity_id for every row, so a NULL here
-- means that backfill missed something. Better to fail loudly than to drop
-- comments silently.
INSERT INTO comments_rebuilt
    (id, item_id, identity_id, parent_id, author, content, removed, created_at, deleted_at)
SELECT id, item_id, identity_id, parent_id, author, content, removed, created_at, deleted_at
FROM comments;

DROP TABLE comments;
ALTER TABLE comments_rebuilt RENAME TO comments;

CREATE INDEX idx_comments_item_id ON comments(item_id);
CREATE INDEX idx_comments_identity_id ON comments(identity_id);
CREATE INDEX idx_comments_created_at ON comments(created_at DESC);
