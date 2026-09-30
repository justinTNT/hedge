-- Additive: no catalogue or contribution rows are replaced. Apply once to pre-mobile databases.
ALTER TABLE identities ADD COLUMN superseded_by TEXT;

CREATE TABLE mobile_sessions (
    id TEXT PRIMARY KEY,
    guest_id TEXT NOT NULL,
    expires_at INTEGER NOT NULL,
    created_at INTEGER NOT NULL
);

CREATE TABLE mobile_auth_codes (
    id TEXT PRIMARY KEY,
    guest_id TEXT NOT NULL,
    challenge TEXT NOT NULL,
    expires_at INTEGER NOT NULL,
    created_at INTEGER NOT NULL
);

CREATE INDEX idx_mobile_sessions_created_at ON mobile_sessions(created_at DESC);
CREATE INDEX idx_mobile_auth_codes_created_at ON mobile_auth_codes(created_at DESC);
