module Server.ModuleServices

// C3 — the single place that adapts this app's environment into each content module's Services,
// and (C6) composes the site dispatch + cron. It is the only server file that knows BOTH the app's
// Env / Server.Identity AND a module's Services type, so the module itself stays ignorant of the
// app. This is the DEFAULT (blog-only) variant — every microblog tenant except idealist;
// ModuleServices.idealist.fs is the blog+alerts superset. Both expose `blog`/`dispatch`/`scheduled`
// so Worker.fs is site-agnostic (selected by HEDGE_SITE in Server.fsproj).

open Fable.Core
open Hedge.Workers
open Content.Server.Author
open Server.Env

/// Build the author resolver from the app's Server.Identity: ensure the guest + anonymous
/// identity exist (one batch), then return the active (claimed) identity or the anon fallback —
/// exactly the flow the module used to run inline against Identity.Server.
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

/// The blog module's Services, adapted from this app's Env + request (the request supplies the
/// guest-session audience; the guest service is deferred so it costs nothing on a read).
let blog (env: Env) (request: WorkerRequest) : Blog.Services.Services =
    { DB = env.DB
      Blobs = env.BLOBS
      Events = env.EVENTS
      AdminKey = env.ADMIN_KEY
      Author = authorResolver env.DB
      Guest = Hedge.GuestSession.service (fun () -> Server.GuestConfig.deps env request)
      NewId = newId
      Now = epochNow
      CaptureEnabled = true }

/// The site dispatch (blog only) — moved here from Worker.fs so the Worker stays site-agnostic.
let dispatch (env: Env) (request: WorkerRequest) (ctx: ExecutionContext) : JS.Promise<WorkerResponse> option =
    Server.Routes.dispatch (Blog.Composition.bind (blog env request)) request ctx

/// No cron on the default microblog tenants (idealist runs the alerts cron — see the .idealist variant).
let scheduled : (ScheduledController -> obj -> ExecutionContext -> JS.Promise<unit>) option = None
