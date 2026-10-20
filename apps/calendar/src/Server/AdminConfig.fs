module Server.AdminConfig

open Hedge.Workers
open Hedge.Admin
open Server.Env

/// Everything admin-generic - the AdminTable type and the schema-driven CRUD -
/// lives in Hedge.Admin. This module just wires the app in: which tables (from
/// generated AdminGen), how to reach the D1 database, and how to authorise a
/// request against this app's admin key.
let adminConfig : AdminConfig<Env> =
    { Tables = Server.AdminGen.tables
      GetDb = fun env -> env.DB
      // Owner-key-only authorization (the default). Apps that add delegated roles supply a richer
      // Authorize returning Hedge.Admin.AdminSubject with a per-(resource, operation) permits matrix.
      Authorize = Hedge.Admin.ownerKey (fun request env ->
        let key = getHeader request "X-Admin-Key"
        key <> "" && key = env.ADMIN_KEY) }
