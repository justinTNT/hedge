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
- **D2 — One D1 (justat-db).** Shared identity + every module's content and comments
  in one database (see D3 — shared identity needs joinable comments; two DBs would
  force cross-DB joins).
- **D3 — Guests AND identities are app-level (shared); comments are per-module.**
  - Shared, app-level (one instance = justat's *existing* `guests`/`identities`,
    OAuth, guest-session client, attribution/avatars). A visitor is one identity
    everywhere on the site.
  - Per content module: only its `*_comments` table + SubmitComment handler +
    comment-thread UI, FK-ing the shared `identities`.
  - So the **blog module is lean**: `blog_items`/`blog_tags`/`blog_item_tags` +
    `blog_comments` + its client. It sheds its own guest/identity stack and reuses
    the shared one.
- **D4 — Extract the blog module for justat first; leave the live microblog on its
  own copy.** justat's blog is brand-new (no data to break) = the safe first
  consumer. Repoint the 6-tenant microblog onto the shared blog module later,
  deliberately (rule-of-three vote #2). Temporary duplication, retired in follow-up.

## Namespacing (one D1)

- **Shared, unprefixed:** `guests`, `identities` (justat's existing).
- **Root module = articles, in place, prefix `""`:** `articles`, `comments` — untouched
  (no migration of live data).
- **Blog module, prefix `blog_`:** `blog_items`, `blog_tags`, `blog_item_tags`,
  `blog_comments` (FK shared `identities`).
- **Consequence / main technical cost:** a module's server code must not hardcode
  table names — Gen emits per-module **table-name constants** the module's SQL uses,
  so the same blog module works whether its tables are `items` (standalone microblog)
  or `blog_items` (mounted in justat). This is the resurfaced landmine that D2's
  earlier two-DB idea dodged; it's the crux of the Phase-1 spike.

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

## Risks

- **Multi-module Gen** (reflecting over N assemblies, route + table prefixing,
  table-name constants) is the real unknown → Phase-1 spike.
- **Module SQL hygiene** — the blog module must reference tables via generated
  constants, never literals; otherwise it breaks when mounted with a prefix.
- **justat is live** — mitigated: purely additive (new tables, new `/blog` route);
  the existing articles site is untouched (root module, unprefixed, no migration).
