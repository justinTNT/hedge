ALTER TABLE items ADD COLUMN slug TEXT;
CREATE UNIQUE INDEX idx_items_slug ON items(slug) WHERE slug IS NOT NULL;
