module Client.GuestSession

open Fable.Core
open Fable.Core.JsInterop

type GuestSessionData = {
    GuestId: string
    DisplayName: string
    AvatarHex: string
    AvatarChar: string
    AvatarUrl: string
}

[<Emit("window.HedgeGuest.getSession()")>]
let private getRawSession () : obj = jsNative

[<Emit("$0 || ''")>]
let private orEmpty (x: obj) : string = jsNative

[<Emit("window.HedgeGuest.avatarForAuthor($0)")>]
let avatarForAuthor (author: string) : string = jsNative

let getSession () : GuestSessionData =
    let raw = getRawSession ()
    { GuestId = raw?guestId
      DisplayName = raw?displayName
      AvatarHex = orEmpty raw?avatarHex
      AvatarChar = orEmpty raw?avatarChar
      AvatarUrl = orEmpty raw?avatarUrl }
