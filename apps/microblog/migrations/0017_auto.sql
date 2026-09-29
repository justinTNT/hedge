-- Auto-generated migration for site: default (db: hedge-db)

-- Changes to identities
ALTER TABLE identities ADD COLUMN superseded_by TEXT;
