module Server.AdminConfig

open Fable.Core
open Hedge.Workers
open Hedge.Admin
open Server.Env

/// Everything admin-generic — the AdminTable type and the schema-driven CRUD — lives in Hedge.Admin.
/// This module wires the app in: which tables, how to reach D1, and how to AUTHORIZE a request. Owner
/// (ADMIN_KEY) gets full access; otherwise a delegated role is resolved through the shared
/// access-control policy (Server.AccessConfig: guest cookie -> active identity -> enabled grant). The
/// role NAMES and the permission MATRIX are this app's policy, not the framework's — the framework only
/// enforces the (resource, operation) decision this yields. Curator WRITE workflows are NOT here: they
/// run through the alerts module's own state-gated curation endpoints (/api/alerts/curation/*),
/// authorized the same way — the generic whole-row CRUD would bypass the approved-entry freeze.

/// idealist's delegated matrix: a "curator" may VIEW the alerts pipeline in the generic admin
/// (read-only) and nothing else. The identity/grant tables and every write stay owner-only. (On
/// tenants without curators this simply never grants — no curator grant exists.)
let private curatorReadable = set [ "AlertSource"; "PendingPost"; "Promotion" ]
let private curatorPermits (resource: string) (op: AdminOp) : bool =
    Set.contains resource curatorReadable && (op = OpList || op = OpRead)

let adminConfig : AdminConfig<Env> =
    { Tables = Server.AdminGen.tables
      GetDb = fun env -> env.DB
      Authorize = fun request env -> promise {
          let key = getHeader request "X-Admin-Key"
          if key <> "" && key = env.ADMIN_KEY then return AdminOwner
          else
              // No owner key → resolve a delegated role from the guest cookie + grants. A curator gets
              // the bounded read matrix; an authenticated non-curator is forbidden (403 everywhere); no
              // acceptable session is unauthenticated (401). Any guest-cookie renewal rides along.
              let! r = Server.AccessConfig.authorize env "curator" request
              match r with
              | Hedge.AccessControl.Authorized (_, repl) -> return AdminSubject (curatorPermits, repl)
              | Hedge.AccessControl.Forbidden (_, repl) -> return AdminSubject ((fun _ _ -> false), repl)
              | Hedge.AccessControl.AuthRequired repl -> return AdminAnonymous repl } }
