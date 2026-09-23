module Server.AccessConfig

// The one place this app binds its environment + DB adapters into the shared access-control policy
// (Hedge.AccessControl). Reuses the ONE guest policy (Server.GuestConfig) plus the identity resolver
// and grant lookup. Access control is consumed by INJECTION (alerts' curator endpoints map its
// RoleResult onto their own capability), not by the router — so there is no WorkerConfig field; the
// host simply calls `authorize` where a role gate is needed.

open Fable.Core
open Hedge.Workers
open Server.Env

// ---- Access-control (grants) resolution: the opt-in sub-surface this app composes over the shared
// identity layer. Kept here (host-side), not in the shared identity module, because grants are
// optional per host. Both read the shared Identity.Server + this app's grant SQL (Server.Sql). ----

/// The guest's ACTIVE identity as its durable OAuth subject (provider, provider_user_id) — the key an
/// access-control grant is checked against. None when the guest is soft-deleted, has no active
/// identity, or its active identity is anonymous (which must never hold a role).
let private activeSubject (db: D1Database) (guestId: string) : JS.Promise<(string * string) option> =
    promise {
        let! live = (bind (db.prepare Sql.guestNotDeleted) [| box guestId |]).first()
        if isNull (box live) then return None
        else
            let! id = Identity.Server.activeFor db guestId
            match id with
            | Some i when i.Provider <> "anonymous" -> return Some (i.Provider, i.ProviderUserId)
            | _ -> return None
    }

/// Is there an enabled grant for this OAuth subject + role? Unknown/absent/disabled → false. Fresh
/// lookup per call, so revocation takes effect at once.
let private hasGrant (db: D1Database) (provider: string) (providerUserId: string) (role: string) : JS.Promise<bool> =
    promise {
        let! row = (bind (db.prepare Sql.grantEnabled) [| box provider; box providerUserId; box role |]).first()
        return not (isNull (box row))
    }

/// Bind the access-control deps for this request: the shared guest policy + the active-identity
/// subject resolver (honors guests.deleted_at, excludes anonymous) + the enabled-grant lookup.
let deps (env: Env) (request: WorkerRequest) : Hedge.AccessControl.Deps =
    { Guest = Server.GuestConfig.deps env request
      ActiveSubject = fun guestId -> promise {
          let! s = activeSubject env.DB guestId
          return s |> Option.map (fun (provider, providerUserId) ->
              ({ Provider = provider; ProviderUserId = providerUserId } : Hedge.AccessControl.Subject)) }
      HasGrant = fun provider providerUserId role -> hasGrant env.DB provider providerUserId role }

/// Authorize `role` for this request (accepted guest cookie → active identity → enabled grant).
/// One request in, so audience/cookie are consistent. The guest fail-closed check fires here (only on
/// an actual curator call), never at Services construction — so cron services stay config-free.
let authorize (env: Env) (role: string) (request: WorkerRequest) : JS.Promise<Hedge.AccessControl.RoleResult> =
    Hedge.AccessControl.requireRole (deps env request) role (Hedge.GuestSession.readCookie request)
