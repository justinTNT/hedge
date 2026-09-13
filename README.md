# Hedge: Hamlet on Edge

F# full-stack on Cloudflare Workers. Shared types, shared codecs, one language
edge to edge — and a code generator that turns your domain model into the SQL
schema, JSON codecs, typed client API, router bindings, and a generic admin.

You author the model (`Domain.fs`, `Api.fs`) and the handlers; Gen writes the
plumbing. The same F# compiles to JS on both ends via Fable, so the wire contract
is a type, not a convention.

## Repository layout

This is a monorepo: one framework, many apps.

```
packages/
├── hedge/            # THE FRAMEWORK
│   ├── src/Hedge/    # Interface (the [<Table>] type system + wrapper types),
│   │                 #   Schema, Validate, Codec, Router, Workers, OAuth,
│   │                 #   Admin (schema-driven CRUD), EventHub/Events (WS)
│   ├── src/Admin/    # Generic admin client + admin.css baseline
│   ├── src/Gen/      # The code generator (Program.fs) + Scaffold.fs
│   ├── src/Client/   # Shared client helpers: Api.fs (HTTP), GuestSession.fs
│   └── tools/        # check_sql.py (SQL-vs-schema drift checker)
├── modules/          # Composable content modules (shared across apps)
│   ├── blog/         #   comment-threaded items (darwin.news, justat /blog)
│   └── articles/     #   long-form posts (justat, ndct)
├── rich-text/        # Shared TipTap/ProseMirror rich-text editor + viewer
└── hedge-extension/  # Browser-extension publishing tooling

apps/                 # One dir per app; each composes the framework (+ modules)
├── microblog/        #   the GOLDEN MODEL — reference impl + test fixture
├── articles/         #   justat.at + ndct (articles, justat also mounts blog)
├── music/  basewatch/  archive/  pathname/
```

Each app is authored + generated + wiring:

```
apps/<app>/
├── src/Models/       # Domain.fs, Api.fs, Ws.fs          ← you author
├── src/Server/       # Handlers.fs (authored) + generated/ (Db, AdminGen, Routes)
├── src/Codecs/       # generated/Codecs.fs
├── src/Client/       # App.fs (authored) + generated/ClientGen.fs
├── gen-modules.json  # which modules this app composes
├── schema.sql        # Gen-emitted composed schema
└── wrangler.toml  package.json  vite.config.js  index.html
```

## Start here

**microblog** is the golden model — the reference implementation the framework is
validated against. To understand Hedge, read `apps/microblog/src/Models/Domain.fs`
(the model), then its `Server/Handlers.fs`, then run `npm run gen` and look at what
appears under `generated/`.

## Quick start

There is no root build; work inside an app directory.

```bash
dotnet tool restore          # once, at the repo root
cd apps/microblog
npm install

npm run dev                  # client + server + gen watchers (concurrently)
npm run gen                  # regenerate schema.sql + generated/ from the model
npm run migrate              # diff the model against a live D1 -> a migration
bash check-sql.sh            # verify hand-written SQL against the schema(s)
npm run deploy               # build + wrangler deploy (per-env: deploy:<env>)
```

Remote D1 migrations are never applied automatically — `migrate` writes the
migration for review; you apply it deliberately.

## Modules & composition

A site is composed as `[identity] + one or more content modules`, listed in the
app's `gen-modules.json`. Gen prefixes each module's tables/routes (e.g. `blog_`,
`/api/blog`) so several modules coexist in one app and one D1. justat.at is the
worked example: `articles` at the root + a `blog` mounted at `/blog`, one deploy,
one database. See `notes/MODULES.md`.

## Deeper docs

- `notes/MONOREPO.md` — framework-vs-app boundary, the golden-model discipline
- `notes/MODULES.md` — module composition, the justat merge, mounting styles
- `notes/ROADMAP.md` — where this is going

## Stack

| Layer   | Library                | Purpose                              |
|---------|------------------------|--------------------------------------|
| Model   | Hedge.Interface + Gen  | `[<Table>]` types → schema/codecs/API |
| Shared  | Thoth.Json             | Type-safe JSON codecs (the contract) |
| Client  | Feliz + Elmish         | React with the F# Elm architecture   |
| Server  | Fable → Workers        | Edge functions over D1 / R2 / KV     |
| Build   | Fable + Vite           | F# → JS for both client and server   |

## Why Thoth + Fetch instead of Fable.Remoting?

Fable.Remoting's server needs the .NET CLR — it doesn't compile to JS. Workers
need pure Fable → JS on both ends, so Hedge uses Thoth codecs + fetch wrappers for
the same type-safe serialization without a runtime dependency. The wrapper cost is
small and now shared (`packages/hedge/src/Client/Api.fs`), and Gen writes the typed
API surface (`generated/ClientGen.fs`) on top of it.
