module Server.AdminConfig

open Hedge.Workers
open Hedge.Admin
open Server.Env

/// The approval queue: a second admin resource over the module's entries table, listing users' entries
/// that asked for a public facet and are still personal. Approving = opening one and setting its
/// Category to the requested facet (community | art); clearing Requested declines it. The update is
/// narrowed to those two columns, so approving can never clobber the owner's content or ownership.
let private approvalQueue : AdminTable =
    { Calendar.AdminGen.entry with
        Name = "ApprovalQueue"
        SelectAll = Calendar.Sql.approvalQueue
        Update = Calendar.Sql.approve
        MutableFields = [ "Category"; "Requested" ]
        SupportedOps = [ OpList; OpRead; OpUpdate ] }

/// Explicit admin exposure: the entry stream with full CRUD (where the sitewide/community/art entries
/// are authored), the approval queue, identities read-only, guests not registered at all. Ceilings
/// bind even the owner.
let adminConfig : AdminConfig<Env> =
    { Tables =
        [ Calendar.AdminGen.entry
          approvalQueue
          { Server.AdminGen.identity with SupportedOps = [ OpList; OpRead ] } ]
      GetDb = fun env -> env.DB
      Authorize = Hedge.Admin.ownerKey (fun request env ->
        let key = getHeader request "X-Admin-Key"
        key <> "" && key = env.ADMIN_KEY) }
