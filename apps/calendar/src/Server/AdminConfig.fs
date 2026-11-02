module Server.AdminConfig

open Hedge.Workers
open Hedge.Admin
open Server.Env

/// Explicit admin exposure: the entry stream with full CRUD (where the sitewide/community/art entries
/// are authored), the module's approval queue (Calendar.Admin: the same table, update narrowed to the
/// decision columns), identities read-only, guests not registered at all. Ceilings bind even the owner.
let adminConfig : AdminConfig<Env> =
    { Tables =
        [ Calendar.AdminGen.entry
          Calendar.Admin.approvalQueue
          { Server.AdminGen.identity with SupportedOps = [ OpList; OpRead ] } ]
      GetDb = fun env -> env.DB
      Authorize = Hedge.Admin.ownerKey (fun request env ->
        let key = getHeader request "X-Admin-Key"
        key <> "" && key = env.ADMIN_KEY) }
