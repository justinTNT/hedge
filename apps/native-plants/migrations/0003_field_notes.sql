-- Existing private notes and corrections retain their purpose and text.
-- Attachment IDs are a bounded JSON list, atomically replaced with a note revision.
ALTER TABLE plant_notes ADD COLUMN purpose TEXT;
ALTER TABLE plant_notes ADD COLUMN photo_ids TEXT;

CREATE TABLE identification_responses (
    id TEXT PRIMARY KEY,
    note_id TEXT NOT NULL,
    note_revision INTEGER NOT NULL,
    submitted_text TEXT NOT NULL,
    outcome TEXT NOT NULL,
    text TEXT NOT NULL,
    alternative_plant_id TEXT,
    reviewer_provider TEXT NOT NULL,
    reviewer_id TEXT NOT NULL,
    reviewer_name TEXT NOT NULL,
    created_at INTEGER NOT NULL,
    FOREIGN KEY (note_id) REFERENCES plant_notes(id),
    FOREIGN KEY (alternative_plant_id) REFERENCES plants(id)
);

CREATE INDEX idx_identification_responses_note_id ON identification_responses(note_id);
CREATE INDEX idx_identification_responses_alternative_plant_id ON identification_responses(alternative_plant_id);
CREATE UNIQUE INDEX idx_identification_responses_note_id_note_revision ON identification_responses(note_id, note_revision);
CREATE INDEX idx_identification_responses_created_at ON identification_responses(created_at DESC);
