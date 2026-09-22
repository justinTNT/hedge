-- Auto-generated migration for site: default (db: hedge-db)

-- New table: grants
CREATE TABLE grants (
    id TEXT PRIMARY KEY,
    provider TEXT NOT NULL,
    provider_user_id TEXT NOT NULL,
    role TEXT NOT NULL,
    enabled INTEGER NOT NULL,
    granted_by TEXT,
    created_at INTEGER NOT NULL
);
CREATE UNIQUE INDEX idx_grants_provider_provider_user_id_role ON grants(provider, provider_user_id, role);
CREATE INDEX idx_grants_created_at ON grants(created_at DESC);
