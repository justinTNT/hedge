module Server.AttributionPolicy

/// ndct is articles-only (the blog module isn't composed — see Server.fsproj / the
/// gen-modules.ndct manifest), so the merge policy re-attributes just this app's
/// articles comments. Superset (articles + blog) lives in AttributionPolicy.fs.
let reassignStatements : string list =
    [ Articles.Sql.reassignComments ]

/// ndct composes only the articles comment table — see AttributionPolicy.fs.
let commentTables : string list =
    [ Articles.Db.Tables.comment ]
