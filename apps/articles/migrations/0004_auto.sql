-- Auto-generated migration for site: articles (identities superseded_by column)

-- Changes to identities
ALTER TABLE identities ADD COLUMN superseded_by TEXT;
