module Server.Items

open Hedge.Workers
open Server.Db

/// Item + tag creation mechanics shared by the submit handler and the alerts
/// cron. Returns unrun statements so the caller batches them (one transaction);
/// the UNIQUE-slug and origin_entry_key constraints surface as a batch rollback.
let createItemStmts (db: D1Database) (create: MicroblogItemCreate) (tags: string list) =
    let ins = insertMicroblogItem db create
    let tagStmts =
        tags |> List.collect (fun tagName ->
            [ bind (db.prepare Sql.insertTag) [| box (newId ()); box tagName; box ins.CreatedAt |]
              bind (db.prepare Sql.linkItemTag) [| box ins.Id; box tagName |] ])
    {| Stmts = ins.Stmt :: tagStmts |> List.toArray; Id = ins.Id; CreatedAt = ins.CreatedAt |}
