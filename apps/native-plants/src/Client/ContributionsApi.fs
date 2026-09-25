module Client.ContributionsApi

open Fable.Core
open Fable.Core.JsInterop
open Hedge.Http
open Models.Contributions

// Capture the request's credential and viewer, never look them up after a wait.
let client viewer key =
    Client.ClientGen.createClient (fun request ->
        let headers = [
            if viewer<>"" then "X-Contribution-Viewer",viewer
            if key<>"" then "X-Admin-Key",key ]
        Client.GuestSession.transport Client.Api.uncachedBrowserTransport {request with Headers=headers @ request.Headers})

let private personalView (data:Models.Api.PersonalSnapshot) : Personal =
    { Anonymous=data.Anonymous; ViewerToken=data.ViewerToken; Notes=List.toArray data.Notes
      Photos=List.toArray data.Photos; HeroPhotoId=data.HeroPhotoId
      NoteCapacity=data.NoteCapacity; PhotoCapacity=data.PhotoCapacity }
let private reviewView (data:Models.Api.ReviewSnapshot) : Review =
    {Notes=List.toArray data.Notes;Photos=List.toArray data.Photos;Page=data.Page;HasMore=data.HasMore}
let private map projection operation = promise {
    let! result=operation
    return Result.map projection result
}
let access key = (client "" key).getAccess() |> map (fun data -> ({CanEditCatalogue=data.CanEditCatalogue;CanReview=data.CanReview;CanIdentify=data.CanIdentify}:Capabilities))
let personal plantId = (client "" "").getPersonal plantId |> map (fun data -> personalView data.Personal)
let review key page = (client "" key).getReview {Page=Some(string page)} |> map (fun data -> reviewView data.Review)
let change plantId viewer command =
    let api=client viewer ""
    match command with
    | SaveNote(id,revision,text,correction) ->
        api.saveNote {PlantId=plantId;Id=id;Revision=revision;Text=text;Correction=correction} |> map (fun data -> personalView data.Personal)
    | SaveEntry(id,revision,text,purpose,photos) ->
        api.saveFieldNote {PlantId=plantId;Id=id;Revision=revision;Text=text;Purpose=purpose;PhotoIds=photos} |> map (fun data -> personalView data.Personal)
    | DeleteNote(id,revision) -> api.deleteNote {PlantId=plantId;Id=id;Revision=revision} |> map (fun data -> personalView data.Personal)
    | UpdatePhoto(id,revision,caption,photographer,offered) ->
        api.updatePhoto {PlantId=plantId;Id=id;Revision=revision;Caption=caption;Photographer=photographer;Offered=offered} |> map (fun data -> personalView data.Personal)
    | DeletePhoto(id,revision) -> api.deletePhoto {PlantId=plantId;Id=id;Revision=revision} |> map (fun data -> personalView data.Personal)
    | SelectHero id -> api.selectHero {PlantId=plantId;Id=id} |> map (fun data -> personalView data.Personal)
let reviewChange key page command =
    let api=client "" key
    match command with
    | CorrectionRead(id,revision,read) -> api.reviewCorrection {Id=id;Revision=revision;Read=read;Page=page} |> map (fun data -> reviewView data.Review)
    | Identify(id,revision,outcome,text,alternative) ->
        api.identifyNote {Id=id;Revision=revision;Outcome=outcome;Text=text;AlternativePlantId=alternative;Page=page} |> map (fun data -> reviewView data.Review)
    | PromotePhoto(id,revision) -> api.promotePhoto {Id=id;Revision=revision;Page=page} |> map (fun data -> reviewView data.Review)

type private UploadResponse = {Status:int;Body:string}
[<Import("upload", "./contribution-client.mjs")>]
let private sendUpload (plantId:string) (id:string) (file:obj) (viewer:string) : JS.Promise<UploadResponse> = jsNative

/// Multipart stays app-owned; its response is consumed inside the existing session
/// lock, then the generated client loads a decoded snapshot in a separate lock.
let uploadPhoto plantId id file viewer : JS.Promise<Result<unit,ApiError>> = promise {
    try
        let! response=sendUpload plantId id file viewer
        if response.Status>=200 && response.Status<300 then return Ok ()
        else return Error(errorFromResponse {Status=response.Status;Body=response.Body;Headers=[]})
    with ex -> return Error(TransportFailure ex.Message)
}
let upload plantId id file viewer = promise {
    let! sent=uploadPhoto plantId id file viewer
    match sent with
    | Error error -> return Error error
    | Ok () ->
        let! result=personal plantId
        return result |> Result.bind(fun data ->
            if data.ViewerToken=viewer then Ok data
            else Error(HttpFailure(409,"Your session changed. Refresh before continuing.")))
}

/// Elmish views currently display a single error message; retain ApiError until this boundary.
let forView operation = promise {
    let! result=operation
    match result with Ok value -> return value | Error error -> return failwith(renderError error)
}
