module Server.AttributionPolicy

/// The merge policy: one comment re-attribution statement per composed content module,
/// each owned by the module that owns its comment table (Tables-driven). Microblog composes
/// only the blog module, so these are single-element lists — but the SAME seam Articles uses,
/// so the identity handlers are one implementation across hosts. Compiled after the blog
/// module server props (needs Blog.Sql + Blog.Db), before Handlers.
let reassignStatements : string list =
    [ Blog.Sql.reassignComments ]

/// The content-comment tables this site composes. Used to sum an identity's comment history
/// (see Attribution's countCommentsSql / findByProviderGlobalSql).
let commentTables : string list =
    [ Blog.Db.Tables.itemComment ]
