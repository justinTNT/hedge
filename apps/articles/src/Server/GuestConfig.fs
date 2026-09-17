module Server.GuestConfig

// The one place this app binds its environment + database adapters into the shared guest-session
// policy (Hedge.GuestSession). Three consumers share this ONE policy: the framework router
// (WorkerConfig.GuestSession — /api/auth/me + OAuth), the content modules (their Services.Guest —
// comment writes), and this app's identity handlers. Audience = the request host; the cookie is
// host-only, so that is stable per deployment and adds a cross-host verification check.
//
// slice C: Bridge is HardCutover for every current deployment — guest ids are public in
// comment/<guestId>/… upload object keys, so an unsigned legacy value must never be re-signed. The
// legacy-eligibility adapters are wired here so slice F can turn on a per-deployment Bridge from
// configuration without touching this policy; under HardCutover they are never consulted.

open Fable.Core
open Fable.Core.JsInterop
open Hedge.Workers
open Server.Env

[<Emit("new URL($0.url).hostname")>]
let private hostOf (request: WorkerRequest) : string = jsNative

/// Active signing key id. Key rotation appends retiring previous keys to configFor's last argument.
let [<Literal>] private KeyId = "k1"

/// Bind the shared guest-session policy for this request. Fails closed (configFor throws) when
/// GUEST_SECRET is absent/short — only reached on a guest operation, never on a content read.
let deps (env: Env) (request: WorkerRequest) : Hedge.GuestSession.Deps =
    { Config = Hedge.GuestSession.configFor KeyId env.GUEST_SECRET (hostOf request) []
      Bridge = Hedge.GuestSession.HardCutover
      // Secure everywhere except an explicit local-HTTP development environment (work order).
      Secure = (env.ENVIRONMENT <> "development")
      Now = epochNow
      NewGuestId = newId
      // migrationStart 0 → created_at < 0 is never true, so an accidental Bridge without a configured
      // start upgrades nothing. slice F supplies the real absolute start alongside the Bridge window.
      LegacyEligible = (fun value -> Server.Identity.legacyEligible env.DB value 0)
      LegacyHasLinkedIdentity = (fun guestId -> Server.Identity.hasLinkedIdentity env.DB guestId) }

/// Resolve the guest for a WRITE (comment / identity mutation): verified or bridge-authorized, else
/// Rejected. Never creates a guest — that is the bootstrap path's job (router /api/auth/me).
let require (env: Env) (request: WorkerRequest) : JS.Promise<Hedge.GuestSession.RequireResult> =
    Hedge.GuestSession.requireGuest (deps env request) (Hedge.GuestSession.readCookie request)
