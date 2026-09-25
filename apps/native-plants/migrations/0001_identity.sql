-- Add shared identity without touching botanical records or imported media.
CREATE TABLE guests (
    id TEXT PRIMARY KEY,
    session_id TEXT NOT NULL,
    created_at INTEGER NOT NULL,
    deleted_at INTEGER
);

CREATE TABLE identities (
    id TEXT PRIMARY KEY,
    guest_id TEXT NOT NULL,
    provider TEXT NOT NULL,
    provider_user_id TEXT NOT NULL,
    name TEXT NOT NULL,
    picture TEXT NOT NULL,
    email TEXT,
    activated_at INTEGER,
    created_at INTEGER NOT NULL,
    FOREIGN KEY (guest_id) REFERENCES guests(id)
);

CREATE INDEX idx_guests_created_at ON guests(created_at DESC);

CREATE INDEX idx_identities_guest_id ON identities(guest_id);

CREATE INDEX idx_identities_created_at ON identities(created_at DESC);
