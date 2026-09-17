module Hedge.GuestSession

// Shared guest-session policy — the ONE implementation of "who is this guest, and what cookie do we
// set back", built on the signed envelope (Hedge.GuestCookie) plus injected app primitives (DB
// legacy lookups, clock, id generation). Lives at the Hedge layer because Hedge.Router's own auth
// handlers (/api/auth/me, OAuth) consume it as well as the content modules' comment/upload handlers;
// content-server (Author.fs) sits above Hedge, so the policy can't live there. Apps bind the Deps
// once and expose the module-facing `Service` via their Services record; nobody downstream parses
// the cookie, reads a secret, or hard-codes the policy. A future identity module can move this
// implementation while keeping the contract.

open Fable.Core
open Hedge.Workers
open Hedge.GuestCookie

/// Remembered-guest lifetime and renewal threshold (seconds).
let [<Literal>] LifetimeSeconds = 31536000          // 365 days
let [<Literal>] RenewWithinSeconds = 2592000        // renew a still-valid credential with < 30 days left
let [<Literal>] private CookieName = "hedge_guest"

/// Per-deployment migration policy for unsigned legacy cookies.
type BridgePolicy =
    /// Exposed deployment: unsigned legacy values never authorize (fresh subject on bootstrap).
    | HardCutover
    /// Private deployment: an eligible *anonymous* legacy cookie upgrades in place until endsAt.
    | Bridge of endsAtEpoch: int

/// App-bound primitives, injected so the policy stays pure + fixture-testable.
type Deps =
    { Config: Config
      Bridge: BridgePolicy
      /// Set the Secure cookie attribute (production; false for local HTTP dev).
      Secure: bool
      Now: unit -> int
      NewGuestId: unit -> string
      /// Is this legacy value an eligible existing guest? (a non-deleted guest row created before
      /// migration start whose stored session value matches). App-provided DB lookup — an unknown
      /// value must return false (never conjure a matching row).
      LegacyEligible: string -> JS.Promise<bool>
      /// Does this legacy guest have a linked (claimed) identity? Linked guests do NOT auto-upgrade
      /// — they re-login via verified OAuth adoption; only anonymous guests bridge.
      LegacyHasLinkedIdentity: string -> JS.Promise<bool> }

/// An authorized guest for a write: its subject id + an optional replacement Set-Cookie header.
type Authorized = { GuestId: string; Replacement: string option }

/// Outcome for a WRITE path (comment/upload/identity mutation). Reading never creates a guest.
type RequireResult =
    | Accepted of Authorized
    /// No acceptable credential — reject before side effects; the client must establish a session.
    | Rejected

/// Outcome for a BOOTSTRAP path (/api/auth/me, OAuth start): always a subject; a fresh signed one
/// is minted when nothing acceptable was presented.
type BootstrapResult = { GuestId: string; IsNew: bool; Replacement: string option }

/// Build a validated signing Config, failing CLOSED on weak/missing configuration (work order:
/// "Guest-enabled deployments fail closed on missing signing configuration"; ">= 32 random bytes;
/// no fallback to an admin key, OAuth secret, empty key, or committed development constant"). The
/// >= 32 check is on the raw string length: a hex/base64-encoded 32-byte secret is longer still, so
/// this is a floor, never a false pass. Apps call this from their bound deps builder; it throws when
/// the secret is absent/short so the misconfiguration is loud rather than silently unsigned.
let configFor (keyId: string) (secret: string) (audience: string) (previous: (SigningKey * int) list) : Config =
    if isNull (box secret) || secret.Length < 32 then
        failwith "guest signing: GUEST_SECRET missing or shorter than 32 bytes (fail closed)"
    if isNull (box keyId) || keyId = "" then failwith "guest signing: key id missing"
    if isNull (box audience) || audience = "" then failwith "guest signing: audience missing"
    { Active = { KeyId = keyId; Secret = secret }; Audience = audience; Previous = previous }

/// Read the raw `hedge_guest` cookie value from a request (transport only — interpretation is the
/// policy's job, in `verify`). The single point that names the cookie for reading; issuing names it
/// in `cookieHeader`. Consumers never touch the cookie themselves.
let readCookie (request: WorkerRequest) : string option =
    parseCookie CookieName (getCookieHeader request)

/// The one place cookie attributes are formatted. Secure is set in production.
let cookieHeader (secure: bool) (token: string) : string =
    sprintf "%s=%s; Path=/; HttpOnly; SameSite=Lax; Max-Age=%d%s"
        CookieName token LifetimeSeconds (if secure then "; Secure" else "")

let private issueHeader (deps: Deps) (now: int) (guestId: string) : JS.Promise<string> =
    promise {
        let! token = issue deps.Config now LifetimeSeconds guestId
        return cookieHeader deps.Secure token
    }

/// WRITE path: verify the cookie; never create a guest. A valid signed credential is accepted
/// (renewed if due). An eligible *anonymous* legacy cookie under an active bridge is accepted with a
/// signed replacement (upgrade-in-place). Everything else — expired, invalid, missing, ineligible
/// or linked legacy, or any legacy under hard cutover — is rejected.
let requireGuest (deps: Deps) (cookieValue: string option) : JS.Promise<RequireResult> =
    promise {
        let now = deps.Now()
        let! v = verify deps.Config now cookieValue
        match v with
        | Signed claims ->
            if needsRenewal RenewWithinSeconds now claims then
                let! repl = issueHeader deps now claims.GuestId
                return Accepted { GuestId = claims.GuestId; Replacement = Some repl }
            else
                return Accepted { GuestId = claims.GuestId; Replacement = None }
        | Legacy raw ->
            match deps.Bridge with
            | Bridge endsAt when now < endsAt ->
                let! eligible = deps.LegacyEligible raw
                if not eligible then return Rejected
                else
                    let! linked = deps.LegacyHasLinkedIdentity raw
                    if linked then return Rejected        // linked identities recover via re-login
                    else
                        let! repl = issueHeader deps now raw
                        return Accepted { GuestId = raw; Replacement = Some repl }
            | _ -> return Rejected
        | Expired | Invalid | Missing -> return Rejected
    }

/// BOOTSTRAP path: like requireGuest, but mint a fresh signed guest when nothing is acceptable, so
/// the caller always has a subject (with a replacement cookie to set).
let resolveOrBootstrap (deps: Deps) (cookieValue: string option) : JS.Promise<BootstrapResult> =
    promise {
        let! r = requireGuest deps cookieValue
        match r with
        | Accepted a -> return { GuestId = a.GuestId; IsNew = false; Replacement = a.Replacement }
        | Rejected ->
            let now = deps.Now()
            let gid = deps.NewGuestId()
            let! repl = issueHeader deps now gid
            return { GuestId = gid; IsNew = true; Replacement = Some repl }
    }

/// OAuth adoption: issue a signed credential (Set-Cookie header) for the verified adopted subject.
/// This is the privileged operation that binds a subject returned by the OAuth flow.
let adopt (deps: Deps) (guestId: string) : JS.Promise<string> =
    issueHeader deps (deps.Now()) guestId

/// The minimal, module-facing capability: resolve a write's guest straight from the request. A
/// content module holds this in its Services and calls it with the request; it never reads the
/// cookie, a secret, or the signing key, and there is no arbitrary Sign(guestId) here.
type Service = { Require: WorkerRequest -> JS.Promise<RequireResult> }

/// Build the module-facing service from a DEFERRED deps builder. The thunk runs (and its
/// fail-closed `configFor` validation fires) only when `Require` is actually called on a write —
/// never at service construction — so a feed read that builds Services but never comments does not
/// trip on a missing secret. Bind audience/DB adapters per request inside the thunk.
let service (getDeps: unit -> Deps) : Service =
    { Require = fun request -> requireGuest (getDeps ()) (readCookie request) }
