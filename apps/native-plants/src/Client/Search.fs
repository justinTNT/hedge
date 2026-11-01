module Client.Search

// Descriptive search input: the plant accounts the read cache holds, in scientific-name order, with the
// bibliography left out. No request is made; the warm-up keeps the accounts current with the catalogue.

open Fable.Core
open Thoth.Json

/// Prime the per-account read cache from one bulk response, so every plant page opens offline after a
/// single request and the search, packs and pages all read the same saved accounts.
let prime (accounts: Models.Api.GetAccounts.Response) : JS.Promise<unit> = promise {
    for plant in accounts.Plants do
        let body = Hedge.Codec.encode ({ Plant = plant }: Models.Api.GetPlant.Response) |> Encode.toString 0
        do! Client.Offline.primeRead Client.Storage.store "" (Client.ClientGen.requests.getPlant plant.Card.Id) { Status = 200; Headers = []; Body = body }
}

/// The saved accounts as (plant id, searchable sections), sorted by scientific name.
let load () : JS.Promise<(string * Models.Api.Section list) list> = promise {
    let! cached = Client.Offline.cachedResponses Client.Storage.store "" "/api/plants/plant/"
    return
        cached
        |> List.choose (fun (_, response, _) -> Decode.fromString Codecs.Decode.getPlantResponse response.Body |> Result.toOption)
        |> List.map (fun r -> NativePlants.Catalogue.normalize r.Plant.Card.ScientificName, r.Plant.Card.Id, r.Plant.Sections |> List.filter (fun s -> NativePlants.Descriptions.searchable s.Label))
        |> List.sortBy (fun (name, id, _) -> name, id)
        |> List.map (fun (_, id, sections) -> id, sections)
}
