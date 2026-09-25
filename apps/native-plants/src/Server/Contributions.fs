module Server.Contributions

open System
open Fable.Core
open Fable.Core.JsInterop
open Hedge.Workers
open Hedge.Router
open Hedge.GuestSession
open Models.Contributions
open Server.Env

type Owner = { Provider:string; Id:string; Cookie:string option }
type Upload = { image:obj; thumbnail:obj; width:int; height:int; size:int }
[<Import("readPhotoUpload", "./contribution-media.mjs")>]
let readUpload (request:WorkerRequest) : JS.Promise<Upload> = jsNative
[<Import("readJson", "./contribution-media.mjs")>]
let readJson (request:WorkerRequest) : JS.Promise<obj> = jsNative
[<Import("viewerToken", "./contribution-media.mjs")>]
let viewerToken (provider:string) (id:string) : JS.Promise<string> = jsNative
[<Emit("new URL($0.url).pathname.split('/').filter(Boolean)")>]
let path (request:WorkerRequest) : string array = jsNative
[<Emit("($0.headers.get('Origin') === new URL($0.url).origin && $0.headers.get('Sec-Fetch-Site') !== 'cross-site')")>]
let sameOrigin (request:WorkerRequest) : bool = jsNative
[<Emit("typeof $0 === 'string'")>]
let isString (value:obj) : bool = jsNative
[<Emit("typeof $0 === 'boolean'")>]
let isBool (value:obj) : bool = jsNative
[<Emit("Number.isSafeInteger($0)")>]
let isInt (value:obj) : bool = jsNative
[<Emit("$0.put($1,$2,{httpMetadata:{contentType:'image/jpeg'}})")>]
let putImage (bucket:R2Bucket) (key:string) (bytes:obj) : JS.Promise<obj> = jsNative
[<Emit("Math.max(0, Math.min(10000, parseInt(new URL($0.url).searchParams.get('page') || '0',10) || 0))")>]
let pageNumber (request:WorkerRequest) : int = jsNative

exception InvalidInput of string
let invalid message = raise(InvalidInput message)
let textField name maxLength (data:obj) =
    let value:obj=data?(name)
    if not(isString value) then invalid ("Missing " + name + ".")
    let s=(unbox<string> value).Trim()
    if s.Length>maxLength then invalid (name+" is too long.")
    s
let boolField name (data:obj) =
    let value:obj=data?(name)
    if not(isBool value) then invalid ("Missing " + name + ".")
    unbox<bool> value
let revisionField (data:obj) =
    let value:obj=data?Revision
    if not(isInt value) || unbox<int> value<0 then invalid "Missing revision."
    unbox<int> value
let validId id = System.Text.RegularExpressions.Regex.IsMatch(id,"^[a-zA-Z0-9_-]{1,100}$")
let stmt (env:Env) sql args = bind (env.DB.prepare sql) args
let first env sql args = (stmt env sql args).first()
let rows env sql args = promise { let! r=(stmt env sql args).all() in return r.results }
let run env sql args = promise { let! r=(stmt env sql args).run() in return (unbox<int> r.meta?changes)>0 }
let response cookie status value =
    let result=jsonResponse (JS.JSON.stringify value) status
    result?headers?set("Cache-Control","private, no-store") |> ignore
    result?headers?set("Vary","Cookie, X-Admin-Key") |> ignore
    match cookie with Some c -> result?headers?set("Set-Cookie",c) |> ignore |None -> ()
    result
let error cookie status message = response cookie status (createObj ["error" ==> message])
let conflict cookie = error cookie 409 "This item changed. Refresh and try again."
let ownerArgs owner = [|box owner.Provider;box owner.Id|]
// This predicate is part of every anonymous write, not only the earlier session check.
let claimGuard = " AND (? <> 'guest' OR NOT EXISTS (SELECT 1 FROM contribution_claims WHERE guest_id=?))"
let visiblePlant = "EXISTS (SELECT 1 FROM plants p WHERE p.id=? AND p.published=1 AND p.deleted_at IS NULL)"
let owned = "id=? AND plant_id=? AND owner_provider=? AND owner_id=?"

/// Verified accounts are the default; explicit false is retained for legacy anonymous-content tests.
let owner (env:Env) request = promise {
    let! required=requireGuest (Server.AuthConfig.deps env request) (readCookie request)
    match required with
    | Rejected -> return None,None
    | Accepted session ->
        let! deleted=first env "SELECT 1 FROM guests WHERE id=? AND deleted_at IS NOT NULL" [|box session.GuestId|]
        if deleted.IsSome then return None,session.Replacement
        else
            let! subject=Identity.Server.activeSubject env.DB session.GuestId
            match subject with
            | Some(provider,id) -> return Some{Provider=provider;Id=id;Cookie=session.Replacement},session.Replacement
            | None when env.CONTRIBUTIONS_REQUIRE_LOGIN<>"false" -> return None,session.Replacement
            | None ->
                let! claimed=first env "SELECT 1 FROM contribution_claims WHERE guest_id=?" [|box session.GuestId|]
                if claimed.IsSome then return None,session.Replacement
                else return Some{Provider="guest";Id=session.GuestId;Cookie=session.Replacement},session.Replacement
}

let reviewer env request = promise {
    if Server.AuthConfig.isOwner env request then return true,None
    else
        let deps:Hedge.AccessControl.Deps = {
            Guest=Server.AuthConfig.deps env request
            ActiveSubject=fun guestId -> promise {
                let! subject=Identity.Server.activeSubject env.DB guestId
                return subject |> Option.map(fun (provider,id)->{Hedge.AccessControl.Subject.Provider=provider;ProviderUserId=id}) }
            HasGrant=Identity.Grants.hasGrant env.DB }
        let! result=Hedge.AccessControl.requireRole deps "curator" (readCookie request)
        match result with
        | Hedge.AccessControl.Authorized(_,cookie) -> return true,cookie
        | Hedge.AccessControl.Forbidden(_,cookie) | Hedge.AccessControl.AuthRequired cookie -> return false,cookie
}

let noteDto (r:obj) : Note =
    {Id=r?id;Text=r?text;Correction=(unbox<int> r?is_correction)=1;Revision=r?revision
     Read=not(isNull r?reviewed_revision) && r?reviewed_revision=r?revision;CreatedAt=r?created_at}
let photoDto (r:obj) : Photo =
    let id:string=r?id
    {Id=id;Image="/api/plants/personal-media/"+id+"/image";Thumbnail="/api/plants/personal-media/"+id+"/thumbnail"
     Caption=r?caption;Photographer=r?photographer;Width=r?width;Height=r?height
     Offered=(unbox<int> r?offered)=1;Revision=r?revision
     PublicPhotoId=if isNull r?published_photo_id then "" else r?published_photo_id}

// These predicates serve both reported usage and the atomic insert guards. Future
// admin lifecycle rules must change them together; review/offer/promotion do not
// release a slot today. A reserved upload counts before either R2 object is written.
let private noteSlot = "plant_id=? AND owner_provider=? AND owner_id=? AND deleted_at IS NULL"
let private photoSlot = "plant_id=? AND owner_provider=? AND owner_id=? AND deleted_at IS NULL"
let private noteLimit = 5
let private photoLimit = 5
let private slotArgs plantId who = Array.append [|box plantId|] (ownerArgs who)
let private capacities env plantId who = promise {
    let args=slotArgs plantId who
    let! counts=first env ("SELECT (SELECT COUNT(*) FROM plant_notes WHERE "+noteSlot+") AS notes, (SELECT COUNT(*) FROM personal_plant_photos WHERE "+photoSlot+") AS photos") (Array.append args args)
    let counts=counts.Value
    return {Used=counts?notes;Limit=noteLimit},{Used=counts?photos;Limit=photoLimit}
}
let private noteLimitResponse cookie = error cookie 409 (sprintf "You can keep %i notes per species. Delete an existing note before adding another." noteLimit)
let private photoLimitResponse cookie = error cookie 409 (sprintf "You can keep %i photos per species. Delete one of your existing photos before adding another." photoLimit)

let personalData env plantId who = promise {
    let args=Array.append [|box plantId|] (ownerArgs who)
    let! notes=rows env "SELECT * FROM plant_notes WHERE plant_id=? AND owner_provider=? AND owner_id=? AND deleted_at IS NULL ORDER BY created_at DESC,id" args
    let! photos=rows env "SELECT * FROM personal_plant_photos WHERE plant_id=? AND owner_provider=? AND owner_id=? AND deleted_at IS NULL AND ready=1 ORDER BY created_at,id" args
    let! pref=first env "SELECT hero_photo_id FROM plant_view_preferences WHERE plant_id=? AND owner_provider=? AND owner_id=?" args
    let hero=pref |> Option.map(fun r -> if isNull r?hero_photo_id then "" else unbox<string> r?hero_photo_id) |> Option.defaultValue ""
    let! token=viewerToken who.Provider who.Id
    let! noteCapacity,photoCapacity=capacities env plantId who
    return {Anonymous=who.Provider="guest";ViewerToken=token;Notes=Array.map noteDto notes;Photos=Array.map photoDto photos;HeroPhotoId=hero;NoteCapacity=noteCapacity;PhotoCapacity=photoCapacity}
}

let personal env plantId who = promise {
    let! data=personalData env plantId who
    return response who.Cookie 200 data
}

let checkedText name limit (text:string) =
    if isNull text then invalid ("Missing "+name+".")
    let text=text.Trim()
    if text.Length>limit then invalid (name+" is too long.")
    text
let checkedRevision revision =
    if not(isInt(box revision)) || revision<0 then invalid "Missing revision."

let mutateCommand env plantId who command onSuccess = promise {
    let id,revision =
        match command with
        | SaveNote(id,revision,_,_) | DeleteNote(id,revision)
        | UpdatePhoto(id,revision,_,_,_) | DeletePhoto(id,revision) -> id,revision
        | SelectHero id -> id,0
    if id<>"" && not(validId id) then invalid "Invalid item."
    checkedRevision revision
    let args=Array.append [|box id;box plantId|] (ownerArgs who)
    let guard=ownerArgs who
    let now=box(epochNow())
    let! changed=promise {
        match command with
        | SaveNote(_,_,value,isCorrection) ->
            let text=checkedText "Text" 6000 value
            if text="" then invalid "Write a note before saving."
            let correction=if isCorrection then 1 else 0
            if id="" then invalid "Missing note ID."
            if revision=0 then
                return! run env ("INSERT OR IGNORE INTO plant_notes (id,plant_id,owner_provider,owner_id,text,is_correction,revision,reviewed_revision,created_at,updated_at,deleted_at) SELECT ?,?,?,?,?,?,1,NULL,?,NULL,NULL WHERE "+visiblePlant+claimGuard+" AND (SELECT COUNT(*) FROM plant_notes WHERE "+noteSlot+")<? AND (SELECT COUNT(*) FROM plant_notes WHERE owner_provider=? AND owner_id=? AND deleted_at IS NULL)<2000")
                    (Array.concat [args;[|box text;box correction;now;box plantId|];guard;slotArgs plantId who;[|box noteLimit|];guard])
            else
                return! run env ("UPDATE plant_notes SET text=?,is_correction=?,revision=revision+1,updated_at=? WHERE "+owned+" AND revision=? AND deleted_at IS NULL"+claimGuard)
                    (Array.concat [[|box text;box correction;now|];args;[|box revision|];guard])
        | DeleteNote _ ->
            return! run env ("UPDATE plant_notes SET deleted_at=?,revision=revision+1 WHERE "+owned+" AND revision=? AND deleted_at IS NULL"+claimGuard)
                (Array.concat [[|now|];args;[|box revision|];guard])
        | UpdatePhoto(_,_,caption,photographer,isOffered) ->
            let caption=checkedText "Caption" 500 caption
            let photographer=checkedText "Photographer" 160 photographer
            let offered=if isOffered then 1 else 0
            return! run env ("UPDATE personal_plant_photos SET caption=?,photographer=?,offered=?,revision=revision+1,updated_at=? WHERE "+owned+" AND revision=? AND ready=1 AND deleted_at IS NULL AND (published_photo_id IS NULL OR ?=1)"+claimGuard)
                (Array.concat [[|box caption;box photographer;box offered;now|];args;[|box revision;box offered|];guard])
        | SelectHero _ ->
            if id<>"" then
                let! photo=first env ("SELECT 1 FROM personal_plant_photos WHERE "+owned+" AND ready=1 AND deleted_at IS NULL") args
                if photo.IsNone then invalid "Choose one of your photographs of this plant."
            let photoId=if id="" then null else box id
            return! run env ("INSERT INTO plant_view_preferences (id,plant_id,owner_provider,owner_id,hero_photo_id) SELECT ?,?,?,?,? WHERE "+visiblePlant+claimGuard+" ON CONFLICT(owner_provider,owner_id,plant_id) DO UPDATE SET hero_photo_id=excluded.hero_photo_id")
                (Array.concat [[|box(newId());box plantId|];guard;[|photoId;box plantId|];guard])
        | DeletePhoto _ ->
            let! photo=first env ("SELECT * FROM personal_plant_photos WHERE "+owned+" AND revision=?"+claimGuard) (Array.concat [args;[|box revision|];guard])
            match photo with
            | None -> return false
            | Some p ->
                // Hide first; a failed R2 delete leaves keys and bytes tracked for retry.
                let! hidden=run env ("UPDATE personal_plant_photos SET deleted_at=? WHERE "+owned+" AND revision=?"+claimGuard) (Array.concat [[|now|];args;[|box revision|];guard])
                if not hidden then return false
                else
                    do! env.BLOBS.delete(p?image_key)
                    do! env.BLOBS.delete(p?thumbnail_key)
                    let! _=run env "UPDATE personal_plant_photos SET stored_bytes=0,ready=0 WHERE id=? AND deleted_at IS NOT NULL" [|box id|]
                    let! _=run env "UPDATE plant_view_preferences SET hero_photo_id=NULL WHERE hero_photo_id=?" [|box id|]
                    return true
    }
    if changed then
        let! data=personalData env plantId who
        return onSuccess data
    elif (match command with SaveNote(_,0,_,_) -> true | _ -> false) then
        let! notes,_=capacities env plantId who
        if notes.Used>=notes.Limit then return noteLimitResponse who.Cookie
        else return conflict who.Cookie
    else return conflict who.Cookie
}

// V1 is retained for cached clients for one compatibility release. All writes share
// the same typed commands, ownership guards, revisions, quotas and SQL as v2.
let mutate env request plantId who = promise {
    let! body=promise {try return! readJson request with ex -> return invalid ex.Message}
    let action=textField "Action" 30 body
    let id=textField "Id" 100 body
    let revision=revisionField body
    let command =
        match action with
        | "saveNote" -> SaveNote(id,revision,textField "Text" 6000 body,boolField "Correction" body)
        | "deleteNote" -> DeleteNote(id,revision)
        | "savePhoto" -> UpdatePhoto(id,revision,textField "Caption" 500 body,textField "Photographer" 160 body,boolField "Offered" body)
        | "deletePhoto" -> DeletePhoto(id,revision)
        | "hero" -> SelectHero id
        | _ -> invalid "Unknown contribution action."
    return! mutateCommand env plantId who command (response who.Cookie 200)
}

let upload env request plantId id who = promise {
    if not(validId id) then invalid "Invalid photograph ID."
    let! existing=first env "SELECT * FROM personal_plant_photos WHERE id=?" [|box id|]
    match existing with
    | Some p when p?owner_provider=who.Provider && p?owner_id=who.Id && p?plant_id=plantId && (unbox<int> p?ready)=1 && isNull p?deleted_at ->
        return! personal env plantId who
    | Some _ -> return conflict who.Cookie
    | None ->
        let! file=promise {try return! readUpload request with ex -> return invalid ex.Message}
        let key="private/native-plants/"+newId()
        let imageKey=key+"/image.jpg"
        let thumbKey=key+"/thumbnail.jpg"
        let! reserved =
            run env ("INSERT OR IGNORE INTO personal_plant_photos (id,plant_id,owner_provider,owner_id,image_key,thumbnail_key,width,height,stored_bytes,caption,photographer,offered,ready,revision,published_photo_id,created_at,updated_at,deleted_at) SELECT ?,?,?,?,?,?,?,?,?,'','',0,0,1,NULL,?,NULL,NULL WHERE "+visiblePlant+claimGuard+" AND (SELECT COUNT(*) FROM personal_plant_photos WHERE "+photoSlot+")<? AND (SELECT COUNT(*) FROM personal_plant_photos WHERE owner_provider=? AND owner_id=? AND (deleted_at IS NULL OR stored_bytes>0))<100 AND (SELECT COALESCE(SUM(stored_bytes),0) FROM personal_plant_photos WHERE owner_provider=? AND owner_id=?)+?<=262144000")
                (Array.concat [[|box id;box plantId|];ownerArgs who;[|box imageKey;box thumbKey;box file.width;box file.height;box file.size;box(epochNow());box plantId|];ownerArgs who;slotArgs plantId who;[|box photoLimit|];ownerArgs who;ownerArgs who;[|box file.size|]])
        if not reserved then
            let! _,photos=capacities env plantId who
            if photos.Used>=photos.Limit then return photoLimitResponse who.Cookie
            else return error who.Cookie 409 "Upload limit reached or this session changed. Each account can keep 100 photographs, up to 250 MB."
        else
            do! promise {
                try
                    let! _=putImage env.BLOBS imageKey file.image
                    let! _=putImage env.BLOBS thumbKey file.thumbnail
                    let! _=run env "UPDATE personal_plant_photos SET ready=1 WHERE id=? AND deleted_at IS NULL" [|box id|]
                    return ()
                with ex ->
                    // A failed upload is not a notebook item. Release its species slot while
                    // retaining keys/bytes against account storage limits until cleanup succeeds.
                    let! _=run env "UPDATE personal_plant_photos SET deleted_at=? WHERE id=? AND ready=0" [|box(epochNow());box id|]
                    try
                        do! env.BLOBS.delete imageKey
                        do! env.BLOBS.delete thumbKey
                        let! _=run env "DELETE FROM personal_plant_photos WHERE id=? AND ready=0" [|box id|]
                        ()
                    with _ -> ()
                    return raise ex
            }
            // Response/count reads must not roll back an already completed upload.
            return! personal env plantId who
}

let media env request id size = promise {
    let! p=first env "SELECT c.* FROM personal_plant_photos c JOIN plants p ON p.id=c.plant_id WHERE c.id=? AND c.ready=1 AND c.deleted_at IS NULL AND p.published=1 AND p.deleted_at IS NULL" [|box id|]
    match p with
    | None -> return error None 404 "Photograph unavailable."
    | Some p ->
        let! who,_=owner env request
        let own=who |> Option.exists(fun who->p?owner_provider=who.Provider && p?owner_id=who.Id)
        let! canReview=promise {
            if own || (unbox<int> p?offered)<>1 then return false
            else let! allowed,_=reviewer env request in return allowed }
        if not(own || canReview) then return error None 404 "Photograph unavailable."
        else
            let key=if size="image" then p?image_key else p?thumbnail_key
            let! blob=env.BLOBS.get key
            match blob with
            | None -> return error None 404 "Photograph unavailable."
            | Some blob ->
                // Image loads cannot join the browser's logout lock. Do not renew cookies here.
                return streamResponse blob.body (createObj ["headers" ==> createObj [
                    "Content-Type" ==> "image/jpeg";"Cache-Control" ==> "private, no-store"
                    "Vary" ==> "Cookie, X-Admin-Key";"X-Content-Type-Options" ==> "nosniff"]])
}

let reviewData env page = promise {
    let page=max 0 (min 10000 page)
    let! notes=rows env "SELECT n.*,p.scientific_name FROM plant_notes n JOIN plants p ON p.id=n.plant_id WHERE n.deleted_at IS NULL AND n.is_correction=1 AND p.published=1 AND p.deleted_at IS NULL ORDER BY (n.reviewed_revision=n.revision) IS 1,n.created_at DESC,n.id LIMIT 51 OFFSET ?" [|box(page*50)|]
    let! photos=rows env "SELECT c.*,p.scientific_name FROM personal_plant_photos c JOIN plants p ON p.id=c.plant_id WHERE c.deleted_at IS NULL AND c.ready=1 AND c.offered=1 AND c.published_photo_id IS NULL AND p.published=1 AND p.deleted_at IS NULL ORDER BY c.created_at,c.id LIMIT 51 OFFSET ?" [|box(page*50)|]
    return {
        Notes=notes |> Array.truncate 50 |> Array.map(fun n->{PlantId=n?plant_id;PlantName=n?scientific_name;Note=noteDto n})
        Photos=photos |> Array.truncate 50 |> Array.map(fun p->{PlantId=p?plant_id;PlantName=p?scientific_name;Photo=photoDto p})
        Page=page;HasMore=notes.Length>50 || photos.Length>50 }
}

let reviewQueue env request cookie = promise {
    let! data=reviewData env (pageNumber request)
    return response cookie 200 data
}

let promote env id revision = promise {
    let! row=first env "SELECT c.* FROM personal_plant_photos c JOIN plants p ON p.id=c.plant_id WHERE c.id=? AND c.ready=1 AND c.deleted_at IS NULL AND c.offered=1 AND p.published=1 AND p.deleted_at IS NULL" [|box id|]
    match row with
    | None -> return false
    | Some p when not(isNull p?published_photo_id) -> return true
    | Some p when p?revision<>revision -> return false
    | Some p ->
        let! image=env.BLOBS.get(p?image_key)
        let! thumb=env.BLOBS.get(p?thumbnail_key)
        match image,thumb with
        | Some image,Some thumb ->
            // A separate immutable public copy survives deletion of the contributor's private copy.
            let root="native-plants/contributed/"+newId()
            let imageKey=root+"/image.jpg"
            let thumbKey=root+"/thumbnail.jpg"
            let publicId="contributed-"+id
            try
                let! _=putImage env.BLOBS imageKey image.body
                let! _=putImage env.BLOBS thumbKey thumb.body
                let! _=env.DB.batch [|
                    stmt env ("INSERT OR IGNORE INTO plant_photos (id,plant_id,image,thumbnail,caption,photographer,sort_order,published,source_evidence,created_at,updated_at,deleted_at) SELECT ?,c.plant_id,?,?,c.caption,c.photographer,MAX(1,(SELECT COALESCE(MAX(sort_order),0)+1 FROM plant_photos WHERE plant_id=c.plant_id)),1,?, ?,NULL,NULL FROM personal_plant_photos c WHERE c.id=? AND c.revision=? AND c.offered=1 AND c.ready=1 AND c.deleted_at IS NULL AND c.published_photo_id IS NULL AND EXISTS(SELECT 1 FROM plants p WHERE p.id=c.plant_id AND p.published=1 AND p.deleted_at IS NULL)")
                        [|box publicId;box("/blobs/"+imageKey);box("/blobs/"+thumbKey);box("Contribution "+id);box(epochNow());box id;box revision|]
                    stmt env "UPDATE personal_plant_photos SET published_photo_id=? WHERE id=? AND EXISTS(SELECT 1 FROM plant_photos WHERE id=?)"
                        [|box publicId;box id;box publicId|]
                |]
                let! published=first env "SELECT image FROM plant_photos WHERE id=?" [|box publicId|]
                if published |> Option.exists(fun r->r?image="/blobs/"+imageKey) then
                    Server.CatalogueStore.invalidate env
                    return true
                else
                    do! env.BLOBS.delete imageKey
                    do! env.BLOBS.delete thumbKey
                    return published.IsSome
            with ex ->
                // Never remove a copy after a successful commit, even if the final read failed.
                let! committed=first env "SELECT 1 FROM plant_photos WHERE image=?" [|box("/blobs/"+imageKey)|]
                if committed.IsNone then
                    do! env.BLOBS.delete imageKey
                    do! env.BLOBS.delete thumbKey
                return raise ex
        | _ -> return false
}

let reviewCommand env cookie page command onSuccess = promise {
    let id,revision=match command with CorrectionRead(id,revision,_) | PromotePhoto(id,revision) -> id,revision
    if not(validId id) then invalid "Invalid item."
    checkedRevision revision
    let! changed=promise {
        match command with
        | CorrectionRead(_,_,read) ->
            let reviewed=if read then box revision else null
            return! run env "UPDATE plant_notes SET reviewed_revision=? WHERE id=? AND revision=? AND is_correction=1 AND deleted_at IS NULL AND EXISTS(SELECT 1 FROM plants p WHERE p.id=plant_id AND p.published=1 AND p.deleted_at IS NULL)" [|reviewed;box id;box revision|]
        | PromotePhoto _ -> return! promote env id revision
    }
    if changed then
        let! data=reviewData env page
        return onSuccess data
    else return conflict cookie
}

let reviewMutation env request cookie = promise {
    let! body=promise {try return! readJson request with ex -> return invalid ex.Message}
    let id=textField "Id" 100 body
    let revision=revisionField body
    let command =
        match textField "Action" 30 body with
        | "read" -> CorrectionRead(id,revision,true)
        | "unread" -> CorrectionRead(id,revision,false)
        | "promote" -> PromotePhoto(id,revision)
        | _ -> invalid "Unknown review action."
    return! reviewCommand env cookie (pageNumber request) command (response cookie 200)
}

let dispatch request env =
    let bits=path request
    let isRoute=bits.Length>=3 && bits.[0]="api" && bits.[1]="plants" && List.contains bits.[2] ["personal";"personal-media";"review";"access"]
    if not isRoute then None else Some(promise {
        try
            if request.method<>"GET" && (request.method<>"POST" || not(sameOrigin request)) then
                return error None 403 "Use this site to change your contributions."
            else
                match Array.toList bits with
                | ["api";"plants";"personal-media";id;size] when request.method="GET" && List.contains size ["image";"thumbnail"] ->
                    return! media env request id size
                | ["api";"plants";"access"] when request.method="GET" ->
                    let! allowed,cookie=reviewer env request
                    let access:Capabilities={CanEditCatalogue=Server.AuthConfig.isOwner env request;CanReview=allowed}
                    return response cookie 200 access
                | ["api";"plants";"review"] ->
                    let! allowed,cookie=reviewer env request
                    if not allowed then return error cookie 403 "Curator access or the site admin key is required."
                    elif request.method="GET" then return! reviewQueue env request cookie
                    else return! reviewMutation env request cookie
                | "api"::"plants"::"personal"::plantId::rest ->
                    let! who,cookie=owner env request
                    match who with
                    | None -> return error cookie 401 "Your session is unavailable. Refresh the page or log in to continue."
                    | Some who ->
                        let! plant=first env ("SELECT 1 WHERE "+visiblePlant) [|box plantId|]
                        if plant.IsNone then return error cookie 404 "This plant is unavailable."
                        else
                            let! token=viewerToken who.Provider who.Id
                            if request.method="POST" && getHeader request "X-Contribution-Viewer"<>token then
                                return error cookie 409 "Your session changed. Refresh before saving this contribution."
                            else
                              match request.method,rest with
                              | "GET",[] -> return! personal env plantId who
                              | "POST",[] -> return! mutate env request plantId who
                              | "POST",["photos";id] -> return! upload env request plantId id who
                              | _ -> return error cookie 404 "Unknown contribution endpoint."
                | _ -> return error None 404 "Unknown contribution endpoint."
        with InvalidInput message -> return error None 400 message
    })
