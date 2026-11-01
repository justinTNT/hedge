module Client.Sync

// App-level offline sync for the notebook. The framework (Client.Offline) owns the durable queue,
// ordering and replay; this module owns the policy: whose writes queue, how a queued item is sent,
// what each outcome means under this app's contribution rules, how later edits fold into earlier
// queued ones, and the typed view of pending items the notebook overlays on its last snapshot.

open Fable.Core
open Fable.Core.JsInterop
open Thoth.Json
open Hedge.Http
open Hedge.Interface
open Models.Api
open Client.Offline

/// Where a read was answered from, for the offline notice. The app installs its dispatcher.
let mutable served : Served -> unit = ignore
let mutable private changed : unit -> unit = ignore
/// Hear every outbox change (queued, delivered, parked, discarded) so the notebook refreshes.
let onChange (handler: unit -> unit) = changed <- handler

let store = Client.Storage.store
let private inner = if Client.Api.apiOrigin = "" then Client.Api.uncachedBrowserTransport else Client.Api.appTransport
/// Private reads cached under the identity's scope, so a notebook opens offline for its owner only. The
/// session lock sits inside the cache: a saved copy answers even when no session can be confirmed (a cold
/// start without signal), while every network read and every write still runs under the lock.
let privateReads : Transport = cachedTransport store (fun () -> Client.Storage.owner) (fun _ outcome -> served outcome) (Client.GuestSession.transport inner)

/// A request that never completed because the network is away. The session lock's own refusal also
/// arrives as a transport failure and must not be mistaken for that.
let away (error: ApiError) =
    match error with
    | TransportFailure message -> message <> Client.GuestSession.sessionChangedMessage
    | _ -> false

// ---- Typed view of queued items ----

type Change =
    | Entry of SaveFieldNote.Request
    | Note of SaveNote.Request
    | Remove of DeleteNote.Request
    | PhotoDetails of UpdatePhoto.Request
    | RemovePhoto of DeletePhoto.Request
    | Hero of SelectHero.Request
    | Upload of plantId: string * id: string * width: int * height: int
type Pending = { Item: OutboxItem; Change: Change }

let plantOf = function
    | Entry r -> r.PlantId | Note r -> r.PlantId | Remove r -> r.PlantId | PhotoDetails r -> r.PlantId
    | RemovePhoto r -> r.PlantId | Hero r -> r.PlantId | Upload (plantId, _, _, _) -> plantId
/// The notebook item a change is about ("" for a hero choice, which is about the plant).
let itemOf = function
    | Entry r -> r.Id | Note r -> r.Id | Remove r -> r.Id | PhotoDetails r -> r.Id | RemovePhoto r -> r.Id
    | Hero _ -> "" | Upload (_, id, _, _) -> id

let uploadPath (plantId: string) (id: string) = "/api/plants/personal/" + plantId + "/photos/" + id
let imageKey (id: string) = "media/outbox/" + id + "/image"
let thumbnailKey (id: string) = "media/outbox/" + id + "/thumbnail"
/// The paths a kept pending photograph shows under; Client.Images resolves them from the store.
let localImage (id: string) = Image ("outbox/" + id + "/image")
let localThumbnail (id: string) = Image ("outbox/" + id + "/thumbnail")

let private body (decoder: Decoder<'T>) (item: OutboxItem) =
    item.Request.Body |> Option.bind (fun text -> Decode.fromString decoder text |> Result.toOption)
let private uploadDecoder =
    Decode.map4 (fun plantId id width height -> Upload (plantId, id, width, height))
        (Decode.field "plantId" Decode.string) (Decode.field "id" Decode.string)
        (Decode.field "width" Decode.int) (Decode.field "height" Decode.int)
/// The typed change a queued item carries, by its endpoint path; None for anything unrecognised.
let describe (item: OutboxItem) : Change option =
    match item.Request.Path with
    | "/api/plants/v2/notes/entry" -> body Codecs.Decode.saveFieldNoteReq item |> Option.map Entry
    | "/api/plants/v2/notes/save" -> body Codecs.Decode.saveNoteReq item |> Option.map Note
    | "/api/plants/v2/notes/delete" -> body Codecs.Decode.deleteNoteReq item |> Option.map Remove
    | "/api/plants/v2/photos/update" -> body Codecs.Decode.updatePhotoReq item |> Option.map PhotoDetails
    | "/api/plants/v2/photos/delete" -> body Codecs.Decode.deletePhotoReq item |> Option.map RemovePhoto
    | "/api/plants/v2/hero" -> body Codecs.Decode.selectHeroReq item |> Option.map Hero
    | path when path.StartsWith "/api/plants/personal/" -> body uploadDecoder item
    | _ -> None

// ---- Folding a new change into what is already queued ----

/// What an incoming change does to an item already queued: nothing, replace it, or cancel both
/// (a delete of something the server never received).
type Fold = Keep | Replace | Cancel
let fold (existing: Change) (incoming: Change) : Fold =
    let same = itemOf existing <> "" && itemOf existing = itemOf incoming
    match existing, incoming with
    | (Entry _ | Note _), (Entry _ | Note _) when same -> Replace
    | Entry { Revision = 0 }, Remove _ when same -> Cancel
    | Note { Revision = 0 }, Remove _ when same -> Cancel
    | (Entry _ | Note _), Remove _ when same -> Replace
    | PhotoDetails _, PhotoDetails _ when same -> Replace
    | Upload _, RemovePhoto _ when same -> Cancel
    | PhotoDetails _, RemovePhoto _ when same -> Replace
    | Hero a, Hero b when a.PlantId = b.PlantId -> Replace
    | _ -> Keep

// ---- Sending and judging ----

type private UploadResult = { Status: int; Body: string }
[<Import("send", "./contribution-client.mjs")>]
let private sendUpload (plantId: string) (id: string) (image: obj) (thumbnail: obj) (viewer: string) : JS.Promise<UploadResult> = jsNative

let private viewerOf (item: OutboxItem) =
    item.Request.Headers |> List.tryPick (fun (k, v) -> if k = "X-Contribution-Viewer" then Some v else None) |> Option.defaultValue ""

/// The owner's saved notebook for a plant, as last primed, if any.
let private snapshotFor (scope: string) (plantId: string) : JS.Promise<Models.Api.PersonalSnapshot option> = promise {
    let path = (Client.ClientGen.requests.getPersonal plantId).Path
    let! cached = cachedResponses store scope path
    return cached |> List.tryPick (fun (p, response, _) ->
        if p = path then Decode.fromString Codecs.Decode.getPersonalResponse response.Body |> Result.toOption |> Option.map (fun r -> r.Personal) else None)
}
/// A photograph queued for upload has revision 0 until the server creates it; a change to its details
/// queued behind it is sent with the revision the server then gave it, read from the owner's snapshot
/// that the upload's delivery stored. Other requests go as queued.
let prepare (item: OutboxItem) : JS.Promise<Request> = promise {
    let requests = Client.ClientGen.requests
    let revisionOf (scope: string) (plantId: string) (photoId: string) = promise {
        let! snapshot = snapshotFor scope plantId
        return snapshot |> Option.bind (fun s -> s.Photos |> List.tryFind (fun p -> p.Id = photoId)) |> Option.map (fun p -> p.Revision) |> Option.filter (fun r -> r > 0)
    }
    match describe item with
    | Some (PhotoDetails r) when r.Revision = 0 ->
        let! revision = revisionOf item.Scope r.PlantId r.Id
        return (match revision with Some revision -> { requests.updatePhoto { r with Revision = revision } with Headers = item.Request.Headers } | None -> item.Request)
    | Some (RemovePhoto r) when r.Revision = 0 ->
        let! revision = revisionOf item.Scope r.PlantId r.Id
        return (match revision with Some revision -> { requests.deletePhoto { r with Revision = revision } with Headers = item.Request.Headers } | None -> item.Request)
    | _ -> return item.Request
}

let private send (item: OutboxItem) : JS.Promise<Result<Response, ApiError>> = promise {
    match describe item with
    | Some (Upload (plantId, id, _, _)) ->
        let! image = store.Get (imageKey id)
        let! thumbnail = store.Get (thumbnailKey id)
        match image, thumbnail with
        | Some image, Some thumbnail ->
            try
                let! result = Client.GuestSession.withSessionRequest (fun () -> sendUpload plantId id image thumbnail (viewerOf item))
                return Ok ({ Status = result.Status; Headers = []; Body = result.Body }: Response)
            with ex -> return Error (TransportFailure ex.Message)
        | _ -> return Ok ({ Status = 410; Headers = []; Body = """{"error":"This photograph is no longer on this phone."}""" }: Response)
    | _ ->
        let! request = prepare item
        return! Client.GuestSession.transport inner request
}

let private errorText (response: Response) =
    match Decode.fromString (Decode.field "error" Decode.string) response.Body with
    | Ok message -> message
    | Error _ -> sprintf "HTTP %d" response.Status
/// This app's reading of a replay: delivered on 2xx; retried while the network is away or the server
/// fails; otherwise parked with the server's own words (a full notebook, a changed item, an expired
/// sign-in), because only the person can resolve those.
let decide (_: OutboxItem) (outcome: Result<Response, ApiError>) : Decision =
    match outcome with
    | Ok response when response.Status >= 200 && response.Status < 300 -> Delivered
    | Ok response when response.Status >= 500 -> Retry
    | Ok response -> Park (errorText response)
    | Error error when away error -> Retry
    | Error error -> Park (renderError error)

[<Import("fetchImageBlob", "./image-client.mjs")>]
let private fetchBlob (path: string) : JS.Promise<obj> = jsNative
let private serverPaths (id: string) = [ imageKey id, "/api/plants/personal-media/" + id + "/image"; thumbnailKey id, "/api/plants/personal-media/" + id + "/thumbnail" ]
/// A delivered photograph stays on this phone as the person's own copy: its prepared bytes move from the
/// queue to their identity-bound media, under the paths the server will now show it at.
let keepDelivered (item: OutboxItem) : JS.Promise<unit> = promise {
    match describe item with
    | Some (Upload (_, id, _, _)) ->
        for (key, url) in serverPaths id do
            let! blob = store.Get key
            match blob with
            | Some blob -> do! keepPrivateMedia store item.Scope url blob
            | None -> ()
    | _ -> ()
}
/// Keep the person's own photographs of a plant, both sizes, so their notebook reads offline.
let keepOwnPhotos (data: Models.Contributions.Personal) : JS.Promise<unit> = promise {
    if Client.Storage.owner <> "" then
        let urls = [ for photo in data.Photos do yield Codecs.img photo.Image; yield Codecs.img photo.Thumbnail ] |> List.filter (fun url -> url.StartsWith "/api/")
        let! _ = fetchPrivateMedia store Client.Storage.owner fetchBlob urls 2 ignore (fun () -> Client.Storage.owner <> "")
        ()
}

/// Store a notebook snapshot as the owner's saved copy of that plant's personal read, so a restart
/// without signal shows the notebook as the server last acknowledged it, not as it was before.
let rememberSnapshot (scope: string) (plantId: string) (snapshot: Models.Api.PersonalSnapshot) : JS.Promise<unit> =
    let body = Hedge.Codec.encode ({ Personal = snapshot }: Models.Api.GetPersonal.Response) |> Encode.toString 0
    primeRead store scope (Client.ClientGen.requests.getPersonal plantId) { Status = 200; Headers = []; Body = body }
/// A delivered command's answer carries the notebook as the server now has it: keep it as the owner's
/// saved snapshot before the queued item goes, so nothing acknowledged is lost if the next read fails.
let rememberDelivered (item: OutboxItem) (response: Response) : JS.Promise<unit> = promise {
    match describe item with
    | Some change when Decode.fromString Codecs.Decode.getPersonalResponse response.Body |> Result.isOk ->
        do! primeRead store item.Scope (Client.ClientGen.requests.getPersonal (plantOf change)) { response with Status = 200 }
    | _ -> ()
}

let outbox = createOutbox store (fun () -> Client.Storage.owner) send decide (fun item response -> promise {
    do! keepDelivered item
    do! rememberDelivered item response })

let flush () = promise {
    let! remaining = outbox.Flush ()
    changed ()
    return remaining
}
let private pendingItems () = promise {
    let! items = outbox.Items ()
    return items |> List.choose (fun item -> describe item |> Option.map (fun change -> { Item = item; Change = change }))
}
/// Queued changes for one plant, oldest first.
let pendingFor (plantId: string) = promise {
    let! items = pendingItems ()
    return items |> List.filter (fun p -> plantOf p.Change = plantId)
}

let private queue (request: Request) (attachments: string list) (change: Change) = promise {
    let! pending = pendingItems ()
    let folds = pending |> List.map (fun p -> p, fold p.Change change)
    if folds |> List.exists (fun (_, f) -> f = Cancel) then
        // Nothing reaches the server: the unsent change and this one cancel out, kept bytes included.
        for (p, f) in folds do
            if f <> Keep then do! outbox.Remove p.Item.Id
        for attachment in attachments do do! store.Delete attachment
    else
        // The successor is durable before its predecessors go, so a failure in between leaves the older
        // acknowledged change in place rather than nothing.
        let! _ = outbox.Enqueue request attachments
        for (p, f) in folds do
            if f = Replace then do! outbox.Remove p.Item.Id
    changed ()
}


let private viewerHeader (viewer: string) = if viewer = "" then [] else [ "X-Contribution-Viewer", viewer ]
let private typed (plantId: string) (command: Models.Contributions.PersonalChange) : Request * Change =
    let requests = Client.ClientGen.requests
    match command with
    | Models.Contributions.SaveEntry (id, revision, text, purpose, photos) ->
        let body : SaveFieldNote.Request = { PlantId = plantId; Id = id; Revision = revision; Text = text; Purpose = purpose; PhotoIds = photos }
        requests.saveFieldNote body, Entry body
    | Models.Contributions.SaveNote (id, revision, text, correction) ->
        let body : SaveNote.Request = { PlantId = plantId; Id = id; Revision = revision; Text = text; Correction = correction }
        requests.saveNote body, Note body
    | Models.Contributions.DeleteNote (id, revision) ->
        let body : DeleteNote.Request = { PlantId = plantId; Id = id; Revision = revision }
        requests.deleteNote body, Remove body
    | Models.Contributions.UpdatePhoto (id, revision, caption, photographer, offered) ->
        let body : UpdatePhoto.Request = { PlantId = plantId; Id = id; Revision = revision; Caption = caption; Photographer = photographer; Offered = offered }
        requests.updatePhoto body, PhotoDetails body
    | Models.Contributions.DeletePhoto (id, revision) ->
        let body : DeletePhoto.Request = { PlantId = plantId; Id = id; Revision = revision }
        requests.deletePhoto body, RemovePhoto body
    | Models.Contributions.SelectHero id ->
        let body : SelectHero.Request = { PlantId = plantId; Id = id }
        requests.selectHero body, Hero body

/// Whether a command touches something already waiting in this plant's queue (the same note or
/// photograph, or this plant's hero choice). Such a command joins the queue instead of overtaking it.
let supersedes (plantId: string) (command: Models.Contributions.PersonalChange) : JS.Promise<bool> = promise {
    let! pending = pendingFor plantId
    let _, change = typed plantId command
    return pending |> List.exists (fun p -> fold p.Change change <> Keep)
}
/// What a queued change is, for a person reading a list of them.
let label = function
    | Entry _ | Note _ -> "A note"
    | Remove _ -> "Deleting a note"
    | PhotoDetails _ -> "Photograph details"
    | RemovePhoto _ -> "Removing a photograph"
    | Hero _ -> "The hero photograph choice"
    | Upload _ -> "A photograph"

/// Queue a notebook command made while away; the viewer token travels with it, as it would have.
let queueChange (plantId: string) (viewer: string) (command: Models.Contributions.PersonalChange) = promise {
    let request, change = typed plantId command
    do! queue { request with Headers = viewerHeader viewer @ request.Headers } [] change
}
/// Keep a prepared photograph's bytes and queue its upload.
let queueUpload (plantId: string) (id: string) (viewer: string) (image: obj) (thumbnail: obj) (width: int) (height: int) = promise {
    do! store.Put (imageKey id) image
    do! store.Put (thumbnailKey id) thumbnail
    let body = Encode.object [ "plantId", Encode.string plantId; "id", Encode.string id; "width", Encode.int width; "height", Encode.int height ] |> Encode.toString 0
    let request : Request = { Method = "POST"; Path = uploadPath plantId id; Query = []; Headers = viewerHeader viewer; Body = Some body }
    do! queue request [ imageKey id; thumbnailKey id ] (Upload (plantId, id, width, height))
}

/// Drop a queued change (and a queued photograph's bytes) unsent.
let discard (id: string) = promise {
    do! outbox.Remove id
    changed ()
}
/// Send a parked change again as the person's version of the item: its revision is brought up to the
/// snapshot's current one, so "This item changed" becomes an edit over the server's copy.
let retry (data: Models.Contributions.Personal option) (id: string) = promise {
    let! pending = pendingItems ()
    match pending |> List.tryFind (fun p -> p.Item.Id = id) with
    | None -> ()
    | Some p ->
        let noteRevision noteId = data |> Option.bind (fun d -> d.Notes |> Array.tryFind (fun n -> n.Id = noteId)) |> Option.map (fun n -> n.Revision)
        let photoRevision photoId = data |> Option.bind (fun d -> d.Photos |> Array.tryFind (fun n -> n.Id = photoId)) |> Option.map (fun n -> n.Revision)
        let requests = Client.ClientGen.requests
        let rebuilt =
            match p.Change with
            | Entry r -> noteRevision r.Id |> Option.map (fun revision -> requests.saveFieldNote { r with Revision = revision })
            | Note r -> noteRevision r.Id |> Option.map (fun revision -> requests.saveNote { r with Revision = revision })
            | Remove r -> noteRevision r.Id |> Option.map (fun revision -> requests.deleteNote { r with Revision = revision })
            | PhotoDetails r -> photoRevision r.Id |> Option.map (fun revision -> requests.updatePhoto { r with Revision = revision })
            | RemovePhoto r -> photoRevision r.Id |> Option.map (fun revision -> requests.deletePhoto { r with Revision = revision })
            | Hero _ | Upload _ -> None
        let request = match rebuilt with Some r -> { r with Headers = p.Item.Request.Headers } | None -> p.Item.Request
        do! outbox.Replace id request
        let! _ = flush ()
        ()
}
/// After a fresh snapshot: parked changes the server evidently already has are finished, not conflicts
/// (a create whose network dropped after the commit, an upload that landed, a delete that took).
let reconcile (data: Models.Contributions.Personal) (pending: Pending list) = promise {
    let settled = ref false
    for p in pending do
        if p.Item.Parked.IsSome then
            let noteExists id = data.Notes |> Array.exists (fun n -> n.Id = id)
            let photoExists id = data.Photos |> Array.exists (fun n -> n.Id = id)
            let done' =
                match p.Change with
                | Entry r when r.Revision = 0 -> noteExists r.Id && (data.Notes |> Array.exists (fun n -> n.Id = r.Id && n.Text = r.Text && n.Purpose = r.Purpose))
                | Note r when r.Revision = 0 -> noteExists r.Id && (data.Notes |> Array.exists (fun n -> n.Id = r.Id && n.Text = r.Text))
                | Remove r -> not (noteExists r.Id)
                | RemovePhoto r -> not (photoExists r.Id)
                | Upload (_, id, _, _) -> photoExists id
                | _ -> false
            if done' then
                do! outbox.Remove p.Item.Id
                settled.Value <- true
    if settled.Value then changed ()
}

// ---- The notebook as it stands on this phone ----

/// The last snapshot with the queued changes applied over it, in order, so the notebook shows what
/// the person did. Capacities move with it so local limits hold while away.
let overlay (data: Models.Contributions.Personal) (pending: Pending list) : Models.Contributions.Personal =
    let apply (state: Models.Contributions.Personal) (p: Pending) =
        let photosFor ids = ids |> List.choose (fun id -> state.Photos |> Array.tryFind (fun (photo: Models.Contributions.Photo) -> photo.Id = id))
        let createdAt = int (p.Item.CreatedAt / 1000.0)
        match p.Change with
        | Entry r ->
            let photos = photosFor r.PhotoIds
            if state.Notes |> Array.exists (fun n -> n.Id = r.Id) then
                { state with Notes = state.Notes |> Array.map (fun n -> if n.Id = r.Id then { n with Text = r.Text; Purpose = r.Purpose; Correction = (r.Purpose = "correction"); Photos = photos } else n) }
            else
                let note : Models.Contributions.Note =
                    { Id = r.Id; Text = r.Text; Correction = (r.Purpose = "correction"); Revision = 0; Read = false; CreatedAt = createdAt; Purpose = r.Purpose; Photos = photos; Responses = [] }
                { state with Notes = Array.append [| note |] state.Notes }
        | Note r ->
            let purpose = if r.Correction then "correction" else "private"
            if state.Notes |> Array.exists (fun n -> n.Id = r.Id) then
                { state with Notes = state.Notes |> Array.map (fun n -> if n.Id = r.Id then { n with Text = r.Text; Purpose = purpose; Correction = r.Correction; Photos = [] } else n) }
            else
                let note : Models.Contributions.Note =
                    { Id = r.Id; Text = r.Text; Correction = r.Correction; Revision = 0; Read = false; CreatedAt = createdAt; Purpose = purpose; Photos = []; Responses = [] }
                { state with Notes = Array.append [| note |] state.Notes }
        | Remove r -> { state with Notes = state.Notes |> Array.filter (fun n -> n.Id <> r.Id) }
        | PhotoDetails r ->
            { state with Photos = state.Photos |> Array.map (fun photo -> if photo.Id = r.Id then { photo with Caption = r.Caption; Photographer = r.Photographer; Offered = r.Offered } else photo) }
        | RemovePhoto r -> { state with Photos = state.Photos |> Array.filter (fun photo -> photo.Id <> r.Id) }
        | Hero r -> { state with HeroPhotoId = r.Id }
        | Upload (_, id, width, height) ->
            if state.Photos |> Array.exists (fun photo -> photo.Id = id) then state
            else
                let photo : Models.Contributions.Photo =
                    { Id = id; Image = localImage id; Thumbnail = localThumbnail id; Caption = ""; Photographer = ""
                      Width = width; Height = height; Offered = false; Revision = 0; PublicPhotoId = "" }
                { state with Photos = Array.append state.Photos [| photo |] }
    let applied = pending |> List.fold apply data
    let shift (capacity: Models.Contributions.Capacity) before after = { capacity with Used = max 0 (capacity.Used + after - before) }
    { applied with
        NoteCapacity = shift data.NoteCapacity data.Notes.Length applied.Notes.Length
        PhotoCapacity = shift data.PhotoCapacity data.Photos.Length applied.Photos.Length }

/// The queued change a notebook item is waiting on, if any (the latest one).
let pendingState (pending: Pending list) (id: string) : OutboxItem option =
    pending |> List.filter (fun p -> itemOf p.Change = id) |> List.tryLast |> Option.map (fun p -> p.Item)
