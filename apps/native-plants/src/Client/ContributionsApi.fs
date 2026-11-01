module Client.ContributionsApi

open Fable.Core
open Fable.Core.JsInterop
open Hedge.Http
open Models.Contributions

// Capture the request's credential and viewer, never look them up after a wait. Personal reads (no
// admin key) go through the identity-scoped read cache, which carries the session lock inside it, so a
// saved notebook opens offline for its owner before any session check; review reads lock as before.
let client viewer key =
    let transport = if key="" then Client.Sync.privateReads else Client.GuestSession.transport (if Client.Api.apiOrigin="" then Client.Api.uncachedBrowserTransport else Client.Api.appTransport)
    Client.ClientGen.createClient (fun request ->
        let headers = [
            if viewer<>"" then "X-Contribution-Viewer",viewer
            if key<>"" then "X-Admin-Key",key ]
        transport {request with Headers=headers @ request.Headers})

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
/// A notebook command on the network. Its answer is the notebook as the server now has it, kept as the
/// owner's saved snapshot (under the identity that made the request) before it is shown.
let change plantId viewer command =
    let api=client viewer ""
    let owner=Client.Storage.owner
    let snapshot : JS.Promise<Result<Models.Api.PersonalSnapshot,ApiError>> =
        match command with
        | SaveNote(id,revision,text,correction) ->
            api.saveNote {PlantId=plantId;Id=id;Revision=revision;Text=text;Correction=correction} |> map (fun data -> data.Personal)
        | SaveEntry(id,revision,text,purpose,photos) ->
            api.saveFieldNote {PlantId=plantId;Id=id;Revision=revision;Text=text;Purpose=purpose;PhotoIds=photos} |> map (fun data -> data.Personal)
        | DeleteNote(id,revision) -> api.deleteNote {PlantId=plantId;Id=id;Revision=revision} |> map (fun data -> data.Personal)
        | UpdatePhoto(id,revision,caption,photographer,offered) ->
            api.updatePhoto {PlantId=plantId;Id=id;Revision=revision;Caption=caption;Photographer=photographer;Offered=offered} |> map (fun data -> data.Personal)
        | DeletePhoto(id,revision) -> api.deletePhoto {PlantId=plantId;Id=id;Revision=revision} |> map (fun data -> data.Personal)
        | SelectHero id -> api.selectHero {PlantId=plantId;Id=id} |> map (fun data -> data.Personal)
    promise {
        let! result=snapshot
        match result with
        | Ok data when owner<>"" -> do! Client.Sync.rememberSnapshot owner plantId data
        | _ -> ()
        return result |> Result.map personalView
    }
let reviewChange key page command =
    let api=client "" key
    match command with
    | CorrectionRead(id,revision,read) -> api.reviewCorrection {Id=id;Revision=revision;Read=read;Page=page} |> map (fun data -> reviewView data.Review)
    | Identify(id,revision,outcome,text,alternative) ->
        api.identifyNote {Id=id;Revision=revision;Outcome=outcome;Text=text;AlternativePlantId=alternative;Page=page} |> map (fun data -> reviewView data.Review)
    | PromotePhoto(id,revision) -> api.promotePhoto {Id=id;Revision=revision;Page=page} |> map (fun data -> reviewView data.Review)

/// A notebook command: delivered (Some snapshot), kept on this phone because the network is away
/// (None), or refused with the reason.
let changeOrQueue plantId viewer command : JS.Promise<Result<Personal option,string>> = promise {
    let! waiting=Client.Sync.supersedes plantId command
    if waiting && Client.Storage.owner<>"" then
        // A change to something already waiting joins the queue behind it, in order, rather than overtaking
        // it on the network; the queue is then sent at once when it can be.
        do! Client.Sync.queueChange plantId viewer command
        let! _=Client.Sync.flush()
        let! still=Client.Sync.supersedes plantId command
        if still then return Ok None
        else
            let! result=personal plantId
            return result |> Result.map Some |> Result.mapError renderError
    else
        let! result=change plantId viewer command
        match result with
        | Ok data -> return Ok(Some data)
        | Error error when Client.Sync.away error && Client.Storage.owner<>"" ->
            do! Client.Sync.queueChange plantId viewer command
            return Ok None
        | Error error -> return Error(renderError error)
}

type private UploadResponse = {Status:int;Body:string}
[<Import("prepare", "./contribution-client.mjs")>]
let private prepare (file:obj) : JS.Promise<obj> = jsNative
[<Import("send", "./contribution-client.mjs")>]
let private sendPrepared (plantId:string) (id:string) (image:obj) (thumbnail:obj) (viewer:string) : JS.Promise<UploadResponse> = jsNative

/// Multipart stays app-owned. The file is prepared (validated, resized) first; the send runs inside the
/// session lock and a decoded snapshot follows in its own lock. Away from the network the prepared pair
/// is kept on this phone and queued (None); a refusal is reported with its reason.
let uploadOrQueue plantId id file viewer : JS.Promise<Result<Personal option,string>> = promise {
    try
        let! prepared=prepare file
        let image:obj=prepared?image
        let thumbnail:obj=prepared?thumbnail
        let! sent=promise {
            try
                let! response=Client.GuestSession.withSessionRequest (fun ()->sendPrepared plantId id image thumbnail viewer)
                return Ok response
            with ex -> return Error(TransportFailure ex.Message)
        }
        match sent with
        | Ok response when response.Status>=200 && response.Status<300 ->
            let! result=personal plantId
            return result |> Result.mapError renderError |> Result.bind(fun data ->
                if data.ViewerToken=viewer then Ok(Some data)
                else Error "Your session changed. Refresh before continuing.")
        | Ok response -> return Error(renderError(errorFromResponse {Status=response.Status;Body=response.Body;Headers=[]}))
        | Error error when Client.Sync.away error && Client.Storage.owner<>"" ->
            do! Client.Sync.queueUpload plantId id viewer image thumbnail (unbox prepared?width) (unbox prepared?height)
            return Ok None
        | Error error -> return Error(renderError error)
    with ex -> return Error ex.Message
}

/// Elmish views currently display a single error message; retain ApiError until this boundary.
let forView operation = promise {
    let! result=operation
    match result with Ok value -> return value | Error error -> return failwith(renderError error)
}
