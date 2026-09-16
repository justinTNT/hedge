module Server.ModuleServices

// C3 — the ndct (articles-only) variant of ModuleServices. Same module name + `dispatch`
// signature as ModuleServices.fs (justat: articles + blog), selected by HEDGE_SITE in
// Server.fsproj, so the shared Worker is site-agnostic. Blog is not composed on ndct, so no
// blog Services builder here (Blog.Services isn't compiled).

open Fable.Core
open Hedge.Workers
open Content.Server.Author
open Server.Env

let private authorResolver (db: D1Database) : AuthorResolver =
    { ResolveAuthor = fun (req: AuthorRequest) ->
        promise {
            let! _ =
                db.batch([|
                    Server.Identity.ensureGuestStmt db req.GuestId req.Now
                    Server.Identity.ensureAnonymousStmt db req.FallbackIdentityId req.GuestId req.AuthorName req.Now
                |])
            let! active = Server.Identity.activeFor db req.GuestId
            return
                { IdentityId = active |> Option.map (fun i -> i.Id) |> Option.defaultValue req.FallbackIdentityId
                  Picture = active |> Option.map (fun i -> i.Picture) |> Option.defaultValue "" }
        } }

let private articles (env: Env) : Articles.Services.Services =
    { DB = env.DB; Events = env.EVENTS; Author = authorResolver env.DB; NewId = newId; Now = epochNow }

let dispatch (env: Env) (request: WorkerRequest) (ctx: ExecutionContext) : JS.Promise<WorkerResponse> option =
    Server.Routes.dispatch (Articles.Composition.bind (articles env)) request ctx
