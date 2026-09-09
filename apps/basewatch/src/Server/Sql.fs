module Server.Sql

/// Hand-written SQL for basewatch — a read-only pages site.

/// The whole nav tree (it's tiny) — client assembles the hierarchy.
let listMenu = """
    SELECT item, title, link, parent_item, ordinal
    FROM menu_items
    WHERE deleted_at IS NULL
    ORDER BY parent_item, ordinal"""

/// One page by its slug (Name). Bind: [name].
let pageByName = """
    SELECT id, name, title, teaser, body, created_at, updated_at, deleted_at
    FROM pages
    WHERE name = ? AND deleted_at IS NULL"""
