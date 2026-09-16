# Work order: complete the unified shell and consolidate its boundaries

Issued 15 September 2026. Planning baseline: **5884eaa**.

Implement [UNIFIED-SHELL.md](UNIFIED-SHELL.md) in checkpoint order **C0 → C1 → C2 → C3 → C4 → C5**, then **C6** (alerts as an idealist-only module — added after the consolidation; see §8·C6 in the canonical plan). That document is canonical for architecture, scope and acceptance; this work order is its execution sequence. [UNIFIED-SHELL-design.md](UNIFIED-SHELL-design.md) is background and does not override it.

The integrated Justat shell already exists. Continue from the current source, preserving later fixes if HEAD has advanced. Do not repeat the former articles-only shell stage. Deliver the implementation and review evidence; production deployment and database migration are outside this order.

## Outcome

Content state and operation validity are explicit in the modules. Libraries own shared identity, lifecycle and platform adapters. Hedge generates typed clients and routes against generic contracts. Apps compose those pieces and supply site policy. Each boundary must work through actual consumers, including the browser extension.

The following ownership decisions are settled:

| Responsibility | Owner |
| --- | --- |
| Drafts, queries, request validity, mutation outcomes, content validation | Articles/Blog modules |
| Routing selection, mounts, chrome, session authority, feature enablement | App host |
| Shared identity implementation, editor/resource adapters | Ordinary content-client/rich-text libraries |
| HTTP contracts, generated clients/routes, schema semantics, generic blob policy | Hedge framework |
| Blog publishing UI and sequencing | Microblog extension product |
| Chrome messaging and transport | hedge-extension library |
| Snapshot capture, records and reference serving | Blog module, enabled by app policy |
| Environment binding and cross-module attribution composition | App server |

Use records, unions, functions and Elmish commands. Keep concrete child message types and module-owned .props. Do not introduce a service locator, runtime plugin registry, general effect system or packaging rewrite.

## Delivery sequence

Make small reviewable commits within each checkpoint. Keep consumers buildable during transitions; remove an adapter only after its consumers pass. Include generator changes, generated output, scaffold changes and relevant fixtures in the same checkpoint.

### C0 — Reproducible baseline and complete build gates

1. Record HEAD, worktree changes, SDK selection and commands. Preserve unrelated edits, including any existing migration-file deletion; do not restore or regenerate those files as cleanup.
2. Maintain focused regression fixtures for the recent archive, extension destination, srcset and admin FormSeq fixes. Exercise real compiled code and both valid/obsolete completions.
3. Extend test.sh/scripts/build-all.sh coverage with clean extension Fable compilation, bundling and Chrome/Firefox package verification. Update extension/build.js as needed for isolated outputs and deterministic fixture configuration. Exclude personal sites.json and previously generated JS from build inputs.
4. Record remaining shell/architecture failures against their C1–C5 owner. Distinguish a reproduced failure from an unrun check. Restore/compiler failures must fail the gate even if a tool returns zero.

**Review gate:** a reproducible baseline, verified recent fixes and a documented consumer matrix. Known defects assigned to later checkpoints may remain; do not describe that baseline as full acceptance.

### C1 — Module-owned state and scoped effects

1. Add transient instance/activation/request/operation identities and small load/submission state unions. Carry query/cursor validity with cached feeds and pending reads.
2. Make editor drafts model state, keyed by target/form and revision. Retain write operations independently of view activation. Clear only the submitted draft revision after confirmed success; preserve rejection/uncertain outcomes and newer edits.
3. Add cancellable, idempotent resource scopes to content-client/rich-text. Own sockets, editors, listeners, timers and deferred creation by instance/activation. Preserve pending write completion delivery after view disposal.
4. Move validity checks into child updates and emit explicit host signals for navigation/title effects. Replace articlesStaleDrop/blogStaleDrop atomically with child validation. Keep ordered teardown and the keyed LeaveCompleted barrier.
5. Invalidate caches after writes/identity reattribution; restore valid pagination and feed watchers on re-entry. Exercise the same contract through standalone adapters.

**Review gate:** all C1 checks in the canonical plan, including reverse-order responses, same-module navigation, retained drafts, late write outcomes, cancelled editor creation and independent resource contexts. Complete the original shell browser/routing acceptance before calling stage 2 finished.

### C2 — Transport-neutral generated clients

1. Add Hedge's typed request/response/transport and ApiError contract. Generate each module's Client record and create transport factory without an ambient Client.Api dependency.
2. Bind browser and extension adapters. Capture the typed extension destination before the first submission effect and use it for image, item and snapshot requests. Keep multipart upload distinct; share error handling and destination ownership.
3. Migrate Blog and the extension, then Articles and remaining app/scaffold consumers. Keep endpoint paths/codecs in generated code and platform IPC at the extension boundary. Supply typed client dependencies to module updates.
4. Remove the extension's unused app Models/codecs/empty ClientGen references. Align Blog's item-response Image wrapper with its domain model while preserving JSON. Type the existing shared identity request/response codecs without changing OAuth or wire contracts.

**Review gate:** the same endpoint behaves through fake, browser and extension transports; failures remain typed and distinguishable; destination changes cannot redirect an in-flight publication. Build both extension artifacts from source without browser-only helpers or app-generated dependencies.

### C3 — Explicit server services and generated handler binding

1. Declare each module's Services record and a small shared author-resolution contract. Apps adapt their Env and identity implementation into those contracts.
2. Generate typed Handlers records and module route dispatch independent of Server.Env. Authored module Composition.bind closes handlers over services; the app supplies composed module dispatchers to site routing.
3. Migrate Blog, then Articles, keeping .props compile order module-owned. Preserve cookie handling, SQL/batch boundaries, event payloads and one-off app generation.
4. Remove module imports of Server.Env/Server.Identity. Keep each module's SQL and attribution statements local; let the host compose cross-module attribution policy.

**Review gate:** a HostProbe with its own environment and no compatibility namespace compiles each module. Controlled-service tests verify auth, cookies, attribution and events. Existing route contracts, SQL checks and default → NDCT → default builds pass without regeneration between site switches.

### C4 — Blog snapshots and generic storage policy

1. Move the app-local snapshot handlers into Blog. Declare the existing POST /api/blog/snapshot as a typed API, preserving itemId/sourceUrl/html and the id response. Retain omitted/null sourceUrl compatibility and existing storage strings/keys.
2. Use that generated client in the extension. Keep snapshot outcome separate from the successfully published item. Leave source-page images external.
3. Keep `GET /archive/<id>` as a module-owned raw-HTML feature route mounted by the app. Microblog enables it; Justat remains disabled. Disabled capture returns 404 using the existing JSON error envelope and performs no writes.
4. Replace the core archive-prefix literal with configured BlobServingPolicy private prefixes. Update configuration and remove the literal atomically, preserving public-blob denial for old/new/encoded archive keys. Blog supplies its prefix; apps select policy.
5. Apply the canonical response-level sandbox/CSP/nosniff policy to direct reference responses. Preserve ordinary image serving and keep generic byte/MIME/rehosting mechanisms in Hedge.

**Review gate:** local capture/read tests cover enabled/disabled hosts, auth, invalid input, missing items and storage failure. Browser tests exercise hostile direct-reference HTML. Public blob paths cannot serve archive objects. No DDL or established JSON/URL changes.

### C5 — Standalone convergence and compatibility removal

1. Move NDCT, then microblog standalone hosts onto Content.Identity/IdentityView and the same hosted content contract. Preserve their standalone products and visual behaviour.
2. Move tenant-specific chrome into app hosts. Keep content rendering in modules and scope headline-fitting callbacks with other view resources.
3. Remove duplicate module identity ownership, obsolete full-app wrappers, default-transport adapters and fixed-ID resource helpers once their consumers have migrated. An explicit session snapshot may remain in content state.
4. Update README, MODULES/MONOREPO notes and the canonical plan with implemented ownership and evidence. Run the complete cross-host acceptance matrix.

**Review gate:** one identity authority/control and one boot sync/provider load per document; working claim/merge/revert/disconnect and attribution refresh; no shell blacklist, host-server namespace dependency or DOM-only draft state. Any remaining compatibility API must identify its external consumer and an explicit follow-up.

### C6 — Alerts as an idealist-only module

New module built on the consolidated boundaries (never before C3). It is a *port* of the `alerts` branch (the pre-module monolith) onto today's module system, not a cherry-pick.

1. Add a self-contained `packages/modules/alerts` mirroring `packages/modules/blog`: `Source` + `PendingPost` domain, an `alerts_promotions` side table for provenance/dedup, the poll→pending→promote server logic (regex `parseFeed` is the documented swap point), and generic admin CRUD. Relocate the old `items.origin_entry_key` into `alerts_promotions` — do **not** add a column to blog's table or a cross-module FK; darwin.news's `blog_items` must be byte-identical afterward. Promotion inserts into the blog feed through blog's public create surface (mind `ItemCreate.ArticleDate` and the surrogate `item_tags.id`).
2. Add an inert framework `Scheduled`/cron capability (safe estate-wide without a `[triggers]` block). Compose alerts for idealist only via `gen-modules.idealist.json` + `HEDGE_SITE=idealist` and conditional `.props`/generated glue — the ndct precedent mirrored (idealist *adds* a module). Scope the hourly cron to `[env.idealist.triggers]`.
3. Keep every non-idealist tenant unchanged: same gen output, no alerts tables, unchanged `blog_items`.

**Review gate:** `test.sh` gen-stability covers the idealist manifest + alerts surface; microblog builds with and without `HEDGE_SITE=idealist`; a controlled end-to-end run (add source → cron → pending → approve → promote with an `alerts_promotions` row) passes on a local/fake service; no non-idealist tenant's schema or gen output changes.

**Out of this order** (separate action, like the darwin.news de-confounding): the live `idealist-db` data migration and the `--env idealist` deploy. Idealist keeps running from the `alerts` branch until C6 lands; retire `alerts`/`alerts-prerebase` afterward.

## Verification and constraints

Use these existing entry points as the starting gates, extending them in C0:

```sh
# Repository root
./scripts/build-all.sh
./test.sh

# From apps/microblog, after C0 provides clean, fixture-only packaging
npm run build:extension:all
```

Do not run deployment or migration commands. The current test.sh regenerates tracked output and cleans app build directories; account for that in an isolated validation checkout or preserved worktree. Commit intentional generated output with its generator change before the committed-output stability check, and then require regeneration to be byte-stable.

At each checkpoint run targeted behavioural tests and the existing full pipeline. At C0 record expected outstanding failures; at subsequent checkpoints require that checkpoint's gates and preserve every previously passing gate. Full completion requires all canonical acceptance checks, not just type-checking.

The final matrix includes Justat's integrated shell, NDCT standalone, microblog standalone at root and its existing subpath, Admin, other framework consumers, a generated scaffold and both extension targets. Preserve default → NDCT → default .NET/Fable/production builds with independent outputs and unchanged generated site selections between switches.

Run the canonical browser sequence with delayed reads/writes: paginate articles → blog → paginate → detail/reply draft → switch back → Back/Forward → return to draft → submit while navigating. Verify one public shell, retained state, correct outcomes and working scrolling. Check OAuth return/focus, deep links, metadata and asset MIME types. Install each built extension for its smoke test; a bundle alone is insufficient evidence.

Use local/fake services for writes. Record unavailable browser/tooling checks as incomplete. Keep wire paths, JSON encoding, schema/table names, stored data, themes and tenant composition compatible. A rollback must retain equivalent archive isolation and stale-operation protection.

Persistent cross-reload drafts, durable publication queues, arbitrary multi-instance product UI, generated mount registries, new shell consumers, article-image semantic adoption, image backfills and library packaging are deferred. Continue routine implementation decisions within the canonical boundaries; report a concrete contradiction before changing those boundaries or introducing a data migration.

## Required handback

For each checkpoint provide:

- Commit(s), implemented behaviour and the canonical acceptance items satisfied.
- Interfaces introduced, previous conventions removed and remaining compatibility consumers.
- Exact commands, SDK, pass/fail/unrun results and artifact/log locations.
- Browser and extension evidence, including valid completions as controls for stale-result tests.
- Any remaining failure or exception, its impact, owner and next action.

Mark C0–C5 complete only when their evidence supports it. Deliver a final report covering the whole matrix and a clean diff limited to the work order; preserve unrelated worktree changes. Production rollout remains a separate action.
