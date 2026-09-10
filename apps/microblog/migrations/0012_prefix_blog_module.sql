-- Converge the microblog onto the shared `blog` module: the content tables become
-- the blog module's prefixed tables. Columns are unchanged (the blog module's
-- Domain matches the microblog's), so this is a pure rename — the app now composes
-- [identity, blog] with identity (guests/identities) staying unprefixed.
--
-- SQLite auto-updates FK references and index/trigger definitions across a RENAME
-- (3.25+, as on D1), so blog_comments/blog_item_tags keep pointing at the renamed
-- parents. This is also the per-tenant live migration (darwin.news first).

ALTER TABLE items RENAME TO blog_items;
ALTER TABLE comments RENAME TO blog_comments;
ALTER TABLE tags RENAME TO blog_tags;
ALTER TABLE item_tags RENAME TO blog_item_tags;
