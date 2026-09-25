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

/// The guest's generated anonymous pseudonym (the new-guest formula) for its own id — used as the
/// fallback name when disconnecting a provider drops back to anonymous, so the stored anonymous
/// identity matches the displayed one (and its avatar, which is derived from the name).
[<Emit("window.HedgeGuest.anonName()")>]
let anonName () : string = jsNative

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

let private parseSession (raw: obj) : GuestSessionData =
    { GuestId = raw?guestId
      DisplayName = raw?displayName
      AvatarHex = orEmpty raw?avatarHex
      AvatarChar = orEmpty raw?avatarChar
      AvatarUrl = orEmpty raw?avatarUrl
      Identity = parseIdentity raw }

let syncSession () : JS.Promise<GuestSessionData> =
    promise {
        let! raw = rawSyncSession ()
        return parseSession raw
    }

/// Explicit session readiness for gating a write (comment/upload). `Ready` is true only when the
/// server bootstrapped/renewed the signed guest cookie on this page; false means the write must not
/// proceed (network/bootstrap failure) — the caller keeps the draft and offers a retry.
type SessionReadiness = { Ready: bool; Session: GuestSessionData }

[<Emit("window.HedgeGuest.ensureSession()")>]
let private rawEnsureSession () : JS.Promise<obj> = jsNative

/// Single-flight bootstrap: awaited before the first comment/upload so the signed cookie exists
/// before the write. Shares one in-flight/succeeded /api/auth/me across callers.
let ensureSession () : JS.Promise<SessionReadiness> =
    promise {
        let! raw = rawEnsureSession ()
        return { Ready = raw?ready; Session = parseSession raw?session }
    }

/// A fresh, server-authoritative read with explicit readiness (unlike display-only syncSession).
[<Emit("window.HedgeGuest.refreshSession()")>]
let private rawRefreshSession () : JS.Promise<obj> = jsNative

let refreshSession () : JS.Promise<SessionReadiness> =
    promise {
        let! raw = rawRefreshSession ()
        return { Ready = raw?ready; Session = parseSession raw?session }
    }

/// Log this browser out; account associations and other devices remain untouched.
[<Emit("window.HedgeGuest.signOut()")>]
let signOut () : JS.Promise<bool> = jsNative

/// Drop the cached bootstrap so the next `ensureSession` re-fetches. Call on a write's 401 (the
/// cookie expired/was cleared/the key changed since bootstrap) so the client re-establishes a
/// session instead of resending the rejected credential until reload.
[<Emit("window.HedgeGuest.invalidateSession()")>]
let invalidateSession () : unit = jsNative

/// Run a typed operation under the browser session lifecycle. The operation owns decoding;
/// the runtime owns cookie serialization and rejects results from an invalidated session.
[<Emit("window.HedgeGuest.withSessionRequest($0)")>]
let withSessionRequest (action: unit -> JS.Promise<'T>) : JS.Promise<'T> = jsNative

/// Optional adapter for generated clients. The inner transport must consume its response body
/// before returning; HTTP and decoder errors remain Hedge.Http's responsibility.
let transport (inner: Hedge.Http.Transport) : Hedge.Http.Transport =
    fun request -> promise {
        try return! withSessionRequest (fun () -> inner request)
        with ex -> return Error (Hedge.Http.TransportFailure ex.Message)
    }
