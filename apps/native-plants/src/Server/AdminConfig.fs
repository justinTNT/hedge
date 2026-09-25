module Server.AdminConfig
open Fable.Core
open Hedge.Workers
open Hedge.Admin
open Server.Env

// Registration is an app decision; new generated tables are never exposed implicitly.
let private tables = [
    Server.AdminGen.plant
    Server.AdminGen.plantPhoto
    Server.AdminGen.plantMap
    Server.AdminGen.glossaryTerm
    Server.AdminGen.sourceReference
    Server.AdminGen.grant
    { Server.AdminGen.identity with SupportedOps = [OpList; OpRead] }
]

let adminConfig : AdminConfig<Env> =
    { Tables = tables
      GetDb = fun env -> env.DB
      Authorize = fun request env -> promise {
          return
              if Server.AuthConfig.isOwner env request then AdminOwner
              else AdminAnonymous None
      } }
