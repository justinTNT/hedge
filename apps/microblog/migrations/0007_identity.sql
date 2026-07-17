-- Progressive Identification: add identities table, migrate comments from guest_id to identity_id

-- 1. Create identities table
CREATE TABLE identities (
    id TEXT PRIMARY KEY,
    guest_id TEXT NOT NULL,
    provider TEXT NOT NULL DEFAULT 'anonymous',
    provider_user_id TEXT NOT NULL DEFAULT '',
    name TEXT NOT NULL,
    picture TEXT NOT NULL DEFAULT '',
    email TEXT,
    activated_at INTEGER,
    created_at INTEGER NOT NULL,
    FOREIGN KEY (guest_id) REFERENCES guests(id)
);

CREATE INDEX idx_identities_guest_id ON identities(guest_id);
CREATE INDEX idx_identities_created_at ON identities(created_at DESC);

-- 2. Populate anonymous identities from existing guests
--    Each guest gets one anonymous identity, activated at their creation time
INSERT INTO identities (id, guest_id, provider, provider_user_id, name, picture, email, activated_at, created_at)
SELECT
    'ident-' || id,
    id,
    'anonymous',
    '',
    COALESCE(name, 'Anonymous'),
    COALESCE(picture, ''),
    NULL,
    created_at,
    created_at
FROM guests;

-- 3. Add identity_id column to comments, populate from guest_id
ALTER TABLE comments ADD COLUMN identity_id TEXT REFERENCES identities(id);
UPDATE comments SET identity_id = 'ident-' || guest_id;
CREATE INDEX idx_comments_identity_id ON comments(identity_id);

-- 4. Drop guest_sessions (no longer needed — display info lives on Identity)
DROP TABLE IF EXISTS guest_sessions;

-- 5. Slim guests: name and picture columns left in place (harmless, no longer written to)
