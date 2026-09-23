module Identity.Attribution

// The identity attribution MECHANISM (previously duplicated byte-for-byte as each host's
// Server.Attribution). The POLICY — which comment tables / reassign statements a site composes —
// stays host-side (Server.AttributionPolicy, which names content modules) and is passed in as data
// (a string list) by the host when it builds Identity.Handlers deps. So this module names no content
// table and depends only on Hedge.

open Fable.Core
open Hedge.Workers

/// Reassign everything attributed to one identity onto another. `statements` IS the merge policy:
/// one re-attribution statement per content-comment table, each owned by the module that owns the
/// table and composed in the host's Server.AttributionPolicy. Every statement shares the bind shape
/// [toId; toId; fromId] (see each module's `reassignComments`), so one batch with one bind array
/// reassigns them all atomically.
let reassign (db: D1Database) (statements: string list) (fromId: string) (toId: string) : JS.Promise<unit> =
    promise {
        match statements with
        | [] -> return ()
        | _ ->
            let! _ =
                db.batch [| for sql in statements -> bind (db.prepare sql) [| box toId; box toId; box fromId |] |]
            return ()
    }

// An identity's comment history spans every content module the site composes, so these sum across all
// of the host's commentTables rather than a single table. The table names come from that list (not
// user input), so interpolating them is safe; identity ids stay parameterised.

/// Total comments attributed to one identity across all content tables. Aliased `n`.
/// Bind: the identity id once per table (`[for _ in tables -> id]`). An identity host that composes
/// NO comment subsystem (e.g. Native Plants: identity only) has an empty table set — then there are
/// zero comments and the sum must be a literal `0`, not `SELECT ()` (which SQLite rejects). The bind
/// is correspondingly empty (`[for _ in [] -> id]` = `[]`).
let countCommentsSql (tables: string list) : string =
    match tables with
    | [] -> "SELECT 0 AS n"
    | _ ->
        let terms = tables |> List.map (fun t -> sprintf "(SELECT COUNT(*) FROM %s WHERE identity_id = ?)" t)
        sprintf "SELECT (%s) AS n" (String.concat " + " terms)

/// The provider account (any guest) with the most comment history, across all content tables;
/// earliest created breaks ties. The COUNT subqueries correlate on `i.id`, so the bind is unchanged:
/// [provider; providerUserId]. With no comment tables there is no history to rank by, so drop the
/// `ORDER BY (…) DESC` count term (an empty `ORDER BY ()` is invalid SQL) and rank by age alone.
let findByProviderGlobalSql (tables: string list) : string =
    match tables with
    | [] ->
        "SELECT i.id, i.guest_id FROM identities i WHERE i.provider = ? AND i.provider_user_id = ? ORDER BY i.created_at ASC LIMIT 1"
    | _ ->
        let terms = tables |> List.map (fun t -> sprintf "(SELECT COUNT(*) FROM %s c WHERE c.identity_id = i.id)" t)
        sprintf
            "SELECT i.id, i.guest_id FROM identities i WHERE i.provider = ? AND i.provider_user_id = ? ORDER BY (%s) DESC, i.created_at ASC LIMIT 1"
            (String.concat " + " terms)
