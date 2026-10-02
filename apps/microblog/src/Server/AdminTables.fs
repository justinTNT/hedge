module Server.AdminTables

// Default composition (every tenant except idealist): just the common blog + identity/grant registry.
// Selected by HEDGE_SITE in Server.fsproj (this file for the default, AdminTables.idealist.fs for idealist).

let tables = Server.AdminTablesCommon.tables
