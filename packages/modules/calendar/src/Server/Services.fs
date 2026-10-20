module Calendar.Services

// The capabilities the calendar module's handlers consume, supplied by the host (its
// Server.ModuleServices adapts the app's Env + identity layer). The module never sees Server.Env,
// a cookie, a secret, or the identity tables: "who is asking" arrives resolved. Kept small — a
// field exists because a handler uses it.

open Fable.Core
open Hedge.Workers

/// Who is asking: the signed-in identity (the owner key for personal entries), if any, plus a
/// renewal cookie the host's session policy wants echoed on the response.
type Viewer = { IdentityId: string option; Replacement: string option }

type Services =
    { /// The tenant D1 database.
      DB: D1Database
      /// Resolve the request's viewer. A host that composes no identity layer supplies `guestOnly`:
      /// everyone reads the public stream and personal entries are simply off.
      Viewer: WorkerRequest -> JS.Promise<Viewer>
      /// Fresh id generator (framework `newId` by default).
      NewId: unit -> string
      /// Clock (framework `epochNow` by default).
      Now: unit -> int }

/// The viewer resolver for an identity-less host: everyone is a guest.
let guestOnly : WorkerRequest -> JS.Promise<Viewer> =
    fun _ -> promise { return { IdentityId = None; Replacement = None } }
