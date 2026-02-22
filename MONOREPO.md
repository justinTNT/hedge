# Monorepo Restructure Plan

## Why

Hedge is becoming a reusable framework. The current repo mixes framework code with the microblog app. Separating them makes the boundaries explicit and allows multiple apps to share the same framework. The microblog app becomes the "golden model" — a reference implementation and test fixture.

## Target Structure

```
hedge/
  packages/
    hedge/
      src/Hedge/           Core library: Interface, Schema, Workers, Codec,
                           Router, Validate, Hooks, SchemaCodec
      src/Gen/             Code generator (source-parses Domain.fs,
                           emits Db.fs + AdminGen.fs)
      src/Admin/           Generic admin client (zero app knowledge)

  apps/
    microblog/             Golden model — current app
      src/Models/          Domain.fs, Api.fs, Config.fs, Ws.fs
      src/Codecs/          App-specific codec wiring (Codecs.fs)
      src/Server/          Env, Handlers, AdminConfig, Admin, EventHub, Worker
                           + generated: Db.fs, AdminGen.fs
      src/Client/          Elmish app
      migrations/
      extension/
      wrangler.toml
      package.json
      index.html
      admin.html
      vite.config.js

    another-app/           Future apps follow the same shape
      src/Models/
      src/Server/
      src/Client/
      ...
```

## What Is Framework vs App

### Framework (`packages/hedge/`)

- `Hedge/` — type system (Interface.fs wrapper types), schema reflection, D1 bindings, codec engine, router, validation engine
- `Gen/` — reads Domain.fs as text, auto-discovers all `type X = {` blocks, generates typed D1 interface (Db.fs) and admin schema (AdminGen.fs)
- `Admin/` — generic admin client. Renders CRUD UI from TypeSchema served by the server. Has zero knowledge of any app's domain types.

### App (`apps/microblog/`)

- `Models/Domain.fs` — domain types. This is the single source of truth. Gen reads it, Db.fs and AdminGen.fs are derived from it.
- `Models/Api.fs` — API request/response types per endpoint
- `Codecs/` — wires the generic codec engine to the app's specific types
- `Server/Handlers.fs` — app logic. Calls generated Db functions.
- `Server/AdminConfig.fs` — registers domain types with the generic admin dispatcher
- `Server/Admin.fs` — generic admin HTTP dispatcher (see gray areas)
- `Server/Worker.fs` — Cloudflare Worker entry point, routes to handlers
- `Client/` — Feliz/Elmish UI, fully app-specific
- `migrations/` — D1 SQL migrations
- `wrangler.toml` — Cloudflare config (bindings, env vars)

## Gray Areas to Resolve

### Admin.fs (server-side dispatcher)

Currently lives in `src/Server/Admin.fs`. It's generic framework logic (dispatches CRUD by type name) but directly imports `AdminConfig.fs` (app-specific entity registry). Two options:

1. **Move to Hedge/**, make it take the config as a parameter. App wires it in Worker.fs.
2. **Leave in app**, accept that each app copies the ~50-line dispatcher.

Option 1 is cleaner. The dispatcher would become a function like `handleAdmin (config: AdminConfig) (request: WorkerRequest) (env: Env)` in the framework.

### Gen invocation path

Gen hardcodes `src/Models/Domain.fs` relative to CWD. When running from an app directory, this works by convention. If we need flexibility, add a CLI argument: `node dist/gen/Program.js --domain src/Models/Domain.fs`. For now, convention is fine — all apps follow the same directory layout.

### fsproj references

Apps reference the framework via relative paths:
```xml
<ProjectReference Include="../../../packages/hedge/src/Hedge/Hedge.fsproj" />
```
Ugly but functional. Alternatives: NuGet local package (complex), git submodule (extra tooling), or a symlink like `apps/microblog/hedge -> ../../packages/hedge` (fragile). Relative paths are the simplest thing that works.

### App selection for build/deploy

Options:
- Root `package.json` with `--app=microblog` flag
- Symlink `active -> apps/microblog`
- Just `cd apps/microblog && npm run deploy`

The last option (just cd) is simplest and most explicit.

## Golden Model Test

The microblog app is both a working app and the test suite for the framework. Tests run from `apps/microblog/`:

1. **Gen snapshot test**: run `npm run gen`, diff Db.fs and AdminGen.fs against checked-in expected output. Catches regressions in the generator.
2. **Build test**: `dotnet build` all projects (Server, Client, Admin). Catches type errors in generated code.
3. **Integration test** (future): spin up miniflare, hit endpoints, verify responses. Catches runtime regressions.

Adding a second app to `apps/` gives us a cross-app compatibility test — ensuring the framework isn't accidentally coupled to microblog-specific assumptions.

## Migration Steps

Rough order when we're ready:

1. Create `packages/hedge/` directory, move `src/Hedge/`, `src/Gen/`, `src/Admin/` into it
2. Create `apps/microblog/`, move remaining app code into it
3. Update all fsproj `<ProjectReference>` paths
4. Update `package.json` scripts (gen, fable, vite, wrangler)
5. Update `vite.config.js` and `wrangler.toml` paths
6. Update Gen to resolve Domain.fs relative to CWD
7. Verify: `npm run gen && dotnet build` all projects from `apps/microblog/`
8. Add golden model snapshot test
9. Extract Admin.fs dispatcher into framework (optional, can defer)

## Prerequisite

Handler codegen should be done first. That completes the generation story (Domain.fs -> Db.fs + AdminGen.fs + Handlers scaffold) before we restructure the repo around it.
