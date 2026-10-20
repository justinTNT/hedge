module Server.AttributionPolicy

// What follows an identity when two of a guest's identities merge (Identity.Attribution.reassign):
// this host composes no comments, but the calendar module attributes a user's entries to an
// identity, so they move with it.

let reassignStatements : string list =
    [ Calendar.Sql.reassignOwner ]

/// Tables the merge ranks duplicate identities by (COUNT of rows per `identity_id`).
let commentTables : string list =
    [ Calendar.Db.Tables.entry ]
