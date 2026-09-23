module Hedge.AccessControl

// Role-based access control — the ONE implementation of "does the person behind this request hold a
// given role". Built ON TOP of the guest-session policy (Hedge.GuestSession): a request is authorized
// for a role when it carries an accepted signed guest cookie whose ACTIVE identity (an OAuth-verified
// (provider, provider_user_id) pair) holds an enabled grant for that role. Framework auth
// infrastructure, sibling of GuestSession — but consumed by app/module handlers via an INJECTED
// capability, NOT by Hedge.Router itself, so (unlike GuestSession) it is deliberately not a
// WorkerConfig field: adding an unused router field would force every createWorker to change for no
// behavior. Apps bind the Deps once (reusing their guest policy + identity + grant lookups) and inject
// the module-facing Service where a role gate is needed. Grants key on the OAuth pair (stable across
// identity merges), never the browser-scoped guestId. A future identity module can move this while
// keeping the contract.

open Fable.Core
open Hedge.Workers
open Hedge.GuestSession

/// The durable subject a grant is keyed on — the active identity's OAuth-verified pair.
type Subject = { Provider: string; ProviderUserId: string }

/// App-bound primitives, injected so the policy stays pure + fixture-testable.
type Deps =
    { /// Reuse the app's ONE guest policy (the WRITE path: verified/bridge, never mints).
      Guest: Hedge.GuestSession.Deps
      /// guestId -> the guest's ACTIVE identity subject, or None when anonymous/none. App wires this
      /// to its identity resolver; it MUST honor guests.deleted_at (a deleted guest resolves to None)
      /// and MUST return None for the shared anonymous identity so it can never hold a role.
      ActiveSubject: string -> JS.Promise<Subject option>
      /// (provider, providerUserId, role) -> is there an ENABLED grant? App-provided DB lookup; an
      /// unknown / absent / disabled triple must return false (never conjure a grant).
      HasGrant: string -> string -> string -> JS.Promise<bool> }

/// Outcome of a role check. The denials are distinct so callers answer 401 vs 403 and render precise
/// UI. The renewal cookie rides on every outcome (a valid session may need renewal even when it lacks
/// the role) — carried separately from the decision. Authorized keeps the Subject for later action
/// attribution without needing an audit subsystem now.
type RoleResult =
    | Authorized of subject: Subject * replacement: string option
    /// No acceptable session, or no active non-anonymous identity — (re-)authenticate. -> 401.
    | AuthRequired of replacement: string option
    /// Identified, but the active identity holds no enabled grant for this role. -> 403.
    | Forbidden of subject: Subject * replacement: string option

/// Resolve whether a request's active identity holds `role`. Chain: accepted guest cookie -> guestId
/// -> active identity subject -> enabled grant. Never mints a guest (wraps requireGuest, the WRITE
/// path). Revocation is honored per-call (fresh HasGrant lookup; no caching).
let requireRole (deps: Deps) (role: string) (cookieValue: string option) : JS.Promise<RoleResult> =
    promise {
        let! required = requireGuest deps.Guest cookieValue
        match required with
        | Rejected -> return AuthRequired None
        | Accepted a ->
            let! subj = deps.ActiveSubject a.GuestId
            match subj with
            | None -> return AuthRequired a.Replacement
            | Some s when s.Provider = "anonymous" -> return AuthRequired a.Replacement  // never role the anon subject
            | Some s ->
                let! ok = deps.HasGrant s.Provider s.ProviderUserId role
                if ok then return Authorized (s, a.Replacement)
                else return Forbidden (s, a.Replacement)
    }

/// The module-facing capability: authorize a role straight from the request. A consumer holds this in
/// its Services and calls it with the request; it never reads the cookie, a secret, or the grant SQL.
type Service = { Authorize: string -> WorkerRequest -> JS.Promise<RoleResult> }

/// Build the service from a DEFERRED deps builder (like GuestSession.service): the thunk runs — and its
/// fail-closed guest configFor validation fires — only when Authorize is actually called, never at
/// construction, so building Services (including request-free cron services) never trips on config.
let service (getDeps: unit -> Deps) : Service =
    { Authorize = fun role request -> requireRole (getDeps ()) role (readCookie request) }
