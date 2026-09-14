module Server.AttributionPolicy

/// The merge policy: one comment re-attribution statement per composed content module,
/// each owned by the module that owns its comment table (Tables-driven). This is the
/// per-site composition — the superset for every site that hosts both modules (justat /
/// default); ndct has its own articles-only variant (AttributionPolicy.ndct.fs). Same
/// shape as the generated `Server.AdminGen.tables` registry (`own @ each module`).
/// Compiled after the module server props (needs Articles.Sql + Blog.Sql), before Handlers.
let reassignStatements : string list =
    [ Articles.Sql.reassignComments
      Blog.Sql.reassignComments ]

/// The content-comment tables this site composes, in the same per-site spirit as above.
/// Used to sum an identity's comment history across ALL its content (see Attribution's
/// countCommentsSql / findByProviderGlobalSql) — so ranking + counts aren't articles-only.
let commentTables : string list =
    [ Articles.Db.Tables.comment
      Blog.Db.Tables.itemComment ]
