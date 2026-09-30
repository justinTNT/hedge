module Server.MobileHandlers

open Fable.Core
open Fable.Core.JsInterop
open Hedge.Workers
open Hedge.Router
open Hedge.GuestSession
open Server.Env

// Host policy only: shared Hedge resolves credentials; Identity.Mobile owns persistence.
// Plants ownership stays provider/account based, exactly as on the website.
let private ttl = 2592000
let private callback = "nativeplants://auth"
[<Import("readJson", "./contribution-media.mjs")>]
let private readJson (request:WorkerRequest) : JS.Promise<obj> = jsNative
[<Emit("typeof $0 === 'string' && /^[a-f0-9]{64}$/.test($0)")>]
let private validProof (value:obj) : bool = jsNative
[<Emit("typeof $0 === 'string' && $0.length > 0 && $0.length <= 128")>]
let private validCode (value:obj) : bool = jsNative
let private tokenResponse token = jsonResponse (JS.JSON.stringify {| token=token |}) 200

let bootstrap env = promise {
    let now=epochNow()
    do! Identity.Mobile.purgeExpired env.DB now
    let! token=Identity.Mobile.mintSession env.DB (newId()) now ttl
    return tokenResponse token
}
let me request env = promise {
    match! Hedge.MobileSession.resolve (Server.AuthConfig.mobileDeps env) request with
    | Hedge.MobileSession.Valid guestId ->
        let! identity=Server.AuthConfig.resolveIdentity env.DB guestId
        match identity with
        | Some json -> return jsonResponse (sprintf """{"guest":{"guestId":"%s","identity":%s}}""" guestId json) 200
        | None -> return jsonResponse """{"guest":null}""" 200
    | _ -> return unauthorized()
}
let returnToApp (request:WorkerRequest) env = promise {
    let challenge=getQueryParam request.url "challenge"
    // Only the OAuth browser's signed cookie can authorize the handoff.
    let! guest=requireGuest (Server.AuthConfig.deps env request) (readCookie request)
    match guest with
    | Accepted accepted when validProof (box challenge) ->
        let! subject=Identity.Server.activeSubject env.DB accepted.GuestId
        match subject with
        | Some _ ->
            let! code=Identity.Mobile.mintCode env.DB accepted.GuestId challenge (epochNow())
            return redirectResponseOpt (callback+"?code="+code) accepted.Replacement
        | None -> return redirectResponseOpt (callback+"?error=identity") accepted.Replacement
    | _ -> return redirectResponseOpt (callback+"?error=session") None
}
let exchange request env = promise {
    let! parsed=promise {
        try let! data=readJson request in return Some data
        with _ -> return None }
    match parsed with
    | Some data when validCode data?code && validProof data?verifier ->
        let! consumed=Identity.Mobile.consumeCode env.DB (epochNow()) data?code data?verifier
        match consumed with
        | Some guestId ->
            // Recheck identity after consuming: a disconnected/deleted account cannot sign in.
            let! subject=Identity.Server.activeSubject env.DB guestId
            match subject with
            | None -> return unauthorized()
            | Some(provider,id) ->
                match! Hedge.MobileSession.resolve (Server.AuthConfig.mobileDeps env) request with
                | Hedge.MobileSession.Valid previous when previous<>guestId ->
                    let! oldSubject=Identity.Server.activeSubject env.DB previous
                    if oldSubject.IsNone then do! Server.ContributionOwnership.claim env.DB previous provider id
                | _ -> ()
                match Hedge.MobileSession.readBearer request with
                | Some previous ->
                    let! hash=Hedge.MobileSession.sha256Hex previous
                    do! Identity.Mobile.revokeByHash env.DB hash
                | None -> ()
                let! token=Identity.Mobile.mintSession env.DB guestId (epochNow()) ttl
                return tokenResponse token
        | None -> return unauthorized()
    | _ -> return badRequest "Invalid sign-in handoff."
}
let signout request env = promise {
    match Hedge.MobileSession.readBearer request with
    | Some token ->
        let! hash=Hedge.MobileSession.sha256Hex token
        do! Identity.Mobile.revokeByHash env.DB hash
    | None -> ()
    return jsonResponse """{"ok":true}""" 200
}
let dispatch (request:WorkerRequest) (env:Env) =
    let path=(System.Uri request.url).AbsolutePath
    match request.method,path with
    | "POST","/api/mobile/bootstrap" -> Some(bootstrap env)
    | "GET","/api/mobile/me" -> Some(me request env)
    | "GET","/api/mobile/return" -> Some(returnToApp request env)
    | "POST","/api/mobile/exchange" -> Some(exchange request env)
    | "POST","/api/mobile/signout" -> Some(signout request env)
    | _ -> None
