module Client.GuestSession

open Fable.Core
open Fable.Core.JsInterop

type GuestSessionData = { GuestId: string; DisplayName: string }

[<Emit("window.HedgeGuest.getSession()")>]
let private getRawSession () : obj = jsNative

let getSession () : GuestSessionData =
    let raw = getRawSession ()
    { GuestId = raw?guestId; DisplayName = raw?displayName }
