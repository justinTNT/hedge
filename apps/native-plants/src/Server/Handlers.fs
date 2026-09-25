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
    let! identify,renewal=Contributions.identifier env request
    return privateResponse (Option.orElse cookie renewal) ({CanEditCatalogue=AuthConfig.isOwner env request;CanReview=allowed;CanIdentify=identify}:GetAccess.Response)
}
let getPersonal id request (env:Env) _ctx =
    ContributionApi.withOwner request env id (fun who -> promise {
        let! data=Contributions.personalData env id who
        return privateResponse who.Cookie ({Personal=ContributionApi.personalSnapshot data}:GetPersonal.Response)
    })
let getReview (query:GetReview.Query) request (env:Env) _ctx =
    ContributionApi.withReviewAccess request env (fun cookie curate identify -> promise {
        let! data=Contributions.reviewDataFor env (match System.Int32.TryParse(Option.defaultValue "0" query.Page) with true,n -> n | _ -> 0) curate identify
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

let reviewCorrection (body:ReviewCorrection.Request) request (env:Env) ctx =
    ContributionApi.withReviewer request env (fun cookie -> promise {
        let! changed=Contributions.applyCuratorCommand env (Models.Contributions.CorrectionRead(body.Id,body.Revision,body.Read))
        if changed then return! getReview {Page=Some(string body.Page)} request env ctx
        else return Contributions.conflict cookie
    })

let promotePhoto (body:PromotePhoto.Request) request (env:Env) ctx =
    ContributionApi.withReviewer request env (fun cookie -> promise {
        let! changed=Contributions.applyCuratorCommand env (Models.Contributions.PromotePhoto(body.Id,body.Revision))
        if changed then return! getReview {Page=Some(string body.Page)} request env ctx
        else return Contributions.conflict cookie
    })

let saveFieldNote (body:SaveFieldNote.Request) request (env:Env) _ctx =
    ContributionApi.withOwner request env body.PlantId (fun who ->
        Contributions.mutateCommand env body.PlantId who (Models.Contributions.SaveEntry(body.Id,body.Revision,body.Text,body.Purpose,body.PhotoIds))
            (fun data -> privateResponse who.Cookie ({Personal=ContributionApi.personalSnapshot data}:SaveFieldNote.Response)))

let identifyNote (body:IdentifyNote.Request) request (env:Env) ctx =
    ContributionApi.withIdentifier request env (fun cookie -> promise {
        let! changed=FieldNotes.identify env request body
        if changed then return! getReview {Page=Some(string body.Page)} request env ctx
        else return Contributions.conflict cookie
    })
