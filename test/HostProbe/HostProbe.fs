module HostProbe

// C3 exit check — a minimal host that imports BOTH content modules with its OWN environment type
// and NO Server.Env / Server.Identity. It builds each module's Services from a caller-supplied
// context, binds the handlers (Composition.bind), and composes the module dispatches. If this
// project compiles, the content modules are decoupled from any app environment — the whole point
// of C3. Compile-only: nothing here runs; the test is `dotnet build` (see test.sh).

open Fable.Core
open Hedge.Workers
open Content.Server.Author

/// A stand-in environment — whatever this probe host happens to have. Deliberately NOT
/// Server.Env: a module must not depend on the app's environment record.
type ProbeEnv =
    { Db: D1Database
      Blobs: R2Bucket
      Events: DurableObjectNamespace
      Key: string }

/// The host supplies its own author resolver; a module never sees how it's built (here a trivial
/// stand-in — the real apps adapt Server.Identity, which the probe deliberately doesn't import).
let private probeAuthor : AuthorResolver =
    { ResolveAuthor = fun (req: AuthorRequest) ->
        promise { return { IdentityId = req.FallbackIdentityId; Picture = "" } } }

/// The host binds the shared guest-session policy (a Hedge type) from ITS OWN environment — the
/// module only sees Hedge.GuestSession.Service, never the app's Env or a secret. Deferred, so this
/// stand-in's configFor never runs in this compile-only probe.
let private probeGuest : Hedge.GuestSession.Service =
    Hedge.GuestSession.service (fun () ->
        { Config = Hedge.GuestSession.configFor "k1" "probe-secret-000000000000000000000000000000" "probe" []
          Bridge = Hedge.GuestSession.HardCutover
          Secure = false
          Now = epochNow
          NewGuestId = newId
          LegacyEligible = (fun _ -> promise { return false })
          LegacyHasLinkedIdentity = (fun _ -> promise { return false }) })

/// Blog's handler record, bound over a Services built from the probe env (no Server.Env).
let blogHandlers (env: ProbeEnv) : Blog.RouteContract.Handlers =
    Blog.Composition.bind
        ({ DB = env.Db; Blobs = env.Blobs; Events = env.Events; AdminKey = env.Key
           Author = probeAuthor; Guest = probeGuest; NewId = newId; Now = epochNow; CaptureEnabled = true }
         : Blog.Services.Services)

/// Articles' handler record, likewise.
let articlesHandlers (env: ProbeEnv) : Articles.RouteContract.Handlers =
    Articles.Composition.bind
        ({ DB = env.Db; Events = env.Events; Author = probeAuthor; Guest = probeGuest; NewId = newId; Now = epochNow }
         : Articles.Services.Services)

/// The alerts module (admin+cron only) composed the same way: the host supplies PromoteToFeed — the
/// bridge into its content feed — without alerts naming a content table. Here it's a compile-only
/// stub; the real idealist host implements it against blog's create surface. Proves the alerts
/// server layer builds with no Server.Env dependency.
let private alertsServices (env: ProbeEnv) : Alerts.Services.Services =
    { DB = env.Db
      NewId = newId
      Now = epochNow
      PromoteToFeed = fun (_input: Alerts.Services.PromotionInput) -> {| Stmts = [||]; ItemId = newId () |}
      // Curator authorization is host-injected; a compile-only stub here proves the alerts curator
      // server layer builds with no access-control / Server.Env dependency.
      AuthorizeCurator = fun (_request: WorkerRequest) -> promise { return Alerts.Services.AuthRequired None } }

let alertsHandlers (env: ProbeEnv) : Alerts.RouteContract.Handlers =
    Alerts.Composition.bind (alertsServices env)

/// The cron entry point, referenced so its whole server chain (Sql/Cron/promote) is compiled.
let alertsCron (env: ProbeEnv) (ctx: ExecutionContext) : JS.Promise<unit> =
    Alerts.Cron.run (alertsServices env) ctx

/// A whole site dispatch composed from the probe-built module records — the shape an app's
/// generated Server.Routes takes, but assembled here with no app environment in sight.
let dispatch (env: ProbeEnv) (request: WorkerRequest) (ctx: ExecutionContext) : JS.Promise<WorkerResponse> option =
    Articles.RouteContract.dispatch (articlesHandlers env) request ctx
    |> Option.orElseWith (fun () -> Blog.RouteContract.dispatch (blogHandlers env) request ctx)
