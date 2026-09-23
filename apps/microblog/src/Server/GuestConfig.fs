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

/// Parse an optional absolute-epoch env value; blank/absent/non-numeric → None.
let private parseEpoch (s: string) : int option =
    if isNull (box s) || s = "" then None
    else match System.Int32.TryParse s with | true, n -> Some n | _ -> None

/// Resolve the per-deployment migration mode from configuration. A bridge is enabled ONLY when the
/// deployment sets both an absolute start and an end still in the future (its legacy ids stayed
/// private); every other case — including every deployment that exposed a guest id in an upload key
/// — is a hard cutover. See notes/GUEST-COOKIES-rollout-worksheet.md.
let private migrationMode (env: Env) (now: int) : Hedge.GuestSession.BridgePolicy * int =
    let migrationStart = parseEpoch env.GUEST_MIGRATION_START |> Option.defaultValue 0
    match parseEpoch env.GUEST_BRIDGE_UNTIL with
    | Some endsAt when now < endsAt && migrationStart > 0 -> Hedge.GuestSession.Bridge endsAt, migrationStart
    | _ -> Hedge.GuestSession.HardCutover, migrationStart

/// Bind the shared guest-session policy for this request. Fails closed (configFor throws) when
/// GUEST_SECRET is absent/short — only reached on a guest operation, never on a content read.
let deps (env: Env) (request: WorkerRequest) : Hedge.GuestSession.Deps =
    let bridge, migrationStart = migrationMode env (epochNow ())
    { Config = Hedge.GuestSession.configFor KeyId env.GUEST_SECRET (hostOf request) []
      Bridge = bridge
      // Secure everywhere except an explicit local-HTTP development environment (work order).
      Secure = (env.ENVIRONMENT <> "development")
      Now = epochNow
      NewGuestId = newId
      // migrationStart bounds eligibility to guests created before it; 0 (no bridge) means the query
      // requires created_at < 0, so nothing is ever eligible under an accidental Bridge.
      LegacyEligible = (fun value -> Identity.Server.legacyEligible env.DB value migrationStart)
      LegacyHasLinkedIdentity = (fun guestId -> Identity.Server.hasLinkedIdentity env.DB guestId) }

/// Resolve the guest for a WRITE (comment / identity mutation): verified or bridge-authorized, else
/// Rejected. Never creates a guest — that is the bootstrap path's job (router /api/auth/me).
let require (env: Env) (request: WorkerRequest) : JS.Promise<Hedge.GuestSession.RequireResult> =
    Hedge.GuestSession.requireGuest (deps env request) (Hedge.GuestSession.readCookie request)
