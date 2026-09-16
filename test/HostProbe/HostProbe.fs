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

/// Blog's handler record, bound over a Services built from the probe env (no Server.Env).
let blogHandlers (env: ProbeEnv) : Blog.RouteContract.Handlers =
    Blog.Composition.bind
        ({ DB = env.Db; Blobs = env.Blobs; Events = env.Events; AdminKey = env.Key
           Author = probeAuthor; NewId = newId; Now = epochNow; CaptureEnabled = true }
         : Blog.Services.Services)

/// Articles' handler record, likewise.
let articlesHandlers (env: ProbeEnv) : Articles.RouteContract.Handlers =
    Articles.Composition.bind
        ({ DB = env.Db; Events = env.Events; Author = probeAuthor; NewId = newId; Now = epochNow }
         : Articles.Services.Services)

/// A whole site dispatch composed from the probe-built module records — the shape an app's
/// generated Server.Routes takes, but assembled here with no app environment in sight.
let dispatch (env: ProbeEnv) (request: WorkerRequest) (ctx: ExecutionContext) : JS.Promise<WorkerResponse> option =
    Articles.RouteContract.dispatch (articlesHandlers env) request ctx
    |> Option.orElseWith (fun () -> Blog.RouteContract.dispatch (blogHandlers env) request ctx)
