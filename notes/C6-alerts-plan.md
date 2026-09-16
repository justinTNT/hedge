# C6 — alerts as an idealist-only content module (execution plan)

> Concrete execution plan for C6. The canonical spec is `notes/UNIFIED-SHELL.md §8·C6`; this is the
> file-level "how". Written 2026-09-16 after reading the monolith on the local `alerts` branch
> (`git show alerts:<path>`) and the current consolidated boundaries. Not yet started.

## Context

The unified-shell consolidation (CP-A…CP-E) is complete. C6 ports the **alerts** feature — a
Google-Alerts-RSS → curation-queue → hourly-cron → promote-into-the-blog-feed pipeline — from the
pre-module monolith (`alerts` branch) into a new `packages/modules/alerts` content module, composed
**only** into the `idealist` tenant of the microblog app. It is the first brand-new module on the
consolidated architecture (C3 Services/Composition, C4 per-site enablement) — an end-to-end
validation of C1–C5.

Two things make it non-trivial beyond "mirror the blog module": it re-introduces a **framework cron
capability**, and its promotion must **not touch blog's schema** (the monolith stamped an
`origin_entry_key` column on the items table; the module owns that dedup state in its own table and
inserts into the blog feed through an app-injected capability).

**Out of scope (separate action):** the live `idealist-db` migration (create `alerts_*` tables; no
`origin_entry_key` backfill — that column never ships to a module tenant) and the `--env idealist`
deploy. Code + gate only.

## Three load-bearing design decisions

1. **Framework cron (re-add, not invent).** The `alerts` branch had it; port to the current
   framework (which now also has `Mounts`/`BlobServing`):
   - `Hedge.Workers`: add `type ScheduledController = { scheduledTime: float; cron: string }`
     (`alerts:packages/hedge/src/Hedge/Workers.fs:93-95`) and re-add `isoToEpoch`
     (`alerts:…Workers.fs:136` — `[<Emit>] let isoToEpoch (s:string):int = jsNative`; removed since).
   - `Hedge.Router`: add `Scheduled: (ScheduledController -> obj -> ExecutionContext ->
     JS.Promise<unit>) option` to `WorkerConfig` (Router.fs:220-228); in `createWorker` (Router.fs:230)
     add a **second literal 3-arg lambda** `scheduled = fun c env ctx -> promise { match
     config.Scheduled with Some h -> return! h c env ctx | None -> return () }` to the returned
     `{| fetch |}` record (per `alerts:…Router.fs:328-336`; literal 3-arg lambda so Fable emits an
     uncurried `scheduled(c,e,ctx)`). Inert without a `[triggers]` block.
     `apps/microblog/worker-entry.js` already spreads `{...Worker}`, so the new `scheduled` export
     surfaces automatically — no worker-entry change. Add `Scheduled = None` to existing app
     `Worker.fs` `createWorker` literals so they still compile. Deliberate framework-boundary
     addition (see `unified-shell-state` / hedge-framework-boundary memory): a generic hook, no
     alerts knowledge in the framework.

2. **Promotion state is alerts-owned; blog's schema is never touched.** New `alerts_promotions`
   table replaces `items.origin_entry_key`. `selectPromotable`'s dedup clause changes from `NOT IN
   (SELECT origin_entry_key FROM items…)` to `NOT IN (SELECT entry_key FROM alerts_promotions)`. The
   item insert + the `alerts_promotions` insert go in **one `DB.batch`**, so the unique
   `alerts_promotions.entry_key` index rolls back a concurrent double-promote (same guarantee the old
   unique index gave). Blog's `Item` domain already has no `OriginEntryKey` → no blog change; every
   non-idealist tenant's `blog_items` + schema stay byte-identical.

3. **Alerts stays decoupled from blog; the app wires promotion.** The alerts module declares a
   `Services.PromoteToFeed : PromotionInput -> FeedInsertion` capability (returns *unexecuted*
   statements + the new item id). The **idealist app** (composing both modules) implements it against
   blog's public create surface (`Blog.Db.insertItem` + `Blog.Sql` tag statements; mind
   `ItemCreate.ArticleDate`). Alerts has no compile-time dependency on blog (C3 pattern; a
   cross-module `ForeignKey<Blog.Item>` stays rejected — provenance is a bare `EntryKey`).

## Part 1 — Framework cron capability

`packages/hedge/src/Hedge/Workers.fs` (add `ScheduledController` + `isoToEpoch`);
`packages/hedge/src/Hedge/Router.fs` (`WorkerConfig.Scheduled` + the `scheduled` emit). Port verbatim
from `alerts`, reconciled with the current field set. Add `Scheduled = None` to existing app
`Worker.fs` literals. Only estate-wide framework change; keep minimal. Gate: every app still builds.

## Part 2 — `packages/modules/alerts` module (mirror `packages/modules/blog`, no client)

Alerts is admin+cron only — **no `.client.props`, no `src/Client`**.
- `module.json`: `{ assembly:"AlertsModels", namespace:"Alerts", tablePrefix:"alerts_",
  routePrefix:"alerts", handlerNs:"Alerts.Handlers", namePrefix:"alerts" }`.
- `src/Models/AlertsModels.fsproj` (mirror `BlogModels.fsproj`) + `src/Models/Domain.fs`
  (`module Alerts.Domain`), three records (port `alerts:…/Domain.fs:69-95`, adapted):
  - `AlertSource { Id: PrimaryKey<string>; Topic: string; FeedUrl: Unique<string>; Enabled: bool;
    CreatedAt: CreateTimestamp }`
  - `PendingPost { Id: PrimaryKey<string>; SourceId: ForeignKey<AlertSource>; EntryKey:
    Unique<string>; Title; Link: Link; Snippet; PublishedAt: int; Approved: bool; Rejected: bool;
    OwnerComment: RichContent; CreatedAt: CreateTimestamp }`
  - **NEW** `Promotion { Id: PrimaryKey<string>; EntryKey: Unique<string>; ItemId: string;
    CreatedAt: CreateTimestamp }` → the `alerts_promotions` dedup table.
  - **No `Api.fs`** (no HTTP endpoints). Confirm Gen tolerates an API-less module (RouteContract
    dispatch becomes a no-op `None`); add a minimal empty `Api.fs` if it doesn't.
- `alerts.codecs.props` + `alerts.server.props` (mirror blog's; server compile order: `generated/Db.fs
  → src/Server/Sql.fs → Services.fs → Alerts.fs → generated/RouteContract.fs → Composition.fs →
  generated/AdminGen.fs`).
- `generated/{Db,Codecs,AdminGen,RouteContract}.fs` — via `dotnet run --project src/Gen/Gen.fsproj --
  module ../../packages/modules/alerts` from the microblog app dir, committed. The three `Alerts.Domain`
  records → `Alerts.Db` tables + `Alerts.AdminGen.tables` (generic-admin CRUD) + codecs.

## Part 3 — Alerts server logic (`src/Server/{Services,Sql,Composition,Alerts}.fs`)

- `Services.fs` (`module Alerts.Services`): `type PromotionInput = { Title; Link:string; Extract:string;
  OwnerComment:string; ArticleDate:int; Topic:string }`; `type FeedInsertion = {| Stmts:
  D1PreparedStatement[]; ItemId:string |}`; `type Services = { DB:D1Database; NewId:unit->string;
  Now:unit->int; PromoteToFeed: PromotionInput -> FeedInsertion }`.
- `Sql.fs` (`module Alerts.Sql`, table names via `Alerts.Db.Tables`): `selectEnabledAlertSources`,
  `insertPendingPost` (`INSERT OR IGNORE … ,0,0,'',?`), `selectPromotable` (JOIN; dedup `AND
  p.entry_key NOT IN (SELECT entry_key FROM alerts_promotions) … LIMIT 20`), `insertPromotion`. Port
  `alerts:…/Sql.fs:145-164`, swapping the dedup subquery.
- `Alerts.fs` (open `Hedge.Workers`, `Alerts.Db`, `Alerts.Services`): port `parseFeed` (the one fragile
  regex-Atom seam — verbatim), `ingestEntries`, `pollSource`, `promoteApproved`, `run` from
  `alerts:…/Server/Alerts.fs`. Adapt: take `services: Services` not `env`; `promoteApproved` builds
  `PromotionInput`, calls `services.PromoteToFeed`, appends `insertPromotion [newId; entryKey; ItemId;
  now]` to the returned Stmts, and `DB.batch`es the lot (atomic double-promote guard); row parsers come
  from generated `Alerts.Db`. `run services ctx` = poll enabled → `promoteApproved`.
- `Composition.fs` (`module Alerts.Composition`): `bind` wires the (empty) RouteContract handler
  record if emitted; the cron entry (`Alerts.run`) is wired by the app.

## Part 4 — idealist composition (mirror articles' ndct HEDGE_SITE pattern, inverted → superset)

microblog has no `HEDGE_SITE` conditionals today (it inlines the blog dispatch in Worker.fs). Adopt
articles' dual-file pattern:
- `apps/microblog/gen-modules.idealist.json`: `[{identity:true},{module:"…/blog",primary:true},
  {module:"…/alerts"}]`.
- `apps/microblog/src/Server/Server.fsproj`: `Condition="'$(HEDGE_SITE)' == 'idealist'"` import of
  `alerts.server.props` + `AlertsModels` ProjectReference; pair `ModuleServices.fs`/`.idealist.fs`,
  `generated/AdminGen.fs`/`.idealist.fs`, `generated/Routes.fs`/`.idealist.fs` on `!= idealist` /
  `== idealist` (mirror articles Server.fsproj:24-45).
- `apps/microblog/src/Server/ModuleServices.fs` + `.idealist.fs`: both expose the same surface so
  `Worker.fs` stays site-agnostic — `let dispatch env request ctx` and `let scheduled :
  (ScheduledController -> obj -> ExecutionContext -> JS.Promise<unit>) option`. Default: dispatch =
  blog only (move it here from Worker.fs), `scheduled = None`. idealist: dispatch = blog + alerts
  RouteContract, `scheduled = Some (fun _c env ctx -> Alerts.run (alertsServices (env:?>Env)) ctx)`
  where `alertsServices` builds `Alerts.Services` with `PromoteToFeed` via `Blog.Db.insertItem` +
  `Blog.Sql` tag stmts.
- `apps/microblog/src/Server/Worker.fs` (site-shared): switch to `Routes = Server.ModuleServices
  .dispatch` and add `Scheduled = Server.ModuleServices.scheduled`.
- `apps/microblog/wrangler.toml`: add `[env.idealist.triggers]` `crons = ["0 * * * *"]` (port
  `alerts:…/wrangler.toml:124-126`).
- `apps/microblog/package.json` `deploy:idealist`: add `HEDGE_SITE=idealist` + `npm run gen` before
  build (today it sets neither).

## Part 5 — Tests + verification (gate)

- Commit regenerated artifacts: `HEDGE_SITE=idealist npm run gen` (microblog) → `AdminGen.idealist.fs`,
  `Routes.idealist.fs`, `schema.idealist.sql`; plus `packages/modules/alerts/generated/*`.
- `test.sh` additions (mirror ndct Step 1a/1c): (a) `run_module_surface microblog
  ../../packages/modules/alerts` + diff; (b) idealist per-site glue step (`HEDGE_SITE=idealist gen` →
  diff the `.idealist` glue); (c) assert the DEFAULT microblog `schema.sql` + `blog_items` are
  unchanged (no alerts tables, no `origin_entry_key`).
- Extend `test/HostProbe` to compose the alerts module server layer (stub `PromoteToFeed`).
- Builds: default microblog (unchanged) AND `HEDGE_SITE=idealist` microblog server both compile;
  whole estate green via `./test.sh`.
- Runtime (local, optional): `wrangler dev` + seeded local idealist-db, register an `AlertSource`,
  fire the cron by hand (`curl "http://localhost:8787/__scheduled?cron=0+*+*+*+*"` / `--test-scheduled`),
  confirm a `PendingPost` → approve in the generic admin → re-fire → one `blog_items` row + one
  `alerts_promotions` row, and a second fire promotes nothing (dedup).

## Sequencing / risk

Part 1 (framework cron — small, estate-wide, first + gate) → Part 2/3 (the module in isolation;
`dotnet build` Models + server via HostProbe) → Part 4 (idealist composition) → Part 5 (regen + tests
+ gate) → optional local runtime. Commit in that order. Risks: (a) the `createWorker` record change
(the literal-3-arg-lambda Fable-emit gotcha — copy `alerts` exactly); (b) Gen tolerating an API-less
module (verify early; empty `Api.fs` if needed); (c) adopting the `HEDGE_SITE` dual-file pattern in
microblog without disturbing the 6 existing tenants (they set no `HEDGE_SITE` / have no
`gen-modules.<tenant>.json` → `siteSuffix=""` → the plain blog-only files). The live migration +
`--env idealist` deploy remain a separate, post-merge action.
