module Server.ContributionApi

open Fable.Core
open Fable.Core.JsInterop
open Hedge.Workers
open Models.Api
open Models.Contributions
open Server.Contributions

[<Import("boundedJsonRequest", "./contribution-media.mjs")>]
let private boundedJsonRequest (request:WorkerRequest) : JS.Promise<WorkerRequest> = jsNative

let personalSnapshot (data:Personal) : PersonalSnapshot =
    { Anonymous=data.Anonymous; ViewerToken=data.ViewerToken; Notes=Array.toList data.Notes
      Photos=Array.toList data.Photos; HeroPhotoId=data.HeroPhotoId
      NoteCapacity=data.NoteCapacity; PhotoCapacity=data.PhotoCapacity }
let reviewSnapshot (data:Review) : ReviewSnapshot =
    { Notes=Array.toList data.Notes; Photos=Array.toList data.Photos; Page=data.Page; HasMore=data.HasMore }

let decorate cookie (result:WorkerResponse) =
    result?headers?set("Cache-Control","private, no-store") |> ignore
    result?headers?set("Vary","Cookie, X-Admin-Key") |> ignore
    cookie |> Option.iter(fun c->result?headers?set("Set-Cookie",c) |> ignore)
    result

let withOwner request env plantId action = promise {
    let! who,cookie=owner env request
    match who with
    | None -> return error cookie 401 "Your session is unavailable. Refresh the page or log in to continue."
    | Some who ->
        try
            if not(validId plantId) then invalid "Invalid plant."
            let! plant=first env ("SELECT 1 WHERE "+visiblePlant) [|box plantId|]
            if plant.IsNone then return error cookie 404 "This plant is unavailable."
            else
                let! token=viewerToken who.Provider who.Id
                if request.method="POST" && getHeader request "X-Contribution-Viewer"<>token then
                    return error cookie 409 "Your session changed. Refresh before saving this contribution."
                else return! action who
        with InvalidInput message -> return error cookie 400 message
}

let withReviewer request env action = promise {
    let! allowed,cookie=reviewer env request
    if not allowed then return error cookie 403 "Curator access or the site admin key is required."
    else
        try return! action cookie
        with InvalidInput message -> return error cookie 400 message
}

let withReviewAccess request env action = promise {
    let! curate,cookie=reviewer env request
    let! identify,renewal=identifier env request
    let cookie=Option.orElse cookie renewal
    if not(curate || identify) then return error cookie 403 "A curator or identifier grant, or the site admin key, is required."
    else
        try return! action cookie curate identify
        with InvalidInput message -> return error cookie 400 message
}

let withIdentifier request env action = promise {
    let! allowed,cookie=identifier env request
    if not allowed then return error cookie 403 "Identifier access or the site admin key is required."
    else
        try return! action cookie
        with InvalidInput message -> return error cookie 400 message
}

// The generated POST decoder uses request.text(). Authorize before consuming the
// untrusted stream, cap it at 24,000 bytes, and pass only that bounded request on.
// Never clone/tee the original stream. The handlers recheck subject/plant ownership.
let dispatch request env ctx routes =
    let bits=path request |> Array.toList
    match bits with
    | "api"::"plants"::"v2"::rest -> Some(promise {
        if request.method="GET" then
            match routes request env ctx with
            | Some result -> let! r=result in return decorate None r
            | None -> return error None 404 "Unknown contribution endpoint."
        elif request.method<>"POST" || not(sameOrigin request) then
            return error None 403 "Use this site to change your contributions."
        else
            let routeKind =
                match rest with
                | ["notes";"entry"] | ["notes";"save"] | ["notes";"delete"] | ["photos";"update"] | ["photos";"delete"] | ["hero"] -> 1
                | ["review";"identify"] -> 3
                | ["review";"correction"] | ["review";"promote"] -> 2
                | _ -> 0
            let! allowed,cookie,status=promise {
                match routeKind with
                | 1 ->
                    let! who,cookie=owner env request
                    return who.IsSome,cookie,401
                | 2 -> let! allowed,cookie=reviewer env request in return allowed,cookie,403
                | 3 -> let! allowed,cookie=identifier env request in return allowed,cookie,403
                | _ -> return false,None,404 }
            if not allowed then return error cookie status (if status=404 then "Unknown contribution endpoint." else "Contribution access is required.")
            else
                let! bounded=promise {
                    try let! bounded=boundedJsonRequest request in return Ok bounded
                    with ex -> return Error ex.Message }
                match bounded with
                | Error message -> return error cookie 400 message
                | Ok bounded ->
                    match routes bounded env ctx with
                    | Some result -> let! r=result in return decorate cookie r
                    | None -> return error cookie 404 "Unknown contribution endpoint."
    })
    | _ -> None
