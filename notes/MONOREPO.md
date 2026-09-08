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

2. **Multi-param (or query-string) GET endpoints.** `Hedge.Interface` only has
   `Get` (no param) and `GetOne` (one path param), which forced the `tag~cursor`
   encoding hack in `getItemsByTag`/`itemsByTag` (tag + cursor packed into one param,
   split in the handler). Add a two-param / query-string GET type to `Interface.fs`
   and teach Gen to emit its route + client wrapper; then the tag cursor is a clean
   separate param and the encoding hack is removed.

3. **Extract Admin.fs into the framework** (item 9 above) — saymay will want admin
   too, so do the extraction now rather than copy the 130-line dispatcher again.

Deliberately staying app-level (decision 2026-09-08): **cursor pagination** — the
per-list SQL and cursor semantics are app-specific, and the client scroll helpers
(`watchScroll`, `loadMoreIfSentinelVisible`) are thin list-UI, so they live with the
app rather than the framework.
