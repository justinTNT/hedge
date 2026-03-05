-- Repoint item_tags to the earliest tag for each name
UPDATE item_tags SET tag_id = (
    SELECT t2.id FROM tags t2
    WHERE t2.name = (SELECT name FROM tags WHERE id = item_tags.tag_id)
    ORDER BY t2.created_at ASC LIMIT 1
);

-- Remove duplicate item_tags (same item_id + tag_id)
DELETE FROM item_tags WHERE rowid NOT IN (
    SELECT MIN(rowid) FROM item_tags GROUP BY item_id, tag_id
);

-- Remove duplicate tags (keep earliest per name)
DELETE FROM tags WHERE rowid NOT IN (
    SELECT MIN(rowid) FROM tags GROUP BY name
);

CREATE UNIQUE INDEX idx_tags_name ON tags(name);
