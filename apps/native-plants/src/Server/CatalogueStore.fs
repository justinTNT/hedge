module Server.CatalogueStore

open Fable.Core
open Fable.Core.JsInterop
open Hedge.Workers
open Models.Api
open NativePlants.Catalogue
open Server.Env

type Catalogue = { Public: GetCatalogue.Response; ById: Map<string, PlantDetail> }
type private State = { mutable Value: Catalogue option; mutable LoadedAt: float; mutable Loading: JS.Promise<Catalogue> option; mutable Epoch: int }
[<Emit("new WeakMap()")>]
let private createStates () : obj = jsNative
let private states = createStates ()
[<Emit("$0.get($1)")>]
let private readState (map: obj) (key: obj) : State = jsNative
[<Emit("$0.set($1,$2)")>]
let private writeState (map: obj) (key: obj) (value: State) : unit = jsNative
[<Emit("Date.now()")>]
let private now () : float = jsNative
[<Emit("(async()=>{const hash=await crypto.subtle.digest('SHA-256',new TextEncoder().encode(JSON.stringify($0)));return Array.from(new Uint8Array(hash),b=>b.toString(16).padStart(2,'0')).join('')})()")>]
let private contentRevision (content: obj) : JS.Promise<string> = jsNative

let private stateFor (env: Env) =
    let existing = readState states (box env.DB)
    if isNull (box existing) then
        let s = { Value=None; LoadedAt=0.; Loading=None; Epoch=0 }
        writeState states (box env.DB) s
        s
    else existing

let invalidate env =
    let s = stateFor env
    s.Epoch <- s.Epoch + 1
    s.Value <- None

let private boolField row name = rowInt row name = 1
let private photo row : Photo =
    { Id=rowStr row "id"; Image=rowStr row "image"; Thumbnail=rowStr row "thumbnail"
      Caption=rowStr row "caption"; Photographer=rowStr row "photographer" }
let private labels =
    [ "features","Recognise this plant"; "habit","Habit"; "bark","Bark"; "leaves","Leaves"; "phyllodes","Phyllodes"
      "flowers","Flowers"; "fruit","Fruit"; "flowering","Flowering season"; "fruiting","Fruiting season"
      "habitat","Habitat"; "distribution","Distribution"; "cultivation","Growing this plant"
      "traditional_uses","Aboriginal uses · source account"; "notes","Notes"; "source_references","References" ]

let private distributionMap row : DistributionMap =
    { Id=rowStr row "id"; Image=rowStr row "image"; Caption=rowStr row "caption"; SourceLabel=rowStr row "source_label" }

let private build (rows: obj array) (photoRows: obj array) (mapRows: obj array) (glossaryRows: obj array) (referenceRows: obj array) =
    let glossary = glossaryRows |> Array.map(fun row ->
        {Id=rowStr row "id";Term=rowStr row "term";Aliases=values(rowStr row "aliases");Definition=rowStr row "definition"
         Illustration=rowStr row "illustration";SourceLabel=rowStr row "source_label"}:GlossaryEntry) |> Array.toList
    let references = referenceRows |> Array.map(fun row ->
        {Id=rowStr row "id";Kind=rowStr row "kind";Number=rowInt row "number";Citation=rowStr row "citation"
         Aliases=values(rowStr row "aliases");SourceLabel=rowStr row "source_label"}:ReferenceEntry) |> Array.toList
    let maps = mapRows |> Array.groupBy (fun m -> rowStr m "plant_id") |> Map.ofArray
    let photos = photoRows |> Array.groupBy (fun p -> rowStr p "plant_id") |> Map.ofArray
    let details = rows |> Array.map (fun row ->
        let id = rowStr row "id"
        let gallery = Map.tryFind id photos |> Option.defaultValue [||] |> Array.map photo |> Array.toList
        let card : PlantCard =
            { Id=id; Slug=rowStr row "slug"; ScientificName=rowStr row "scientific_name"
              CommonNames=rowStr row "common_names"; Family=rowStr row "family"; Genus=rowStr row "genus"
              Aliases=values (rowStr row "aliases"); Forms=values (rowStr row "forms"); Height=rowStr row "height"
              Sun=values (rowStr row "sun"); Water=values (rowStr row "water"); GardenFeatures=values (rowStr row "garden_features")
              Wildlife=values (rowStr row "wildlife"); EndemicNt=boolField row "endemic_nt"
              Summary=rowStr row "features"; Photo=List.tryHead gallery
              Photographers=gallery |> List.map(fun p->p.Photographer.Trim()) |> List.filter hasPhotoCredit |> List.distinct |> List.sort }
        { Card=card;Photos=gallery
          DistributionMaps=Map.tryFind id maps |> Option.defaultValue [||] |> Array.map distributionMap |> Array.toList
          Sections=labels |> List.choose (fun (key,label) ->
            let value = rowStr row key
            if value.Trim() = "" then None else Some {Label=label;Text=value}) }) |> Array.toList
    let cards = details |> List.map (fun d -> d.Card) |> List.sortBy (fun p -> p.ScientificName)
    { ById=details |> List.map (fun d -> d.Card.Id,d) |> Map.ofList
      Public={ Plants=cards;Families=families cards;Forms=facets (fun p -> p.Forms) cards
               Sun=facets (fun p -> p.Sun) cards;Water=facets (fun p -> p.Water) cards
               GardenFeatures=facets (fun p -> p.GardenFeatures) cards;Wildlife=facets (fun p -> p.Wildlife) cards
               Revision=string (now ());RefreshSeconds=15;Photographers=facets (fun p->p.Photographers) cards;Glossary=glossary;References=references } }

/// One transactional bulk snapshot per isolate/DB, at most once every 15 seconds. All botanical
/// operations run over immutable records/maps. Expired refresh failures fail closed, including unpublishing.
let rec get (env: Env) : JS.Promise<Catalogue> =
    let s = stateFor env
    match s.Value with
    | Some v when now () - s.LoadedAt < 15000. -> promise { return v }
    | _ ->
        match s.Loading with
        | Some pending -> pending
        | None ->
            let epoch = s.Epoch
            let pending = promise {
                try
                    let! result = env.DB.batch [|
                        env.DB.prepare "SELECT * FROM plants WHERE published=1 AND deleted_at IS NULL ORDER BY scientific_name"
                        env.DB.prepare "SELECT pp.* FROM plant_photos pp JOIN plants p ON p.id=pp.plant_id WHERE pp.published=1 AND pp.deleted_at IS NULL AND p.published=1 AND p.deleted_at IS NULL ORDER BY pp.sort_order, pp.id"
                        env.DB.prepare "SELECT pm.* FROM plant_maps pm JOIN plants p ON p.id=pm.plant_id WHERE pm.published=1 AND pm.deleted_at IS NULL AND p.published=1 AND p.deleted_at IS NULL ORDER BY pm.sort_order, pm.id"
                        env.DB.prepare "SELECT * FROM glossary_terms WHERE published=1 AND deleted_at IS NULL ORDER BY term"
                        env.DB.prepare "SELECT * FROM source_references WHERE published=1 AND deleted_at IS NULL ORDER BY kind, sort_order, id"
                    |]
                    let catalogue = build result.[0].results result.[1].results result.[2].results result.[3].results result.[4].results
                    let! revision = contentRevision (box (catalogue.ById |> Map.toArray |> Array.map snd, catalogue.Public.Glossary, catalogue.Public.References))
                    let catalogue = {catalogue with Public={catalogue.Public with Revision=revision}}
                    s.Loading <- None
                    if epoch = s.Epoch then
                        s.Value <- Some catalogue
                        s.LoadedAt <- now ()
                        return catalogue
                    else return! get env
                with ex ->
                    s.Loading <- None
                    return raise ex
            }
            s.Loading <- Some pending
            pending
