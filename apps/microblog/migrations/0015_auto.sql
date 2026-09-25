-- Auto-generated migration for site: default (db: hedge-db)

-- New table: mobile_sessions
CREATE TABLE mobile_sessions (
    id TEXT PRIMARY KEY,
    guest_id TEXT NOT NULL,
    expires_at INTEGER NOT NULL,
    created_at INTEGER NOT NULL
);
CREATE INDEX idx_mobile_sessions_created_at ON mobile_sessions(created_at DESC);
