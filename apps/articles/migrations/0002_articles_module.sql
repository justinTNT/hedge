-- Converge justat/ndct onto the extracted `articles` module (D5, notes/MODULES.md).
-- Renames the transitional root-module tables to the module's prefixed names:
--   articles -> articles_posts, comments -> articles_comments, article_id -> post_id.
-- Pure metadata renames (no data copy); modern SQLite/D1 updates the FK + index refs
-- automatically. The parent_id self-FK in schema.sql is SCHEMA-ONLY (fresh deploys):
-- SQLite can't ALTER ADD CONSTRAINT, so it is deliberately NOT added to existing rows.
-- blog_* / identities / guests are untouched.
ALTER TABLE articles RENAME TO articles_posts;
ALTER TABLE comments RENAME TO articles_comments;
ALTER TABLE articles_comments RENAME COLUMN article_id TO post_id;
