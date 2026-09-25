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

// Generated request-aware handlers keep contribution policy in this app.
let inline private privateResponse cookie value = ContributionApi.decorate cookie (respond value)
let getAccess request (env:Env) _ctx = promise {
    let! allowed,cookie=Contributions.reviewer env request
    return privateResponse cookie ({CanEditCatalogue=AuthConfig.isOwner env request;CanReview=allowed}:GetAccess.Response)
}
let getPersonal id request (env:Env) _ctx =
    ContributionApi.withOwner request env id (fun who -> promise {
        let! data=Contributions.personalData env id who
        return privateResponse who.Cookie ({Personal=ContributionApi.personalSnapshot data}:GetPersonal.Response)
    })
let getReview (query:GetReview.Query) request (env:Env) _ctx =
    ContributionApi.withReviewer request env (fun cookie -> promise {
        let! data=Contributions.reviewData env (match System.Int32.TryParse(Option.defaultValue "0" query.Page) with true,n -> n | _ -> 0)
        return privateResponse cookie ({Review=ContributionApi.reviewSnapshot data}:GetReview.Response)
    })

let saveNote (body:SaveNote.Request) request (env:Env) _ctx =
    ContributionApi.withOwner request env body.PlantId (fun who ->
        Contributions.mutateCommand env body.PlantId who (Models.Contributions.SaveNote(body.Id,body.Revision,body.Text,body.Correction))
            (fun data -> privateResponse who.Cookie ({Personal=ContributionApi.personalSnapshot data}:SaveNote.Response)))

let deleteNote (body:DeleteNote.Request) request (env:Env) _ctx =
    ContributionApi.withOwner request env body.PlantId (fun who ->
        Contributions.mutateCommand env body.PlantId who (Models.Contributions.DeleteNote(body.Id,body.Revision))
            (fun data -> privateResponse who.Cookie ({Personal=ContributionApi.personalSnapshot data}:DeleteNote.Response)))

let updatePhoto (body:UpdatePhoto.Request) request (env:Env) _ctx =
    ContributionApi.withOwner request env body.PlantId (fun who ->
        Contributions.mutateCommand env body.PlantId who (Models.Contributions.UpdatePhoto(body.Id,body.Revision,body.Caption,body.Photographer,body.Offered))
            (fun data -> privateResponse who.Cookie ({Personal=ContributionApi.personalSnapshot data}:UpdatePhoto.Response)))

let deletePhoto (body:DeletePhoto.Request) request (env:Env) _ctx =
    ContributionApi.withOwner request env body.PlantId (fun who ->
        Contributions.mutateCommand env body.PlantId who (Models.Contributions.DeletePhoto(body.Id,body.Revision))
            (fun data -> privateResponse who.Cookie ({Personal=ContributionApi.personalSnapshot data}:DeletePhoto.Response)))

let selectHero (body:SelectHero.Request) request (env:Env) _ctx =
    ContributionApi.withOwner request env body.PlantId (fun who ->
        Contributions.mutateCommand env body.PlantId who (Models.Contributions.SelectHero body.Id)
            (fun data -> privateResponse who.Cookie ({Personal=ContributionApi.personalSnapshot data}:SelectHero.Response)))

let reviewCorrection (body:ReviewCorrection.Request) request (env:Env) _ctx =
    ContributionApi.withReviewer request env (fun cookie ->
        Contributions.reviewCommand env cookie body.Page (Models.Contributions.CorrectionRead(body.Id,body.Revision,body.Read))
            (fun data -> privateResponse cookie ({Review=ContributionApi.reviewSnapshot data}:ReviewCorrection.Response)))

let promotePhoto (body:PromotePhoto.Request) request (env:Env) _ctx =
    ContributionApi.withReviewer request env (fun cookie ->
        Contributions.reviewCommand env cookie body.Page (Models.Contributions.PromotePhoto(body.Id,body.Revision))
            (fun data -> privateResponse cookie ({Review=ContributionApi.reviewSnapshot data}:PromotePhoto.Response)))
