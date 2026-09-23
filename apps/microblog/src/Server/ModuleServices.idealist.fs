module Server.ModuleServices

// C6 — the idealist variant: blog + alerts. Same module name and surface (`blog`, `dispatch`,
// `scheduled`) as the default ModuleServices.fs, selected by HEDGE_SITE in Server.fsproj, so
// Worker.fs stays site-agnostic. This is the one place that knows BOTH the alerts module and blog's
// create surface: it implements alerts' PromoteToFeed against Blog.Db.insertItem + Blog.Sql tag
// statements, so alerts itself keeps no compile-time dependency on blog.

open Fable.Core
open Hedge.Workers
open Content.Server.Author
open Server.Env

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

/// alerts → blog bridge. Build blog's item + topic-tag inserts (UNEXECUTED) and return them with the
/// new item id, so the alerts module batches them together with its own alerts_promotions row (one
/// atomic COMMIT). The tag is upserted (INSERT OR IGNORE) then linked by name. Extract/OwnerComment
/// arrive as ready rich-text JSON from the alerts module.
let private promoteToFeed (db: D1Database) (input: Alerts.Services.PromotionInput) : Alerts.Services.FeedInsertion =
    let itemId = newId ()
    let now = epochNow ()
    let create : Blog.Db.ItemCreate =
        { Title = input.Title
          Link = Some input.Link
          Image = input.Image
          Extract = Some input.Extract
          OwnerComment = input.OwnerComment
          ArticleDate = input.ArticleDate
          Slug = None
          ViewCount = 0 }
    let itemStmt = (Blog.Db.insertItem db itemId now create).Stmt
    let tagStmt = bind (db.prepare Blog.Sql.insertTag) [| box (newId ()); box input.Topic; box now |]
    let linkStmt = bind (db.prepare Blog.Sql.linkItemTag) [| box (newId ()); box itemId; box input.Topic |]
    {| Stmts = [| itemStmt; tagStmt; linkStmt |]; ItemId = itemId |}

let private alertsServices (env: Env) : Alerts.Services.Services =
    { DB = env.DB
      NewId = newId
      Now = epochNow
      PromoteToFeed = promoteToFeed env.DB
      // Curator authorization, injected. Takes the request at CALL time, so this constructor stays
      // request-free — the cron builds alertsServices too and must not need a request. Maps the
      // framework's RoleResult onto alerts' own CuratorAuth (alerts names no access-control type).
      AuthorizeCurator = fun request ->
          promise {
              let! r = Server.AccessConfig.authorize env "curator" request
              return
                  match r with
                  | Hedge.AccessControl.Authorized (_, repl) -> Alerts.Services.Allowed repl
                  | Hedge.AccessControl.AuthRequired repl -> Alerts.Services.AuthRequired repl
                  | Hedge.AccessControl.Forbidden (_, repl) -> Alerts.Services.Forbidden repl
          } }

/// Site dispatch = blog + alerts (alerts contributes no routes, so this just falls through to blog).
let dispatch (env: Env) (request: WorkerRequest) (ctx: ExecutionContext) : JS.Promise<WorkerResponse> option =
    Server.Routes.dispatch
        (Blog.Composition.bind (blog env request))
        (Alerts.Composition.bind (alertsServices env))
        request ctx

/// The alerts cron — poll enabled feeds, promote approved drafts. Fires on the [env.idealist]
/// [triggers] crons schedule; inert without it.
let scheduled : (ScheduledController -> obj -> ExecutionContext -> JS.Promise<unit>) option =
    Some (fun _controller env ctx -> Alerts.Cron.run (alertsServices (env :?> Env)) ctx)
