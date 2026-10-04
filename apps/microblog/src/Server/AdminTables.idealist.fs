module Server.AdminTables

// Idealist composition: the common registry plus the alerts pipeline descriptors (owner full CRUD; a
// curator gets read-only visibility via Server.AdminConfig's permission matrix). Selected by
// HEDGE_SITE=idealist in Server.fsproj.

let tables =
    Server.AdminTablesCommon.tables @ [
        Alerts.AdminGen.alertSource
        Alerts.AdminGen.pendingPost
        Alerts.AdminGen.promotion
    ]
