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

/// The minimal, module-facing capability: resolve a write's guest. A content module holds this in
/// its Services and calls it; it exposes no signing key and no arbitrary Sign(guestId).
type Service = { Require: string option -> JS.Promise<RequireResult> }

/// Build the module-facing service from bound deps.
let service (deps: Deps) : Service = { Require = requireGuest deps }
