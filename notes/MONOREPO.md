# Monorepo Restructure Plan

## Why

Hedge is becoming a reusable framework. The current repo mixes framework code with the microblog app. Separating them makes the boundaries explicit and allows multiple apps to share the same framework. The microblog app becomes the "golden model" — a reference implementation and test fixture.

## Target Structure

```
hedge/
  packages/
    hedge/
      src/Hedge/           Core library: Interface, Schema, Workers, Codec,
                           Router, Validate, SchemaCodec
      src/Admin/           Generic admin client (zero app knowledge)

  apps/
    microblog/             Golden model — current app
      src/Gen/             Code generator (.NET console app, reflects on Models)
      src/Models/          Domain.fs, Api.fs, Config.fs, Ws.fs
      src/Codecs/
        generated/         Codecs.fs (generated — encode/decode/validate)
      src/Server/
        generated/         Db.fs, AdminGen.fs, Routes.fs (generated)
        Env.fs, Handlers.fs, AdminConfig.fs, Admin.fs, EventHub.fs, Worker.fs
      src/Client/
        generated/         ClientGen.fs (generated — typed API + WsEvent DU)
        Api.fs, GuestSession.fs, RichText.fs, App.fs
      migrations/
      extension/
      wrangler.toml
      package.json
      index.html
      admin.html
      vite.config.js

    another-app/           Future apps follow the same shape
      src/Gen/             Own Gen.fsproj (template) referencing own Models
      src/Models/
      src/Server/
      src/Client/
      ...
```

## What Is Framework vs App

### Framework (`packages/hedge/`)

- `Hedge/` — type system (Interface.fs wrapper types, TableAttribute), schema reflection, D1/R2/KV bindings, codec engine, router, validation engine, `createWorker` entry point, blob handlers
- `Admin/` — generic admin client. Renders CRUD UI from TypeSchema served by the server. Has zero knowledge of any app's domain types.

### App (`apps/microblog/`)

- `Models/Domain.fs` — domain types with wrapper types (PrimaryKey, ForeignKey, etc.) and `[<Table>]` overrides. Single source of truth.
- `Models/Api.fs` — API endpoint modules. Each module has `endpoint : Get<R> | GetOne<R> | Post<Req, R>` plus Request/Response/view types.
- `Models/Ws.fs` — WebSocket event record types.
- `Gen/` — .NET console app that reflects on the compiled Models assembly and generates 6 files. Gen.fsproj is a per-app template — it references the app's Models.fsproj and the framework's Hedge.fsproj.
- `Codecs/generated/Codecs.fs` — **generated**: Encode/Decode modules for all domain, API, and WS types; Validate module with schemas for request types.
- `Server/generated/Db.fs` — **generated**: typed D1 row types, parsers, SQL builders per domain type.
- `Server/generated/AdminGen.fs` — **generated**: AdminTable records with schemas and SQL for the admin CRUD dispatcher.
- `Server/generated/Routes.fs` — **generated**: route dispatch matching endpoints to handler functions.
- `Server/Handlers.fs` — app logic. Stubs generated once (if file doesn't exist), developer fills in. Compiler enforces the contract — new endpoints in Api.fs cause build failures until the handler is added.
- `Server/AdminConfig.fs` — registers domain types with the generic admin dispatcher using generated AdminGen tables.
- `Server/Admin.fs` — admin HTTP dispatcher (see gray areas).
- `Server/Worker.fs` — ~10 lines wiring `createWorker` with app routes and admin config.
- `Client/Api.fs` — framework HTTP helpers only (fetchJson, postJson, openWebSocket).
- `Client/generated/ClientGen.fs` — **generated**: typed API functions per endpoint + WsEvent DU with decoder.
- `Client/App.fs` — Feliz/Elmish UI, fully app-specific.

### Developer Focus (5 files)

| File | Role |
|---|---|
| `Models/Domain.fs` | Data model (types → DB schema) |
| `Models/Api.fs` | API contracts (endpoints, request/response shapes) |
| `Models/Ws.fs` | WebSocket event types |
| `Server/Handlers.fs` | Business logic (stubs generated once, you fill in) |
| `Client/App.fs` | UI (Elmish) |

Everything else is **framework** (Hedge) or **generated** (`generated/` subdirs).

## Gray Areas

### Admin.fs (server-side dispatcher)

Currently lives in `src/Server/Admin.fs`. It's generic framework logic (dispatches CRUD by type name) but directly imports `AdminConfig.fs` (app-specific entity registry).

`createWorker` already takes admin as an optional callback (`Admin: (WorkerRequest -> obj -> Route -> ...) option`), so the dispatcher function itself could move to Hedge/. AdminConfig.fs stays app-side — it builds the entity registry from generated AdminGen tables.

**Decision**: move the dispatcher into Hedge/ as a function that takes config as parameter. App wires it in Worker.fs. Can defer — works fine as-is.

### Gen as per-app template

Gen is framework code (the generator logic is reusable) but its fsproj must reference the app's Models. Solution: Gen.fsproj is a **per-app template**. Each app gets its own `src/Gen/Gen.fsproj` that references:
- Its own `src/Models/Models.fsproj` (for reflection targets)
- The framework's `packages/hedge/src/Hedge/Hedge.fsproj` (for Interface types, Schema types)

The actual `Program.fs` source lives in the framework and is referenced via a shared file link or copy. Since Program.fs is pure generator logic with no app-specific code, it can be shared across apps.

Options for sharing Program.fs:
- **Shared file link**: `<Compile Include="../../../packages/hedge/src/Gen/Program.fs" />` — works, slightly fragile
- **Copy on scaffold**: when creating a new app, copy Gen/ from a template. Drift risk if framework updates Gen.
- **NuGet tool**: package Gen as a `dotnet tool`. Each app installs it and runs `dotnet hedge-gen`. Cleanest but most setup.

For now, shared file link is simplest.

### fsproj references

Apps reference the framework via relative paths:
```xml
<ProjectReference Include="../../../packages/hedge/src/Hedge/Hedge.fsproj" />
```
Ugly but functional. Same pattern as today, just longer paths.

### App selection for build/deploy

Just `cd apps/microblog && npm run deploy`. Simple and explicit.

## Golden Model Test

The microblog app is both a working app and the test suite for the framework. Tests run from `apps/microblog/`:

1. **Gen snapshot test**: run `npm run gen`, diff all generated files against checked-in expected output. Catches regressions in the generator.
2. **Build test**: `dotnet build` all projects (Server, Client, Admin). Catches type errors in generated code.
3. **Integration test** (future): spin up miniflare, hit endpoints, verify responses. Catches runtime regressions.

Adding a second app to `apps/` gives us a cross-app compatibility test — ensuring the framework isn't accidentally coupled to microblog-specific assumptions.

## Migration Steps

Steps 1-7 are complete. Two remain and can be tackled together:

1. ~~Create `packages/hedge/` directory, move `src/Hedge/` and `src/Admin/` into it~~
2. ~~Create `apps/microblog/`, move remaining app code into it~~
3. ~~Set up Gen as per-app template: `apps/microblog/src/Gen/Gen.fsproj` referencing own Models + framework Hedge, with shared `Program.fs` link~~
4. ~~Update all fsproj `<ProjectReference>` paths~~
5. ~~Update `package.json` scripts (gen uses `dotnet run`, fable, vite, wrangler)~~
6. ~~Update `vite.config.js` and `wrangler.toml` paths~~
7. ~~Verify: `npm run gen && dotnet build` all projects from `apps/microblog/`~~
8. **Add golden model snapshot test** — `test.sh` currently does build verification only. Add diffing generated files against checked-in expected output to catch generator regressions.
9. **Extract Admin.fs dispatcher into framework** — Admin.fs is 100% generic (dispatches CRUD via entity list from AdminConfig.fs). Move into `packages/hedge/src/Hedge/` as a function parameterized over entities and admin key extraction, eliminating the 130-line copy in every scaffolded app.

## Framework work — trigger: the second app (saymay music player)

Building saymay is the first genuinely *different* hedge app (music player, not a
news/blog tenant of microblog). That's the validation point for extracting shared
concerns — do these three together when scaffolding it:

1. **First-class per-tenant config/identity.** Tenant identity is currently
   scattered and half-built: `SITE_SLUG`/`SITE_LOGO` are injected ad hoc by the app's
   `vite.config.js`, and `Models.Config.GlobalConfig`/`FeatureFlags` is *defined but
   never wired to the client*. Consolidate into ONE framework capability: the
   framework stamps `body.tenant-<slug>` and delivers a typed per-tenant config
   `{ slug; title; logo; features }` to the client via an accessor; apps just declare
   each tenant's values (wrangler `[env.*]` + build env stay app-level — those are the
   tenant *list*, not the mechanism). Consequences: per-tenant CSS keys off the
   framework-owned `body.tenant-*`; **`fitHeadlines` (and any per-tenant behaviour)
   gates on a `features` flag (e.g. `features.bigText`) instead of the hardcoded
   `body.classList.contains('tenant-usbase')` check in `Client/Shared.fs`** — the
   behaviour stays app code, only its activation reads the framework signal.
   (As of 2026-09-08 that hardcoded check has already grown to a two-tenant list —
   `tenant-usbase || tenant-mtmuse` — which is exactly the smell this item removes.)

2. **Multi-param (or query-string) GET endpoints.** `Hedge.Interface` only has
   `Get` (no param) and `GetOne` (one path param), which forced the `tag~cursor`
   encoding hack in `getItemsByTag`/`itemsByTag` (tag + cursor packed into one param,
   split in the handler). Add a two-param / query-string GET type to `Interface.fs`
   and teach Gen to emit its route + client wrapper; then the tag cursor is a clean
   separate param and the encoding hack is removed.

3. **Extract Admin.fs into the framework** (item 9 above) — saymay will want admin
   too, so do the extraction now rather than copy the 130-line dispatcher again.

4. **Image as a generated admin field type** — the framework-shaped slice of image upload:
   add a semantic `Blob`/`Image` type to `Interface.fs` so a model field typed that way is
   *generated* by Gen + the generic Admin into an upload control wired to `/api/blobs`
   (models → web-code — that's what makes this framework, not the fact that many apps want
   uploads). A bespoke upload button in a hand-written client view is **app** code and does
   not belong here. Batch the field-type work with the Admin extraction. See
   `notes/IMAGES.md` (Workstream D); the other image workstreams (ingestImage, backfill,
   auto-mirror-on-submit) are app/ETL work, independent of this batch, and can land earlier.

Deliberately staying app-level (decision 2026-09-08): **cursor pagination** — the
per-list SQL and cursor semantics are app-specific, and the client scroll helpers
(`watchScroll`, `loadMoreIfSentinelVisible`) are thin list-UI, so they live with the
app rather than the framework.

## Reflection & north star (2026-09-09) — after five apps

The framework held up across five apps: microblog (golden), articles (justat/ndct),
music (dont/just.saymay.be), pathname (static comic), archive (ntne.ws, D1 + FTS5).
`packages/hedge` stayed clean (Gen + Hedge runtime + generic Admin); all the
duplication is in the apps. Grounded sweep + the emerging direction:

### North star: apps as mountable modules; a site is a composition

Endgame (user, 2026-09-09): stop treating microblog / pages / articles / music as
monolithic deploys. Each becomes a self-contained **module** — a feature package with
the standard shape (`Domain` + `Api` + `Handlers` + a `Client` component + its admin
entities) exposing a mount point. A **site** = one deploy, one D1, that imports the
modules it wants and mounts them under routes (e.g. an org-site with the microblog
mounted at `/blog`). "Make a new app by merging the microblog with the org-site" is
the motivating case. (basewatch itself is NOT this — it's just pages/menus; this is
the horizon it should be *shaped* for, not built into.)

hedge is already most of the way there: Gen reflects over `Models` and emits the
combined Db/Routes/Admin/Codecs, so pointing it at two modules' domain types already
produces merged plumbing; the Router dispatches; the generic Admin lists whatever
tables exist. What the framework must add to compose cleanly (all in-boundary —
generic runtime + codegen):
- **Route prefixing / mounting** in Router (a module's routes under a mount point).
- **Client-side module registry** so the shell renders the right module's component.
- **Table / type namespacing** so two modules' `comments` tables don't collide.
- Gen composing **multiple modules'** models into one schema/admin/codecs.

Design this against the FIRST real merge (org-site + blog), not speculatively.

### Framework candidates (model→codegen / generic runtime — in boundary)
- **Generic Admin → framework** — item 9 above; still the safe warm-up. NEW
  dimension (rule-of-three confirmed, basewatch 2026-09-09): the admin's **baseline
  stylesheet** belongs here too, not in each app. "App provides the generic admin's
  CSS" is an antipattern across four apps and it *broke* in two — archive and
  basewatch render the admin as raw HTML because those read-only apps carry no
  app stylesheet to piggyback on (microblog/articles only worked by accident, via
  their own `styles.css`). Fix: ship `public/admin.css` from the framework's Admin
  package as a token-driven baseline — a `:root` of `--admin-*` tokens (accent,
  ink, bg, panel, line, danger, radius, maxw) styling every `admin-*` class; apps
  re-skin by overriding the tokens (or loading a sheet after it). The baseline
  already exists (written for basewatch; copied into archive) — the work is to
  *own* it in `packages/hedge/src/Admin` and have scaffold wire the
  `<link rel="stylesheet" href="/admin.css">` + build-copy, so no app ships its own
  copy. Content-heavy admins (org-site, articles — where you *live*) customise more;
  touch-up admins (microblog) take the baseline as-is.
- **Per-tenant config** — "second app" item 1 above (still unbuilt).
- **Multi-param / query GET endpoints** — item 2 above (archive worked around it again for section + search).
- **`features` capability system in Gen** — NEW. Apps/models declare capabilities (comments, guests, tags, search, admin); Gen emits only those. Kills the "scaffold everything, then strip" tax (archive was copy-articles-then-delete-the-comment-stack). This is the framework half of "modules"; the app half is the guest-comments library.
- **`[<Searchable>]` models → FTS** — NEW (archive). Mark a model searchable; Gen emits the FTS5 virtual table + migration + `/api/search` endpoint + codec. Hand-rolled in archive `Server/{Sql,Handlers}.fs`.
- **Image field type** — item 4 above (unbuilt).

### App-library candidates (reused features/presentation — NOT framework)
- **`guest-comments`** — the identity/comment/attribution/OAuth-avatar/live-events stack (`Server/{Identity,Attribution,EventHub}.fs`, comment bits of `Handlers`/`Sql`, `Client/GuestSession.fs`, `lib/guest-session.js`, comment UI). Copied byte-for-byte between microblog + articles; archive dropped it; music copied-but-unused. Extract as a **mountable module** — design constraint from the north star: a pages app must be able to add it as a sub-section. This IS what the microblog-as-a-module becomes.
- **`rich-text`** — `Client/RichText.fs` + `lib/rich-text/` (TipTap).
- **presentation helpers** — day-grouping, date formatting, teaser/HTML-entity extraction (archive `deriveTeaser`), feed/detail Elmish patterns.
- **`etl` tools** — mongo→D1 pipeline (bson parse, de-mojibake, chunked seeding, cover/URL derivation), currently ad-hoc in scratch. NEW required step
  (rule-of-three confirmed: justat, basewatch, ndct): an **asset-mirror pass** —
  scan migrated content bodies for external asset refs (dead/moved hosts:
  `larak.in`, `saymay.be`, Ghost `/content/images/…`, Netlify), download each,
  upload to the app's R2 bucket, and rewrite the body paths to `/blobs/<key>`
  (served by the framework's existing `/blobs/` route). Pair it with a **verify
  step** (see operational learnings) — after mirroring, fetch every referenced
  asset and assert its `content-type`, because the SPA fallback hides 404s.

### Sequencing (discipline)
- **Now:** build **basewatch** as the first deliberately *module-shaped* app — pages/menus only, minimal (no comments), clean seams. Not to compose it today, but so it's the first clean module and a third concrete data point. (2026-09-09: **done**, and its client is now **Feliz/Elmish** — the first *pages* Client component and the first Feliz client for a *non-feed* shape (hierarchical nav tree + page-by-name), replacing the throwaway vanilla client. The framework had already generated the whole server/codec/typed-client path; only the Client project was missing. So basewatch is now a complete module-shaped data point — Domain + Api + Handlers + Feliz Client + admin — which is what the future "pages" module extracts from. The nav-tree builder (flat menu → tree, in `App.fs navTree`) is the first F# version of the reusable nav presentation.)
- **After basewatch, one deliberate consolidation pass** (never rewire the *live* microblog/articles under build pressure): (1) Admin → framework; (2) extract `guest-comments` as a mountable module, migrate the live apps onto it, prune dead EventHub + music vestigial files; (3) the codegen work — `features` + `[<Searchable>]` — now informed by three apps.
- **Design the mount/compose runtime against the first real merge**, not in the abstract.
- **Rule of three** throughout: don't promote an abstraction until a third app has voted. basewatch is that vote for the pages shape.

### Operational learnings (basewatch + ndct, 2026-09-09)
- **The SPA fallback silently masks missing assets.** With
  `not_found_handling = "single-page-application"`, a request for a missing
  `/content/images/x.webp` returns `index.html` — `200 text/html`, not `404`. So a
  broken image *looks present* (200) and only fails when the browser tries to decode
  HTML as an image. This hid the ndct missing-images bug until after cutover.
  **Status codes lie under an SPA fallback; verify `content-type`.** → migration
  verification (the ETL verify step above) must fetch each referenced asset and
  assert its content-type, never trust the status code. Also a reason to prefer
  narrow asset routes over a blanket SPA fallback where feasible.
- **R2 + `/blobs/<key>` is the settled content-asset home** (justat, ndct). Content
  images migrated off dead hosts land in the app's R2 bucket and are served by the
  framework `/blobs/` route (content-type derived from the key extension when R2
  httpMetadata is absent). Removing these external refs also cuts a Netlify
  dependency each time — worth doing before retiring the old Netlify sites.

### Prune (dead code, batch into the consolidation)
- `EventHub.fs` (live WS comments) — copied into microblog/articles/music, dead everywhere (broadcast dropped).
- music's vestigial `GuestSession.fs`/`RichText.fs`/`Ws.fs`/`EventHub.fs` (scaffold copy, unused).
