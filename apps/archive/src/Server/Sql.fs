module Server.Sql

/// Every hand-written SQL statement in the archive, named and in one place.
/// Read-only: front page, section indexes, single article, full-text search.

/// Newest articles overall — the front-page lead column. Includes body so the
/// handler can derive a teaser (small N). Bind: [limit].
let listRecent = """
    SELECT id, title, section, article_date, body
    FROM articles
    WHERE deleted_at IS NULL
    ORDER BY article_date DESC
    LIMIT ?"""

/// Newest in one section — the front-page section teasers (body for teaser).
/// Bind: [section, limit].
let listSectionRecent = """
    SELECT id, title, section, article_date, body
    FROM articles
    WHERE section = ? AND deleted_at IS NULL
    ORDER BY article_date DESC
    LIMIT ?"""

/// Every article in a section (stubs) — the chronological index. Bind: [section].
let listSectionAll = """
    SELECT id, title, section, article_date
    FROM articles
    WHERE section = ? AND deleted_at IS NULL
    ORDER BY article_date DESC"""

/// Per-section counts for the masthead / nav.
let countsBySection = """
    SELECT section, COUNT(*) AS n
    FROM articles
    WHERE deleted_at IS NULL
    GROUP BY section"""

/// Full-text search over title + body. Bind: [matchExpr, limit].
let searchFts = """
    SELECT id, section, article_date, title
    FROM articles_fts
    WHERE articles_fts MATCH ?
    ORDER BY rank
    LIMIT ?"""

let bumpViewCount = "UPDATE articles SET view_count = view_count + 1 WHERE id = ?"
