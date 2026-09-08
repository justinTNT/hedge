# hedge: app shapes & migration roadmap

Status: **planning** (2026-09-08). Complements `MONOREPO.md` (framework extraction)
and `IMAGES.md` (images→R2). No code implied by this doc.

## Framing (agreed)

The **browser extension** (`packages/hedge-extension`) is what makes microblog a
microblog: items are *captured from web pages while browsing*. Every other site
**authors its content in admin**. So the organising principle is:

> microblog = the extension-fed link-blog shape.
> Everything that isn't extension-fed is a **different style of hedge app** — its own
> `apps/<shape>/`, sharing the framework, deployed per-site via `[env.*]` tenants.

Keep everything in the **one repo** while the framework is still settling: a monorepo
lets the app/framework boundary move in a single commit, which is exactly what "feeling
our way forward" needs. Split an app out only if it ever needs an independent release
cadence — none do yet.

## App shapes (taxonomy)

| Shape | What it is | Ingest | Sites / tenants | Distinct from microblog |
|-------|-----------|--------|-----------------|--------------------------|
| **microblog** *(exists)* | link-blog of web pages | extension | darwin.news, wt.fail, usba.se, mtmuse, idealist | — |
| **articles** *(new)* | admin-authored articles, per-site layout. **"Archive" = the same shape, large + read-only + section nav** | admin / bulk import | justat.at, ndct, **ntne.ws (+ nonukes embedded)** | body *is* the content (no external link); no extension; richer read view |
| **music** *(new)* | amplitude player wrapper + catalogue | admin | dont, just (saymay) | player UI; album/track/mixtape model |
| **pages** *(new)* | hierarchical pages + menus (mini-CMS) | admin | basewatch.org | navigation-first, not a feed at all |
| **reader** *(new, static)* | book-style sequential reader (2 pages/spread) | — (finished work) | pathname | no feed, no admin; bespoke page-turner |
| **static** *(not a hedge app)* | plain static hosting | — | darwin.email, aukus.fyi | no D1 / admin / framework |
| **cards** *(deferred)* | vCard/presence directory | admin | da-rw.in | "another day" |

"Archive" is not its own shape — **ntne.ws is an *articles* app that happens to be large
and read-only.** That reframe is what makes nonukes-inside-ntne.ws natural (below).

Notes:
- **nonukes** currently exists as a *microblog tenant* (stood up for inspection). It moves
  **inside** ntne.ws and that standalone tenant is retired (see "Composing nonukes" below).
- **articles vs microblog**: ~80% overlap (feed list, item detail, tags, comments,
  date-grouping, themes) but the item model differs (authored body, byline/date, no
  subject-link) and it's not extension-fed. Recommendation: **separate app**. Where the
  read-side genuinely overlaps, apps may **share that code directly** (a shared module, or
  just a copy) — *sharing is not a reason to grow the framework*. The framework earns both
  apps their CRUD/API/admin from their models regardless; presentation is app code.
- **pathname** (**reader/book** shape): a finished ~72-page work, path = page number, reads
  as 2-page spreads. Bespoke page-turner, no feed, no admin → **static site** with a custom
  viewer, not a hedge app. (Its own shape, but a static implementation.)

## Composing nonukes into ntne.ws (the embed question)

Because ntne.ws *is* an articles app, folding nonukes in is a composition question, not a
"two apps" one. nonukes items (title / url / teaser / comment / tags) are dead-archive
link-blog entries — no more extension capture, no live commenting — so they don't need
microblog machinery any more, just a home. Two levels of ambition:

- **(1) Flatten — recommended for v1.** ETL the 97 nonukes stories into the articles model
  as a `nonukes` **section** of ntne.ws: `source = url`, `body = teaser + owner_comment`,
  keep `art_date`/tags. One model, one set of views, nonukes becomes browsable at
  `ntne.ws/nonukes`. Ships with near-zero extra machinery; loses only the link-first *look*
  (moot for a dead archive). The standalone nonukes microblog tenant is then retired.
- **(2) Reuse the microblog read-side directly.** Keep nonukes in the microblog item model
  and render its feed/detail *views* as a section inside the ntne.ws app by **sharing those
  view functions between the two apps** (a shared module they both import, or a copy). This
  is the literal "embed the microblog inside the articles" — and note it is **not framework
  work**: the feed list / item card / tag nav are presentation, shared app-to-app, and stay
  out of `packages/hedge`.

Recommendation: **do (1) now** (get nonukes co-located cheaply, while "feeling forward").
Reach for (2) only if we genuinely want the link-blog presentation preserved — and if so,
share the views as plain code, don't fold them into the framework.

## Why the non-microblog apps are cheap: the framework already authors

`packages/hedge` provides, app-type-agnostic: **Gen** (reflects over `Models` + endpoint
decls → emits `Db`/`AdminGen`/`Routes`/`ClientGen`/`Codecs` + SQL/migrations), the
schema/validation/codec runtime those generated files use (`Schema`, `Validate`, `Codec`,
`SchemaCodec`), the request runtime the generated routes plug into (`Router`, `Workers`
incl. blob upload/serve, `OAuth`), and the generic schema-driven **Admin** client. Apps
own their models, hand-written business logic and views, and app-scoped runtime bits like
the `EventHub` durable object and their identity glue. That's why articles/music/pages can
all "rely on admin for new items" — the generic Admin *is* their authoring surface.

So a new admin-authored app ≈ **`Models` + `Client` views + theme + `wrangler [env.*]`**.
The single biggest enabler is therefore **extracting Admin into the framework**
(`MONOREPO.md` item 3/9): it unlocks authoring for *every* non-extension shape at once.
Do it early.

> **Framework boundary (decided 2026-09-08).** The framework is *only* the
> **F# models → autogenerated web-code engine**: `Gen`, the generic schema-driven `Admin`,
> and the runtime the generated code targets (Router, `Interface` endpoint types, D1
> helpers, blob serve, identity). Code does **not** become framework because two apps
> repeat it — shared presentation (feed views, cards, themes, players) is shared as
> ordinary app code (a shared module, or a copy) and stays out of `packages/hedge`. The
> test is "does it *generate* web features from a model?", not "is it reused?". A generic
> admin **field type** (e.g. an image-typed field autogenerating an upload control) is
> framework; a hand-written widget is not.

## Proposed repo shape

```
packages/
  hedge/            # framework — the F# models → autogen web-code engine (Gen + generic Admin + runtime)
  hedge-extension/  # the browser extension (microblog ingest)
apps/
  microblog/        # extension link-blog        (darwin.news, wt.fail, usba.se, mtmuse, idealist)
  music/            # saymay                      (dont, just)
  articles/         # authored articles          (justat, ndct, and ntne.ws in archive mode + nonukes)
  pages/            # menu-driven CMS            (basewatch)
sites/              # plain static (no framework) (darwin.email, aukus.fyi, pathname)
```

Per app: own `Client / Codecs / Gen / Models / Server` + a `wrangler.toml` with one
`[env.<tenant>]` per site + per-tenant deploy scripts — exactly today's microblog pattern,
generalised. "App type" = a dir under `apps/`; "tenant" = an `[env.*]` deploy.

## Sequence (recommended)

**Phase 0 — finish what's started**
- mtmu.se cutover (in flight); top up **idealist** content (only ~2 items loaded); decide
  **nonukes** fate (fold into archive vs keep as microblog tenant).

**Phase 1 — articles (justat + ndct), proceed-and-retrofit**
Decided 2026-09-08: **build articles now; do NOT front-load the framework batch.** The one
heavy-use worry — authoring in admin with images — is already solved: the generic Admin
(`Admin/App.fs`) mounts the TipTap rich-text editor for every `RichContent` field, and that
editor (`lib/rich-text/tiptap-editor.js`) uploads drag/drop/paste images to `/api/blobs`.
So article *body* images upload today with zero framework work.

The three engine items are **additive, localized retrofits**, not blockers:
- **Image/Blob field type** — only gap is a *dedicated hero field* with upload (vs paste-URL);
  v1 can skip a separate hero (first body image, or optional URL). Retrofit = change one
  field's type + regen.
- **Multi-param GET** — the `tag~cursor` hack works; articles reuse it, swap later.
- **Admin extraction** — articles scaffolds its own dispatcher copy (works); articles being
  the *second* consumer is what *triggers* the extraction, as a retrofit not a gate.

Do first: build `apps/articles` (Article model + views, reusing microblog read-side where it
fits; generic Admin for authoring). **One small fix opportunistically:** `handleBlobUpload`
stores blobs with no content-type, so `handleBlobServe` serves them as
`application/octet-stream` — store `httpMetadata.contentType` (~1 line; helps existing
tenants too). Then retrofit the engine items as articles reveals the need.

**Phase 2 — music (saymay)**
The genuinely different app: add the amplitude wrapper (fix the known nav race during the
rebuild) + album/mixtape model; **delete the Lightsail Hasura**. Two tenants
(albums / mixtapes). By now the engine retrofits from Phase 1 (esp. Admin extraction) are
in hand, so saymay builds on a proven authoring base.

**Phase 3 — ntne.ws (articles app, archive mode) + nonukes**
The **same articles app** from Phase 1, scaled up: 3,514 articles + section routes
(`/waste /uranium /ranger /rumjungle /intervention`), read-only. Confirmed a **hedge app**
(D1 makes section/tag queries trivial vs a 3,514-page static build). **nonukes flattened in
as a `/nonukes` section** (see "Composing nonukes"); the standalone nonukes tenant is
retired. Resolves nonukes.

**Phase 4 — pages (basewatch)**
Most distinct shape (hierarchical pages + menus); shares least; do last of the content
sites so the framework is mature first.

**Independent / any time**
- **Static redeploys**: darwin.email (straight redeploy — good early warm-up to shrink the
  iojs surface), aukus.fyi. *Decide target: Cloudflare (consolidate) vs leave on Netlify.*
- **pathname** → static book-reader (2-up spread), whenever.
- **images→R2** (`IMAGES.md`) — independent; **gates AWS/isnt.so cleanup**.
- **da-rw.in** (cards) — deferred.

**Then — AWS teardown** once every iojs site is migrated and images are backfilled: retire
the EC2, release the EIP, rotate the exposed keys, revisit isnt.so/Lightsail. (S3 door
stays open per decision — mp3s/experiments may remain.)

## Open decisions

1. ~~Phase 1 driver~~ **Resolved**: articles-first, **proceed-and-retrofit** — build
   articles now; engine items (Image field type, multi-param GET, Admin extraction) are
   additive retrofits, not front-loaded. Body-image upload already works via the generic
   Admin's rich-text editor.
2. ~~archive shape~~ **Resolved**: ntne.ws is a hedge **articles** app (archive mode), not a
   separate shape and not static.
3. ~~articles as its own app~~ **Resolved**: separate `apps/articles` (not a microblog mode);
   shared read-side is app-level reuse, not framework.
4. **nonukes composition**: flatten into ntne.ws as a section *(recommended v1)* vs reuse
   the microblog read-side views as shared app code *(not framework)*.
5. **aukus.fyi / darwin.email redeploy target**: Cloudflare vs stay on current host.
