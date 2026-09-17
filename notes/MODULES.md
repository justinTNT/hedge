# Modules & the first merge (justat.at = articles + blog)

The deliberate first merge that drives the modules/compose build. justat.at is an
**env of the `articles` app** (`deploy:justat`, alongside `deploy:ndct`), so this
is: **add a `blog` module to the articles app and mount it for the justat env** —
articles at the root, a blog at `/blog`, one deploy, one D1.

## Decisions (locked)

- **D1 — Two module-integration styles; use path-mount first, keep the door open to
  composition.** There are two legitimate ways a module's client integrates, and the
  framework should support **both**:
  - **Path-mounted bundle** — a self-contained bundle at a path via the framework
    `Mount` (proven by rhyming, and how `/admin` already works): root → articles,
    `/blog` → blog. Coarse-grained, isolated; cross-module nav is a page load.
  - **Integrated component** — an Elmish component composed into a unified shell
    (`Msg`-nesting / `Cmd.map`): seamless SPA nav, chrome + identity rendered/loaded
    once. Fine-grained, woven.
  - **Non-throwaway sequencing:** shape every module's client as a **component**
    (`init`/`update`/`view`), NOT a self-running `Program.run`. Path-mount wraps it in
    a thin standalone entry that runs it; the shell (later) hosts the same component.
    One module shape, two mounting adapters — so path-mount work carries into
    composition unchanged.
  - **First merge uses path-mount:** ship `/blog` as a bundle beside the *untouched*
    live articles client (D4-consistent). The unified shell + articles-as-component is
    the paired follow-up (see below), when we're not under first-merge pressure.
  - **Update (16 Sep 2026 — CP-B convergence, `a21957e`):** the sequencing above is
    complete and has converged further. A module client is now purely a **hosted
    component** — it exposes only the hosted surface (`emptyHosted`/`enterHosted`/
    `updateHosted`/`withSession`/`invalidateContent`/`invalidateInFlight`/
    `disposeHosted`/`contentView`) and owns no router, identity or chrome. There is no
    module-owned standalone `App` any more: the "standalone entry" is an **app-level
    single-module host** (`apps/microblog/src/Client/Blog/{Host,Chrome}.fs`,
    `apps/articles/src/Client/Articles/{Host,Chrome}.fs`) that mirrors the Justat shell
    (one router, one `Content.Identity`/`IdentityView` authority, its own chrome) and
    drives the module through that surface. Identity + tenant chrome are host-owned; the
    module's identity subsystem/UI and `ClaimHandoff` were deleted. See
    [UNIFIED-SHELL.md §0](UNIFIED-SHELL.md).
- **D2 — One D1 (justat-db).** Shared identity + every module's content and comments
  in one database (see D3 — shared identity needs joinable comments; two DBs would
  force cross-DB joins).
- **D3 — Guests AND identities are app-level (shared); comments are per-module.**
  - Shared, app-level (one instance = justat's *existing* `guests`/`identities`,
    OAuth, guest-session client, attribution/avatars). A visitor is one identity
    everywhere on the site.
  - Per content module: only its `*_comments` table + SubmitComment handler +
    comment state (draft/DraftRev/collapse/reply, live-event vs response append),
    FK-ing the shared `identities`. The comment-thread **presentation** is delegated
    to the shared `Content.Comments` renderer (packages/content-client/Comments.fs +
    comments.css): the module maps its rows to presentation records and wires
    callbacks; the module keeps ownership of persistence, requests, and the editor
    lifecycle. So the two modules share one comment UI without depending on each other.
  - So the **blog module is lean**: `blog_items`/`blog_tags`/`blog_item_tags` +
    `blog_comments` + its client. It sheds its own guest/identity stack and reuses
    the shared one.
- **D4 — Extract the blog module for justat first; leave the live microblog on its
  own copy.** justat's blog is brand-new (no data to break) = the safe first
  consumer. Repoint the 6-tenant microblog onto the shared blog module later,
  deliberately (rule-of-three vote #2). Temporary duplication, retired in follow-up.
- **D5 — No privileged "root module"; uniform prefixing + a `primary` mount (ACTIVE —
  see "Convergence plan" below; locked 2026-09-10).** "Root module" isn't a real
  concept — it bundles two unrelated things:
  1. *Unprefixed* paths/tables/generated names (`/api/item`, `items`, `getFeed`).
     This is pure backward-compat scaffolding: it exists only so single-module apps
     regenerate **byte-identically** and we avoid migrating live data. No design
     justification beyond that.
  2. *Answers the naked URL* (`/`). This is the only irreducible part — and it's just
     a routing choice: a `Mount` with `OnPath "/"` (or a `primary`/first-in-order flag
     in `gen-modules.json`) pointing at some module's shell. Independent of prefixing.
  **Target end-state:** every module is uniform — `/api/<m>/*`, `<m>_*` tables, `<m>*`
  generated names — with *no* special root. A `primary` flag (or `OnPath "/"` mount)
  decides who serves the naked URL, fully decoupled from paths/tables/names.
  Consequences:
  - The generator gets **simpler**: the `qualify`/byte-identical branch (emit
    unprefixed for the root, prefixed otherwise) collapses to *always* prefix +
    *always* qualify. The root special-case is complexity that only pays for
    not-migrating-yet.
  - The per-site "where's the content API" wart disappears: clients (e.g. the hedge
    extension) post to `/api/blog/item` **uniformly** on every site — no per-site
    `apiPrefix` needed. (Today the extension compiles the microblog's root-mounted
    `ClientGen` → `/api/item`; against justat's mounted blog at `/api/blog/item` it
    404s — the concrete symptom that surfaced this decision.)
  - Adding/removing a module never shifts another module's paths.
  **Gate:** requires migrating the live data off the unprefixed shape (darwin.news's
  `items`/`comments`/`tags` at `/api/item` → `blog_*` at `/api/blog/*`) — which *is*
  the D4 microblog convergence. So uniform prefixing isn't a retrofit we bolt on; it's
  the shape we adopt *when* we converge, and it makes that step cleaner. No change to
  justat today: its blog is a *secondary* module → prefixed under both models already.

## Namespacing (one D1 — the transitional scheme; superseded by D5 at convergence)

- **Shared, unprefixed:** `guests`, `identities` (justat's existing).
- **Root module = articles, in place, prefix `""`:** `articles`, `comments` — untouched
  (no migration of live data).
- **Blog module, prefix `blog_`:** `blog_items`, `blog_tags`, `blog_item_tags`,
  `blog_comments` (FK shared `identities`).
- **Consequence / main technical cost:** a module's server code must not hardcode
  table names — Gen emits per-module **table-name constants** the module's SQL uses,
  so the same blog module works whether its tables are `items` (standalone microblog)
  or `blog_items` (mounted in justat). Delivered by the `Tables` module in generated
  Db.fs (Phase 1.5, committed).

## Identity reference — wrap, don't unwrap (locked)

The blog `ItemComment` references the shared identity, but must NOT depend on the
concrete `Identity` type (that would couple `BlogModels` → the host's Models).
Solution: a framework wrapper **`IdentityRef`** in `Hedge.Interface` (alongside
`PrimaryKey`/`ForeignKey`/`Unique`). `ItemComment.IdentityId : IdentityRef`.
- Typed (not a naked string), decoupled (only depends on `Hedge`), Gen-aware:
  Gen classifies `IdentityRef` → a `TEXT` column with a FK to the shared
  `identities` table (by convention — identity is a framework-blessed shared
  concept). Later, a natural hook for an admin identity-picker.

## Module wiring — file-links now, split-generation later (locked)

A module's parts relate differently to *generated* code, so wiring is a mix:
- **Models** (Domain/Api/Ws) → a compiled **assembly** (distinct name, e.g.
  `BlogModels`) so Gen can `Assembly.Load` + reflect → **ProjectReference**.
- **Server** (Handlers/Sql) → references the *consumer's* generated `Db`/`Tables`
  + shared `Server.Identity`, so it must co-compile with them → **F# file-links**
  (`<Compile Include="../../packages/modules/blog/Server/Handlers.fs" />`, exactly
  how shared `RichText.fs` is wired). NOT filesystem symlinks (npm-hostile), NOT a
  standalone library (can't see code generated into its consumer).
- **Client** → a hosted **component** (its `.client.props` file-list), composed into a
  host bundle. Post-CP-B there is no module-owned standalone entry; an app supplies the
  single-module host (or the Justat shell) that runs it. See the D1 update above.
- **Later refinement — split generation:** generate prefix-agnostic bits
  (row parsers, codecs, typed API client fns) *per-module* and only prefix-dependent
  bits (`Tables`, `schema`, `Routes`, admin registry) *per-site*; then modules become
  clean ProjectReference libraries. Companion to the articles-extraction/shell work.

## Composed codegen — naming rule (the remaining Gen unknown, now designed)

Composing articles + blog collides on both **generated names** and **type refs**:
both apps have API modules `SubmitComment`/`Events` and a WS `NewCommentEvent`.
So the composed Codecs/Routes/ClientGen/AdminGen/Validate would emit duplicate
`submitCommentReq` etc. and ambiguous `SubmitComment.Request`. The rule:
- **Type references → fully namespace-qualified** by the type's own namespace
  (`Models.Api.SubmitComment` vs `Blog.Api.SubmitComment`; `t.FullName.Replace("+",".")`).
- **Generated identifiers → root module unprefixed, non-root modules prefixed** by a
  per-module discriminator (e.g. `blogSubmitCommentReq`, client `blogGetFeed`). This
  keeps single-module output byte-identical (root only) AND leaves every app's
  hand-written refs (`ClientGen.getArticles`) intact; only the *extracted* blog client
  (which we control) uses the prefixed names.
- Drive the implementation against a **real composed compile** (justat), protecting
  the live articles generated files (scratch/temp gen), so real errors — not guesses —
  shape it. Pervasive but mechanical across the 5 generators; single-module stays
  identical (verified via the gen-stability test).

## The reusable machinery (the "modules build")

- **Multi-module Gen** — Gen reflects over a **list of modules** (articles as the
  root module in-place + blog) and emits combined `Routes`/`Codecs`/`ClientGen`/
  `AdminGen`/`Db`/`schema.sql`: each module's API under a **route prefix**
  (`/api/blog/*`), each module's tables under a **table prefix**, table-name
  constants emitted per module. Uniform N-module handling (root module = prefix `""`).
- **Module shape** — Domain (+ prefix) + Api (+ route/client prefix) + Handlers +
  a Client bundle + admin entities; identity is consumed from the shared layer.
- **Site/app composition** — the app lists the modules it mounts (per env), path-mounts
  each client bundle, and the shared admin spans all modules' tables.

## Phased sequencing

1. **Phase 1 — Multi-module Gen spike (de-risk first).** Prove Gen can reflect over
   two module definitions and emit combined, route-prefixed Routes/Codecs + table
   prefixing + per-module table-name constants. Smallest possible proof; this is the
   real technical unknown.
2. **Phase 2 — Extract the blog module** — microblog's Domain/Api/Handlers/Client →
   `packages/modules/blog/`, made prefix-aware (SQL via generated table-name
   constants), identity delegated to the shared layer, comments kept. The client is
   shaped as a **component** (`init`/`update`/`view`) with a thin standalone entry
   for the path-mounted bundle — so composition can host the same component later.
   microblog untouched (justat is the first consumer).
3. **Phase 3 — Compose in the articles app / justat env** — reference articles(root) +
   blog module; Gen composes; justat-db migration adds `blog_*`; worker path-mounts
   the blog client at `/blog` and its API at `/api/blog`; add a "Blog" menu item.
   (Mount is per-env: justat mounts blog; ndct need not.)
4. **Phase 4 — Seed + deploy + verify** — starter blog content; deploy justat; confirm
   articles (root) + blog (`/blog`) coexist, one shared identity, admin manages both.
5. **Follow-ups (later, no build pressure):**
   - Repoint the live microblog onto the shared blog module (rule-of-three vote #2).
   - Physically extract **articles into `packages/modules/articles`** (moving justat +
     ndct onto it together) for full `packages/modules/*` symmetry — when a genuinely
     separate site wants articles, or we want the symmetric layout.
   - **Unified Elmish shell (integrated-component style)** — paired with the articles
     extraction: refactor the articles client into a component and stand up the shell
     that hosts articles + blog components with seamless SPA nav, shared chrome, and
     identity loaded once. The blog component (from Phase 2) is reused as-is; only the
     mounting changes (standalone entry → hosted in the shell).

## Convergence plan (active — locked 2026-09-10)

Realise D5 across the live estate. **Code the target once; migrate + deploy + test
one site fully before the next.**

**End state.** No root module. Every module is uniform — `/api/<m>/*`, `<m>_*`
tables, `<m>*` generated names, client mounted at a path. A per-site manifest lists
the site's modules and marks the **primary** (its client mounts at `OnPath "/"` —
the naked URL); primary is decoupled from prefixing. `guests`/`identities` stay
**app-level and unprefixed** (shared identity, not a module — D3).

**"No root module" resolved: the shared identity IS the one unprefixed base.** Every
site = `[identity (unprefixed) + content module(s) (prefixed)]`. This is structurally
identical to justat *today* — justat is `[Models(identity+articles, unprefixed), blog]`;
the convergence just splits `Models` into `identity` (Guest/Identity, stays unprefixed)
+ a prefixed `articles` content module. So darwin.news = `[identity, blog]` (blog
primary), ndct = `[identity, articles]`, justat = `[identity, articles, blog]`. The
generator's unprefixed path is **kept** — it's exactly what serves identity — so "no
root module" means no unprefixed *content*, not a generator rip-out. (Identity lives
in each app's own Models for now; deduping into a shared package is a later refinement.)

**Module names/tables (locked).**
- `articles` — base tables renamed for parity + to avoid `articles_articles`:
  `Article` → `posts`, `ArticleComment` → `comments` ⇒ **`articles_posts`**,
  **`articles_comments`**. `ArticleComment.IdentityId` → `IdentityRef` (decouple, like blog).
- `blog` — keep the name (not "weblog"); `blog_items`/`blog_comments`/`blog_tags`/
  `blog_item_tags` (justat already migrated).

**Mechanism.**
- **`HEDGE_SITE`** build flag drives *both* the gen manifest (which modules) *and*
  the compiled file-set.
- **Module-owned `.props`** (not app fsproj reaching into module files): each module
  ships `<m>.server.props` / `<m>.client.props` listing its own `<Compile>` items
  (paths via `$(MSBuildThisFileDirectory)`). The app compares by conditional
  `<Import … Condition="'$(HEDGE_SITE)' == '<site>'">`. Module owns its files; app
  owns the composition. Shared layer (generated `Db`/`Tables`, `Server.Identity`,
  `RichText`) stays app-level, compiled **once**, referenced by all modules — never
  re-linked per module. (Module source *is* recompiled per *site* that mounts it —
  the compile-in-consumer consequence, until split-gen makes modules libraries.)
- **Generator** drops the root special-case: *always* prefix + *always* qualify (the
  `qualify`-on-compose branch collapses — simpler).
- **Client** — generalize the routing/API split done for blog: each mounted module's
  client gets `routingBase` (its mount path; `""` when primary at `/`) + `apiPrefix`
  (its module's `/api/<m>` prefix). So a primary module routes at `/` but still calls
  `/api/<m>/*`.

**Rollout order** (each: migrate + deploy + test fully before the next):
1. **darwin.news `[blog]`** — proves the machinery on the *already-proven* blog module
   + the microblog→module migration (`items`→`blog_items`, paths → `/api/blog`, client
   primary at `/`). `apps/microblog` consumes the shared blog module (`.props` +
   manifest `[blog]`); darwin.news stays hosted there — no app-unification now.
2. **Remaining ~5 microblog tenants `[blog]`** — mechanical repeats of #1.
3. **justat `[articles, blog]`** — introduces the articles module + two-module case;
   migrate `articles`→`articles_posts`, `comments`→`articles_comments`.
4. **ndct `[articles]`** — articles-only; same articles machinery, now proven.

**Coding sequence** (target coded in rollout order): generator no-root/uniform +
blog `.props` + `apps/microblog` composes `[blog]` + client routing/api decoupling →
prove darwin.news → *then* extract `articles` into `packages/modules/articles` for
justat/ndct.

**Guardrail:** no `deploy:ndct` (and no forced redeploy of any running site) until
that site's migration is ready — running deployments stay on current code until their
rollout turn.

## Risks

- **Multi-module Gen** (reflecting over N assemblies, route + table prefixing,
  table-name constants) is the real unknown → Phase-1 spike.
- **Module SQL hygiene** — the blog module must reference tables via generated
  constants, never literals; otherwise it breaks when mounted with a prefix.
- **justat is live** — mitigated: purely additive (new tables, new `/blog` route);
  the existing articles site is untouched (root module, unprefixed, no migration).
