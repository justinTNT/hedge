module NativePlants.Catalogue

open Models.Api
open Models.Api.SearchPlants

let emptyQuery : Query =
    { Q=None; Family=None; Genus=None; Form=None; Sun=None; Water=None; Feature=None; Wildlife=None; Endemic=None; Photos=None; Photographer=None }

let normalize (s: string) = s.ToLowerInvariant().Trim().Replace("’", "'")
let hasPhotoCredit (credit:string) =
    not(System.String.IsNullOrWhiteSpace credit) && normalize credit <> "photographer not recorded"
let values (s: string) = s.Split('|') |> Array.map (fun x -> x.Trim()) |> Array.filter ((<>) "") |> Array.toList
let names (p: PlantCard) = [ p.ScientificName; p.CommonNames ] @ p.Aliases

/// Exact names first, then prefixes, then all-token matches. No inferred taxonomic synonymy.
let score (q: string) (p: PlantCard) =
    let q = normalize q
    let ns = names p |> List.map normalize
    if q = "" then 4
    elif ns |> List.contains q then 0
    elif ns |> List.exists (fun n -> n.StartsWith q) then 1
    elif ns |> List.exists (fun n -> n.Split(' ') |> Array.exists (fun token -> token.StartsWith q)) then 2
    else
        let haystack = String.concat " " (ns @ [ normalize p.Family; normalize p.Genus ])
        let tokens = q.Split(' ') |> Array.filter ((<>) "")
        if tokens |> Array.forall haystack.Contains then 3 else 99

let private selected (selection: string option) available =
    match selection with
    | None -> true
    | Some v when v = "" -> true
    | Some v -> values v |> List.exists (fun x -> available |> List.exists (fun y -> normalize x = normalize y))

/// OR within each selected facet, AND between facets. Missing values never imply a negative fact.
let filter (query: Query) (plants: PlantCard list) =
    let q = query.Q |> Option.defaultValue "" |> fun s -> s.Substring(0, min 160 s.Length)
    plants
    |> List.filter (fun p ->
        score q p < 99 && selected query.Family [p.Family] && selected query.Genus [p.Genus]
        && selected query.Form p.Forms && selected query.Sun p.Sun && selected query.Water p.Water
        && selected query.Feature p.GardenFeatures && selected query.Wildlife p.Wildlife
        && selected query.Photographer p.Photographers
        && (query.Endemic <> Some "true" || p.EndemicNt) && (query.Photos <> Some "true" || p.Photo.IsSome))
    |> List.sortBy (fun p -> score q p, normalize p.ScientificName)

let suggestions q plants =
    if (normalize q).Length < 2 then []
    else filter { emptyQuery with Q=Some q } plants |> List.truncate 7

let facets (get: PlantCard -> string list) plants : Facet list =
    plants |> List.collect (get >> List.distinct) |> List.countBy id
    |> List.sortBy fst |> List.map (fun (name,count) -> {Name=name;Count=count})

let families (plants: PlantCard list) : Taxon list =
    plants |> List.groupBy (fun p -> p.Family) |> List.sortBy fst
    |> List.map (fun (name,ps) -> { Name=name;Count=ps.Length;Genera=ps |> List.map (fun p -> p.Genus) |> List.distinct |> List.sort })
