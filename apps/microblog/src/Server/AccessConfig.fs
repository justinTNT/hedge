module Server.AccessConfig

// The one place this app binds its environment + DB adapters into the shared access-control policy
// (Hedge.AccessControl). Reuses the ONE guest policy (Server.GuestConfig) plus the identity resolver
// and grant lookup. Access control is consumed by INJECTION (alerts' curator endpoints map its
// RoleResult onto their own capability), not by the router — so there is no WorkerConfig field; the
// host simply calls `authorize` where a role gate is needed.

open Fable.Core
open Hedge.Workers
open Server.Env

// Access-control (grants) resolution is the shared identity module's OPT-IN sub-surface
// (Identity.Grants, via identity.grants.server.props); this app just binds it to its Env. Both
// resolvers read the shared Identity.Server + the module-owned grant SQL — no grant SQL is copied here.

/// Bind the access-control deps for this request: the shared guest policy + the active-identity
/// subject resolver (honors guests.deleted_at, excludes anonymous) + the enabled-grant lookup.
let deps (env: Env) (request: WorkerRequest) : Hedge.AccessControl.Deps =
    { Guest = Server.GuestConfig.deps env request
      ActiveSubject = fun guestId -> promise {
          let! s = Identity.Grants.activeSubject env.DB guestId
          return s |> Option.map (fun (provider, providerUserId) ->
              ({ Provider = provider; ProviderUserId = providerUserId } : Hedge.AccessControl.Subject)) }
      HasGrant = fun provider providerUserId role -> Identity.Grants.hasGrant env.DB provider providerUserId role }

/// Authorize `role` for this request (accepted guest cookie → active identity → enabled grant).
/// One request in, so audience/cookie are consistent. The guest fail-closed check fires here (only on
/// an actual curator call), never at Services construction — so cron services stay config-free.
let authorize (env: Env) (role: string) (request: WorkerRequest) : JS.Promise<Hedge.AccessControl.RoleResult> =
    Hedge.AccessControl.requireRole (deps env request) role (Hedge.GuestSession.readCookie request)
