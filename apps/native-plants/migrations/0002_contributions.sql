-- Additive: private app content plus opt-in shared curator grants.

CREATE TABLE contribution_claims (
    id TEXT PRIMARY KEY,
    guest_id TEXT NOT NULL,
    created_at INTEGER NOT NULL
);

CREATE TABLE grants (
    id TEXT PRIMARY KEY,
    provider TEXT NOT NULL,
    provider_user_id TEXT NOT NULL,
    role TEXT NOT NULL,
    enabled INTEGER NOT NULL,
    granted_by TEXT,
    created_at INTEGER NOT NULL
);

CREATE TABLE personal_plant_photos (
    id TEXT PRIMARY KEY,
    plant_id TEXT NOT NULL,
    owner_provider TEXT NOT NULL,
    owner_id TEXT NOT NULL,
    image_key TEXT NOT NULL,
    thumbnail_key TEXT NOT NULL,
    width INTEGER NOT NULL,
    height INTEGER NOT NULL,
    stored_bytes INTEGER NOT NULL,
    caption TEXT NOT NULL,
    photographer TEXT NOT NULL,
    offered INTEGER NOT NULL,
    ready INTEGER NOT NULL,
    revision INTEGER NOT NULL,
    published_photo_id TEXT,
    created_at INTEGER NOT NULL,
    updated_at INTEGER,
    deleted_at INTEGER,
    FOREIGN KEY (plant_id) REFERENCES plants(id)
);

CREATE TABLE plant_notes (
    id TEXT PRIMARY KEY,
    plant_id TEXT NOT NULL,
    owner_provider TEXT NOT NULL,
    owner_id TEXT NOT NULL,
    text TEXT NOT NULL,
    is_correction INTEGER NOT NULL,
    revision INTEGER NOT NULL,
    reviewed_revision INTEGER,
    created_at INTEGER NOT NULL,
    updated_at INTEGER,
    deleted_at INTEGER,
    FOREIGN KEY (plant_id) REFERENCES plants(id)
);

CREATE TABLE plant_view_preferences (
    id TEXT PRIMARY KEY,
    owner_provider TEXT NOT NULL,
    owner_id TEXT NOT NULL,
    plant_id TEXT NOT NULL,
    hero_photo_id TEXT,
    FOREIGN KEY (plant_id) REFERENCES plants(id)
);

CREATE INDEX idx_contribution_claims_created_at ON contribution_claims(created_at DESC);

CREATE UNIQUE INDEX idx_contribution_claims_guest_id ON contribution_claims(guest_id);

CREATE INDEX idx_grants_created_at ON grants(created_at DESC);

CREATE UNIQUE INDEX idx_grants_provider_provider_user_id_role ON grants(provider, provider_user_id, role);

CREATE INDEX idx_personal_plant_photos_created_at ON personal_plant_photos(created_at DESC);

CREATE INDEX idx_personal_plant_photos_plant_id ON personal_plant_photos(plant_id);

CREATE INDEX idx_plant_notes_created_at ON plant_notes(created_at DESC);

CREATE INDEX idx_plant_notes_plant_id ON plant_notes(plant_id);

CREATE UNIQUE INDEX idx_plant_view_preferences_owner_provider_owner_id_plant_id ON plant_view_preferences(owner_provider, owner_id, plant_id);

CREATE INDEX idx_plant_view_preferences_plant_id ON plant_view_preferences(plant_id);
