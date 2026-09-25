module Server.AdminConfig
open Fable.Core
open Hedge.Workers
open Hedge.Admin
open Server.Env

let private tables =
    Server.AdminGen.tables
    |> List.filter (fun table -> not (List.contains table.Name ["Guest"; "PlantNote"; "PersonalPlantPhoto"; "PlantViewPreference"; "ContributionClaim"]))

// The owner needs provider-account identifiers to assign grants, but identity lifecycle
// changes must still go through the shared identity handlers, never generated CRUD.
let private ownerPermits resource operation =
    match resource with
    | "Identity" -> operation = OpList || operation = OpRead
    | _ -> tables |> List.exists (fun table -> table.Name = resource)

let adminConfig : AdminConfig<Env> =
    { Tables = tables
      GetDb = fun env -> env.DB
      Authorize = fun request env -> promise {
          return
              if Server.AuthConfig.isOwner env request then AdminSubject (ownerPermits, None)
              else AdminAnonymous None
      } }
