module Server.Attribution

open Fable.Core
open Hedge.Workers

/// Reassign everything attributed to one identity onto another. `statements` IS the
/// merge policy: one re-attribution statement per content-comment table, each owned by
/// the module that owns the table and composed in `Server.AttributionPolicy` (microblog
/// composes only blog). Every statement shares the bind shape [toId; toId; fromId] (see
/// each module's `reassignComments`), so one batch with one bind array reassigns them all
/// atomically.
let reassign (db: D1Database) (statements: string list) (fromId: string) (toId: string) : JS.Promise<unit> =
    promise {
        match statements with
        | [] -> return ()
        | _ ->
            let! _ =
                db.batch [| for sql in statements -> bind (db.prepare sql) [| box toId; box toId; box fromId |] |]
            return ()
    }

// An identity's comment history spans every content module the site composes, so these
// sum across all of `AttributionPolicy.commentTables` rather than a single table. The
// table names come from that list (not user input), so interpolating them is safe;
// identity ids stay parameterised.

/// Total comments attributed to one identity across all content tables. Aliased `n`.
/// Bind: the identity id once per table (`[for _ in tables -> id]`).
let countCommentsSql (tables: string list) : string =
    let terms = tables |> List.map (fun t -> sprintf "(SELECT COUNT(*) FROM %s WHERE identity_id = ?)" t)
    sprintf "SELECT (%s) AS n" (String.concat " + " terms)

/// The provider account (any guest) with the most comment history, across all content
/// tables; earliest created breaks ties. The COUNT subqueries correlate on `i.id`, so the
/// bind is unchanged: [provider; providerUserId].
let findByProviderGlobalSql (tables: string list) : string =
    let terms = tables |> List.map (fun t -> sprintf "(SELECT COUNT(*) FROM %s c WHERE c.identity_id = i.id)" t)
    sprintf
        "SELECT i.id, i.guest_id FROM identities i WHERE i.provider = ? AND i.provider_user_id = ? ORDER BY (%s) DESC, i.created_at ASC LIMIT 1"
        (String.concat " + " terms)
