module Models.Api
open Hedge.Interface

type Photo = { Id: string; Image: string; Thumbnail: string; Caption: string; Photographer: string }
type Section = { Label: string; Text: string }
type PlantCard = {
    Id: string; Slug: string; ScientificName: string; CommonNames: string; Family: string; Genus: string
    Aliases: string list; Forms: string list; Height: string; Sun: string list; Water: string list
    GardenFeatures: string list; Wildlife: string list; EndemicNt: bool; Summary: string; Photo: Photo option; Photographers: string list
}
type DistributionMap = { Id: string; Image: string; Caption: string; SourceLabel: string }
type PlantDetail = { Card: PlantCard; Sections: Section list; Photos: Photo list; DistributionMaps: DistributionMap list }
type Taxon = { Name: string; Count: int; Genera: string list }
type Facet = { Name: string; Count: int }

type GlossaryEntry = { Id: string; Term: string; Aliases: string list; Definition: string; Illustration: string; SourceLabel: string }
type ReferenceEntry = { Id: string; Kind: string; Number: int; Citation: string; Aliases: string list; SourceLabel: string }

module GetCatalogue =
    type Response = { Plants: PlantCard list; Families: Taxon list; Forms: Facet list; Sun: Facet list; Water: Facet list; GardenFeatures: Facet list; Wildlife: Facet list; Revision: string; RefreshSeconds: int; Photographers: Facet list; Glossary: GlossaryEntry list; References: ReferenceEntry list }
    let endpoint : Get<Response> = Get "/api/plants/catalogue"

module GetPlant =
    type Response = { Plant: PlantDetail }
    let endpoint : GetBy<Response> = GetBy (sprintf "/api/plants/plant/%s")

module GetRevision =
    type Response = { Revision: string }
    let endpoint : Get<Response> = Get "/api/plants/revision"

module SearchPlants =
    type Query = { Q: string option; Family: string option; Genus: string option; Form: string option; Sun: string option; Water: string option; Feature: string option; Wildlife: string option; Endemic: string option; Photos: string option; Photographer: string option }
    type Response = { Plants: PlantCard list; Total: int }
    let endpoint : GetQuery<Query, Response> = GetQuery "/api/plants/search"

// Private, generated v2 surface. Public catalogue endpoints retain their ordinary transport.
type PersonalSnapshot = {
    Anonymous:bool; ViewerToken:string; Notes:Models.Contributions.Note list
    Photos:Models.Contributions.Photo list; HeroPhotoId:string
    NoteCapacity:Models.Contributions.Capacity; PhotoCapacity:Models.Contributions.Capacity
}
type ReviewSnapshot = {
    Notes:Models.Contributions.ReviewNote list; Photos:Models.Contributions.ReviewPhoto list
    Page:int; HasMore:bool
}

module GetAccess =
    type Response = { CanEditCatalogue:bool; CanReview:bool }
    let requestContext = true
    let endpoint : Get<Response> = Get "/api/plants/v2/access"

module GetPersonal =
    type Response = { Personal:PersonalSnapshot }
    let requestContext = true
    let endpoint : GetBy<Response> = GetBy (sprintf "/api/plants/v2/personal/%s")

module GetReview =
    type Query = { Page:string option }
    type Response = { Review:ReviewSnapshot }
    let requestContext = true
    let endpoint : GetQuery<Query,Response> = GetQuery "/api/plants/v2/review"

module SaveNote =
    type Request = { PlantId:string; Id:string; Revision:int; Text:string; Correction:bool }
    type Response = { Personal:PersonalSnapshot }
    let endpoint : Post<Request,Response> = Post "/api/plants/v2/notes/save"

module DeleteNote =
    type Request = { PlantId:string; Id:string; Revision:int }
    type Response = { Personal:PersonalSnapshot }
    let endpoint : Post<Request,Response> = Post "/api/plants/v2/notes/delete"

module UpdatePhoto =
    type Request = { PlantId:string; Id:string; Revision:int; Caption:string; Photographer:string; Offered:bool }
    type Response = { Personal:PersonalSnapshot }
    let endpoint : Post<Request,Response> = Post "/api/plants/v2/photos/update"

module DeletePhoto =
    type Request = { PlantId:string; Id:string; Revision:int }
    type Response = { Personal:PersonalSnapshot }
    let endpoint : Post<Request,Response> = Post "/api/plants/v2/photos/delete"

module SelectHero =
    type Request = { PlantId:string; Id:string }
    type Response = { Personal:PersonalSnapshot }
    let endpoint : Post<Request,Response> = Post "/api/plants/v2/hero"

module ReviewCorrection =
    type Request = { Id:string; Revision:int; Read:bool; Page:int }
    type Response = { Review:ReviewSnapshot }
    let endpoint : Post<Request,Response> = Post "/api/plants/v2/review/correction"

module PromotePhoto =
    type Request = { Id:string; Revision:int; Page:int }
    type Response = { Review:ReviewSnapshot }
    let endpoint : Post<Request,Response> = Post "/api/plants/v2/review/promote"
