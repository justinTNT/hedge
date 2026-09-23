module Models.Api
open Hedge.Interface

type Photo = { Id: string; Image: string; Thumbnail: string; Caption: string; Photographer: string }
type Section = { Label: string; Text: string }
type PlantCard = {
    Id: string; Slug: string; ScientificName: string; CommonNames: string; Family: string; Genus: string
    Aliases: string list; Forms: string list; Height: string; Sun: string list; Water: string list
    GardenFeatures: string list; Wildlife: string list; EndemicNt: bool; Summary: string; Photo: Photo option
}
type PlantDetail = { Card: PlantCard; Sections: Section list; Photos: Photo list }
type Taxon = { Name: string; Count: int; Genera: string list }
type Facet = { Name: string; Count: int }

module GetCatalogue =
    type Response = { Plants: PlantCard list; Families: Taxon list; Forms: Facet list; Sun: Facet list; Water: Facet list; GardenFeatures: Facet list; Wildlife: Facet list; Revision: string; RefreshSeconds: int }
    let endpoint : Get<Response> = Get "/api/plants/catalogue"

module GetPlant =
    type Response = { Plant: PlantDetail }
    let endpoint : GetBy<Response> = GetBy (sprintf "/api/plants/plant/%s")

module GetRevision =
    type Response = { Revision: string }
    let endpoint : Get<Response> = Get "/api/plants/revision"

module SearchPlants =
    type Query = { Q: string option; Family: string option; Genus: string option; Form: string option; Sun: string option; Water: string option; Feature: string option; Wildlife: string option; Endemic: string option; Photos: string option }
    type Response = { Plants: PlantCard list; Total: int }
    let endpoint : GetQuery<Query, Response> = GetQuery "/api/plants/search"
