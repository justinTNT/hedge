module Server.ModuleServices

// C3 — adapts this app's Env into each composed content module's Services and composes their
// generated dispatches into one site dispatch. This is the DEFAULT (justat: articles + blog);
// ndct (articles-only) uses ModuleServices.ndct.fs. Both expose `dispatch` with the same
// signature, so the shared Worker stays site-agnostic (mirrors AttributionPolicy.fs / .ndct.fs).
// It is the one place that knows both the app's Env / Server.Identity and the modules' Services.

open Fable.Core
open Hedge.Workers
open Content.Server.Author
open Server.Env

/// Build the author resolver from the app's Server.Identity: ensure the guest + anonymous
/// identity exist, then return the active (claimed) identity or the anon fallback.
let private authorResolver (db: D1Database) : AuthorResolver =
    { ResolveAuthor = fun (req: AuthorRequest) ->
        promise {
            let! _ =
                db.batch([|
                    Identity.Server.ensureGuestStmt db req.GuestId req.Now
                    Identity.Server.ensureAnonymousStmt db req.FallbackIdentityId req.GuestId req.AuthorName req.Now
                |])
            let! active = Identity.Server.activeFor db req.GuestId
            return
                { IdentityId = active |> Option.map (fun i -> i.Id) |> Option.defaultValue req.FallbackIdentityId
                  Picture = active |> Option.map (fun i -> i.Picture) |> Option.defaultValue "" }
        } }

// The guest-session service each module gets — the shared signed-cookie policy bound from this
// app's Env + Server.Identity (Server.GuestConfig), deferred so a missing GUEST_SECRET fails only
// a comment write, not a feed read. `request` supplies the audience (host).
let private guestService (env: Env) (request: WorkerRequest) : Hedge.GuestSession.Service =
    Hedge.GuestSession.service (fun () -> Server.GuestConfig.deps env request)

let private articles (env: Env) (request: WorkerRequest) : Articles.Services.Services =
    { DB = env.DB; Events = env.EVENTS; Author = authorResolver env.DB
      Guest = guestService env request; NewId = newId; Now = epochNow }

let private blog (env: Env) (request: WorkerRequest) : Blog.Services.Services =
    // Justat keeps snapshot capture DISABLED (CaptureEnabled = false → POST /api/blog/snapshot
    // returns 404, no write; the /archive route is not mounted below).
    { DB = env.DB; Blobs = env.BLOBS; Events = env.EVENTS; AdminKey = env.ADMIN_KEY
      Author = authorResolver env.DB; Guest = guestService env request
      NewId = newId; Now = epochNow; CaptureEnabled = false }

/// Compose every composed content module's dispatch (articles + blog) over records bound from
/// `env` + `request`, in the order the generated site Routes expects (articles then blog).
let dispatch (env: Env) (request: WorkerRequest) (ctx: ExecutionContext) : JS.Promise<WorkerResponse> option =
    Server.Routes.dispatch (Articles.Composition.bind (articles env request)) (Blog.Composition.bind (blog env request)) request ctx
