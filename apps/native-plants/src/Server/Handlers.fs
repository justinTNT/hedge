module Server.Handlers
open Fable.Core
open Thoth.Json
open Hedge.Codec
open Hedge.Workers
open Hedge.Router
open Models.Api
open Server.Env

let inline private respond value = okJson (encode value |> Encode.toString 0)
let getCatalogue (env: Env) = promise {
    let! catalogue = CatalogueStore.get env
    return respond catalogue.Public
}
let getPlant (id: string) (env: Env) = promise {
    let! catalogue = CatalogueStore.get env
    match Map.tryFind id catalogue.ById with
    | Some plant -> return respond ({Plant=plant}: GetPlant.Response)
    | None -> return notFound ()
}
let searchPlants (query: SearchPlants.Query) (env: Env) = promise {
    let! catalogue = CatalogueStore.get env
    let plants = NativePlants.Catalogue.filter query catalogue.Public.Plants
    return respond ({Plants=plants;Total=plants.Length}: SearchPlants.Response)
}
let getRevision (env: Env) = promise {
    let! catalogue = CatalogueStore.get env
    return respond ({Revision=catalogue.Public.Revision}:GetRevision.Response)
}
