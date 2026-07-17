module Server.Attribution

open Fable.Core
open Hedge.Workers

/// Reassign everything attributed to one identity onto another.
/// This list IS the merge policy: every table that attributes rows to an
/// identity gets a statement here, and nowhere else. A new attributed
/// domain type (e.g. calendar entries) adds one line.
let reassign (db: D1Database) (fromId: string) (toId: string) : JS.Promise<unit> =
    promise {
        let! _ =
            db.batch [|
                bind (db.prepare Sql.reassignComments) [| box toId; box toId; box fromId |]
            |]
        return ()
    }
