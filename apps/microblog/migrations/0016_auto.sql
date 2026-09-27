-- Auto-generated migration for site: default (db: hedge-db)

-- New table: mobile_auth_codes
CREATE TABLE mobile_auth_codes (
    id TEXT PRIMARY KEY,
    guest_id TEXT NOT NULL,
    challenge TEXT NOT NULL,
    expires_at INTEGER NOT NULL,
    created_at INTEGER NOT NULL
);
CREATE INDEX idx_mobile_auth_codes_created_at ON mobile_auth_codes(created_at DESC);
