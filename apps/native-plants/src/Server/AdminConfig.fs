module Server.AdminConfig
open Hedge.Workers
open Hedge.Admin
open Server.Env
let adminConfig : AdminConfig<Env> =
    { Tables = Server.AdminGen.tables
      GetDb = fun env -> env.DB
      CheckKey = fun request env ->
          let key = getHeader request "X-Admin-Key"
          key <> "" && key = env.ADMIN_KEY }
