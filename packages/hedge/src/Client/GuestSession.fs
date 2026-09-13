module Client.GuestSession

// The framework's shared guest-session accessor over window.HedgeGuest (see
// lib/guest-session.js). One source, `<Compile Include>`'d by apps that carry
// identities; content modules reference it as `Client.GuestSession` (aliased
// locally as `module GuestSession = Client.GuestSession`, mirroring RichText).
// Do not copy per app. (music has a slimmer, divergent copy of its own.)

open Fable.Core
open Fable.Core.JsInterop

type IdentityData = {
    Id: string
    Provider: string
    Name: string
    Picture: string
}

type GuestSessionData = {
    GuestId: string
    DisplayName: string
    AvatarHex: string
    AvatarChar: string
    AvatarUrl: string
    Identity: IdentityData option
}

[<Emit("window.HedgeGuest.getSession()")>]
let private getRawSession () : obj = jsNative

[<Emit("$0 || ''")>]
let private orEmpty (x: obj) : string = jsNative

[<Emit("window.HedgeGuest.avatarForAuthor($0)")>]
let avatarForAuthor (author: string) : string = jsNative

[<Emit("$0 == null")>]
let private isJsNull (o: obj) : bool = jsNative

let private parseIdentity (raw: obj) : IdentityData option =
    let id = raw?identity
    if isJsNull id then None
    else Some { Id = id?id; Provider = id?provider; Name = id?name; Picture = orEmpty id?picture }

let getSession () : GuestSessionData =
    let raw = getRawSession ()
    { GuestId = raw?guestId
      DisplayName = raw?displayName
      AvatarHex = orEmpty raw?avatarHex
      AvatarChar = orEmpty raw?avatarChar
      AvatarUrl = orEmpty raw?avatarUrl
      Identity = parseIdentity raw }

[<Emit("window.HedgeGuest.syncSession()")>]
let private rawSyncSession () : JS.Promise<obj> = jsNative

let syncSession () : JS.Promise<GuestSessionData> =
    promise {
        let! raw = rawSyncSession ()
        return { GuestId = raw?guestId
                 DisplayName = raw?displayName
                 AvatarHex = orEmpty raw?avatarHex
                 AvatarChar = orEmpty raw?avatarChar
                 AvatarUrl = orEmpty raw?avatarUrl
                 Identity = parseIdentity raw }
    }
