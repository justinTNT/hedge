# Unified shell — canonical implementation and consolidation plan

Updated 15 September 2026, against source through 5884eaa. **This is the implementation authority for the unified shell and the architectural consolidation identified in the subsequent reviews.** It supersedes the 14 September sequencing where the implementation has advanced. [UNIFIED-SHELL-design.md](UNIFIED-SHELL-design.md) remains background; [MONOREPO.md](MONOREPO.md) supplies the framework philosophy. The executable handoff is [UNIFIED-SHELL-work-order.md](UNIFIED-SHELL-work-order.md).

This revision is a plan, not a claim that its acceptance checks have passed. Implementation already includes the two-module Justat shell. Continue from that baseline; do not recreate the articles-only intermediate shell. No production deployment or database migration is part of this work.

## 1. Objective and current position

Finish the shell's state/lifetime contract and make the framework/library/app division enforceable through typed interfaces and tests. The result must preserve current content, wire formats, URLs, themes and tenant composition while removing the conventions that caused the repeated review defects.

| Original stage | Current position | Remaining obligation |
| --- | --- | --- |
| 0 — Hosting interfaces | Present in both content modules | Make resource ownership and callback cancellation effective throughout page helpers. |
| 1 — Articles-only shell | Superseded by the integrated implementation | Retain the established standalone entry choices for NDCT and microblog. |
| 2 — Articles + blog in Justat | Integrated; acceptance remains incomplete | Preserve drafts and operation outcomes; establish per-request validity and scoped resources. C1 closes this stage's outstanding contract. |
| 3 — Shared ownership | Content.Identity and IdentityView extracted; legacy ownership remains | Move standalone hosts onto the shared identity component and delete transitional module ownership in C5. |
| 4 — Optional expansion | Deferred | Generated client mount registries, persistent drafts and additional shell products remain separate work. |

Recent fixes are the starting point: cached-feed loading resets, retained mutation errors, pinned extension destinations, admin FormSeq, archive-key blocking and the extension .NET build gate. Preserve their protection while replacing temporary implementations. The review of 7962bf6 was architectural; establish its runtime baseline in C0 rather than treating its commit message as verification.

The delivery order is **C0 → C1 → C2 → C3 → C4 → C5**. Each checkpoint is buildable and reviewable independently. Generated source and composition wiring may change in C2–C4. Existing SQL schema, table names, stored values and HTTP paths must remain compatible; none of these checkpoints requires DDL.

A subsequent **C6** adds the *alerts* feature as an idealist-only module built on the consolidated boundaries (§8·C6). It is sequenced after C5 because, unlike C0–C5, it is a new module: it introduces new (additive) tables, a new framework cron capability, and a separate live idealist-db migration. Those last two sit outside this order's "no new framework surface without a consumer / no migration" scope and land with, or after, the C6 code.

## 2. Boundary decisions

These are decisions for implementation, not alternatives for the dev to choose again.

| Owner | Owns | Concrete application |
| --- | --- | --- |
| Hedge framework | Model semantics; generation; codecs and errors; transport contracts; generic routes, storage primitives and admin | Image → schema attribute → admin upload control; generated HTTP contracts; configurable public-blob access; generic form-operation tokens. |
| Content modules | Domain/API definitions, content state, queries, validation and feature behaviour | Blog owns items, comments, tags and source snapshots; articles owns posts/comments. Each owns the validity and outcomes of its content operations. |
| Ordinary libraries | Shared identity experience, host lifecycle helpers, rich-text integration and platform adapters | packages/content-client, packages/rich-text and packages/hedge-extension. A small content-server contract holds shared author-resolution types. |
| Apps/sites | Composition, mounts, chrome, credentials and feature policy | Justat's sidebar; NDCT's hero; tenant branding; which module is primary; snapshot enablement; environment-to-module service binding. |
| Extension product | Blog publishing workflow and its UI | apps/microblog/extension composes Blog's generated client with the extension transport. Generic Chrome messaging belongs in packages/hedge-extension. |

Keep the existing uniform module API/table prefixes and the shared, unprefixed identity base. Identity remains one host-owned authority per document. API mounting, content mounting and deployment base remain distinct.

Use ordinary records, discriminated unions, functions and Elmish composition. Keep the concrete Articles/Blog shell messages. File linking through module-owned .props remains supported; changing packaging is unnecessary for this plan. There is no service locator, runtime plugin registry, general effect framework or wholesale UI rewrite.

Mutable editor/socket handles belong in resource adapters. Draft text, request validity, publication destination and operation outcome belong in explicit application state. Browser interop stays at the Chrome, DOM, TipTap and Workers boundaries.

## 3. C0 — Capture the baseline and establish the gates

**Files:** test.sh, scripts/build-all.sh, focused fixtures under test/, existing app build scripts.

Promote the meaningful review probes into maintained fixtures, using actual compiled modules and injectable transport/resource boundaries. Include both a valid completion and an obsolete completion in each race test. Do not encode the current implementation's mistakes as expected behaviour.

Record the baseline in three groups: passing checks, reproduced outstanding defects, and checks not run. Check the latest fixes, including encoded archive keys, settings changes during extension submission, comma-containing srcset URLs and reopened admin create forms. Outstanding shell draft/lifetime cases become the C1 acceptance gate; they are not waived because a build passes.

The extension gate added in 7962bf6 is a .NET type-check. Add a clean Fable compile and bundle/package verification for both Chrome and Firefox. Use isolated output directories, exclude developer-specific sites.json, and verify manifests, popup assets and background entry files. Checked-in fable_output is not an input to this verification. Detect restore/compiler errors as well as process exit codes: Fable has previously continued after a restore failure.

Record the .NET SDK and build commands used. Respect an existing repository SDK selection; if none exists, select and document the validated installed SDK rather than changing developers' global installations. Keep default/NDCT outputs separate during checks and retain the default → NDCT → default sequence.

**Exit:** the build/consumer matrix is explicit; recent fixes have reproducible checks; remaining failures are assigned to C1–C5. No generated migrations or production writes are needed to establish this baseline.

## 4. C1 — Make content state and operation lifetimes explicit

**Files:** packages/content-client/HostContext.fs and a new Lifecycle.fs; packages/rich-text/RichText.fs plus its JS lifecycle adapter; both modules' Client/Types.fs, App.fs and Pages/*; apps/articles/src/Client/Shell/{Types,App}.fs; shared Admin lifecycle callers where affected.

### C1a. State and completion ownership

Introduce small transient identity types for module instance, activation, request and operation. These are client state, not persisted IDs or new database wrappers.

Each content model owns:

- Its active activation, or an explicit inactive state.
- Cached pages together with their query, cursor and content revision.
- Read state that relates a pending request token to the previous usable result.
- Drafts keyed by item/reply target, and a separate key for each new-item form.
- A monotonic draft revision and a retained operation map.

A read token carries instance, activation and request identity. Pagination also records the originating query and cursor. Accept a result only against the request it completes. Invalidating a read restores its cached data or idle state and clears its in-flight state. Re-entering a feed reattaches its watcher and runs the fill-to-viewport check without discarding loaded pages.

Represent a submission with its operation ID, immutable request, target, originating draft key/revision and outcome. Outcomes distinguish pending, confirmed success, confirmed rejection and an uncertain outcome. A lost or undecodable acknowledgement must not be described as a confirmed server rejection or cause automatic resubmission. Navigation never retries writes.

Keep operation completions deliverable after the view activation ends. Apply them to their originating operation even while the child module is inactive. Successful completion clears only the submitted draft revision; edits made after submission survive. Rejections and uncertain outcomes remain associated with the operation until acknowledged or explicitly retried. A background operation must not clear another item's editor or navigate the currently active view.

Invalidate cached content after successful writes and identity reattribution. Mark inactive caches stale; refresh the active view appropriately. Identity refresh must not silently reuse a feed whose attribution is obsolete. Preserve pagination for still-valid caches.

Use small load/submission unions instead of unrelated booleans where those booleans currently represent one operation. Apply this to the touched content flows; it is not a repository-wide state-style conversion.

### C1b. Effects, resources and the shell boundary

Publish editor changes into the draft model through an onChange message. SubmitComment and SubmitItem construct requests from model state. Reading the editor DOM during update is removed from these flows. The rich-text wrapper gains additive initial-content, change-callback and cancellation/disposal support; existing admin callers remain compatible during migration.

Create a resource scope per instance and activation. It owns sockets, editor handles, sentinel listeners, timers and animation-frame callbacks, including deferred editor creation. Disposal is synchronous and idempotent. A disposed scope cannot create a late editor or fire a view callback; disposing an old scope cannot destroy an incoming scope's resources. IDs derive from the instance and purpose rather than shared comment-editor constants.

Pending server writes belong to the document's operation lifetime, separate from view-resource disposal. Keep their completion delivery alive when a view is disposed. Cross-reload persistence remains outside this plan.

The child contract remains empty/enter/leave/update/view, extended with module-owned token validation and explicit host signals. Updates compute state and deferred commands; command construction does not touch the DOM or network. Navigation and document-title effects are emitted as activation-qualified host signals. The existing Identity.Signal pattern is the precedent.

The shell owns route selection, activation assignment, ordered leave/enter and identity fan-out. It forwards child messages to the retained child; the child decides whether a content completion is valid. Remove articlesStaleDrop/blogStaleDrop only in the same change that makes the children validate all affected reads, events and writes. The shell must no longer enumerate GotSubmitComment/GotMoreFeed cases to decide their semantics.

On navigation: invalidate the outgoing activation, dispose its view resources, then enter the incoming activation. Preserve the keyed LeaveCompleted barrier. A newer navigation supersedes an older pending transition.

**C1 exit checks:**

- Paginate articles → blog → articles; loaded pages and cursors survive and scrolling continues.
- Leave an initial/detail/pagination request pending, navigate, then resolve it: no stale view, spinner or pagination lock.
- Switch between two items in one module with responses resolving in reverse order.
- Write a reply draft, switch modules and return: content and reply target survive.
- Submit draft A, edit it further or open draft B, then resolve success/error/transport failure: retain the correct outcome and preserve the newer draft.
- Close a view before deferred editor creation; navigate rapidly; dispose twice: no orphan or cross-instance resource effect.
- Exercise two separate contexts in a fixture without shared DOM IDs or handles.
- Run the same content transitions through standalone adapters; preserve NDCT and microblog behaviour.

C1 closes the original stage-2 state/lifetime gate once the routing and browser checks in section 9 also pass. Later architectural checkpoints do not retroactively waive those requirements.

## 5. C2 — Generate clients against an explicit transport contract

**Files:** new packages/hedge/src/Hedge/Http.fs; Gen/Program.fs and Scaffold.fs; generated module client surfaces; packages/hedge/src/Client/Api.fs; packages/hedge-extension/{Api,Chrome}.fs; extension background.js/Popup.fs/Extension.fsproj; consuming client composition files.

Add a platform-independent HTTP runtime in Hedge. Its contract is a request record (method, relative path/query, headers and optional body text), a response record (status, headers and body text), and a transport function returning a promise/result. Separate transport failure, HTTP rejection/validation details and response-decoding failure in a shared ApiError type.

Gen emits a typed Client record and a create transport factory for each module. Each endpoint function owns the generated path/query construction and the request/response codecs. This is the only authored-to-wire mapping for generated endpoints. The generated surface has no dependency on a host's Client.Api namespace, window globals, Chrome APIs or an app's Models assembly. Pure event codecs can remain generated alongside it; socket opening stays in a platform adapter.

The browser adapter applies the deployment base once and preserves current cookie behaviour. The extension adapter binds a typed destination record containing URL and credentials once per submission, and carries that immutable destination through image, item and snapshot requests. Its IPC messages use explicit records and boundary codecs; obj is confined to the platform interop conversion.

The background broker executes transport requests. Endpoint paths and blog response-shape knowledge stay in the generated client/publishing controller, not duplicated in background.js. Multipart image upload remains a distinct transport operation using the same destination and error model; do not encode binary uploads as JSON.

Consolidate HTTP error decoding in the generic runtime. The UI renders typed errors; it does not infer meaning from arbitrary error objects. Give the existing shared identity client calls typed codecs while preserving their established endpoint bodies and OAuth behaviour.

Migrate consumers in order: blog clients and extension, articles clients, remaining app/scaffold consumers of generated clients. Compatibility wrappers may bind a default transport while a consumer migrates; their removal is required before C5 completion. Module updates receive a constructed typed client through dependencies rather than locating an ambient Client.Api module.

The extension project references BlogModels, Blog's generated surface and the extension adapter. Remove its references to microblog's empty app ClientGen, app codecs and app Models once unused. Keep the publishing product in apps/microblog/extension.

Align Blog.SubmitItem.Item.Image with Blog.Domain.Item.Image by using the framework Image wrapper instead of Link. Update the typed mapping/consumers and regenerate codecs; the JSON value remains the same optional URL string. Keep submission input validation compatible. This closes the concrete model/API semantic mismatch without a field-name heuristic in the admin.

**C2 exit checks:**

- Run the same generated endpoint through a fake transport, browser adapter and extension broker. Assert the same request body/path and decoded response.
- Compile an extension client without browser query/socket helpers or microblog app-level generated files.
- Preserve all existing module paths, JSON keys, wrapper encoding and event names.
- Test omitted/encoded query values, path parameters and deployment base handling.
- Verify HTTP validation, non-JSON HTTP failure, invalid success JSON and transport failure remain distinguishable.
- Change/delete the configured site during every submission phase: every request still uses the original destination.
- Clean-build both extension artifacts and the affected site/scaffold artifacts.

## 6. C3 — Give server modules explicit dependencies and binding

**Files:** module-owned Server/Services.fs and Composition.fs; both modules' Handlers.fs and .server.props; a small packages/content-server identity contract; Gen module route output; app Server/ModuleServices.fs and generated dispatch wiring.

Remove content-module imports of Server.Env and Server.Identity. A module declares its own Services record containing the capabilities its handlers actually consume: D1/R2/event resources, author resolution, authoring authorization and the clock/ID functions needed for deterministic operations. Keep the record small; add fields because a handler uses them.

Define a shared author-resolution result in ordinary content-server code. Apps adapt their current identity implementation to that contract. Module handlers receive resolved identity data without knowing an app's generated IdentityRow or environment namespace. Preserve the current guest/cookie behaviour and statement/batch boundaries.

Each module continues to own its SQL and comment-reassignment statements. The app's attribution policy composes the participating modules when an identity is merged or disconnected. No module queries a sibling's tables; no generic framework registry decides content attribution policy.

Extend module-owned generation with a route/handler contract:

- A generated Handlers record describes the decoded arguments of each endpoint and its Worker response function. The bound delegates take request/execution context as needed and close over Services.
- Module route generation decodes path/query/body values and dispatches through this record. It imports framework and module types, never Server.Env.
- Authored Composition.bind services constructs the handler record from module functions.
- The site's generated dispatcher accepts the composed module dispatchers. The app constructs those dispatchers from its environment before use; it is the one place that adapts Env into module Services.
- Preserve the existing generation path for one-off apps while migrating its callers. Do not force music/archive/basewatch to become content modules.

Returning WorkerResponse remains supported for cookies, status codes and raw responses. Do not require a broad server effect-system rewrite or change existing response envelopes to obtain dependency isolation.

Adopt blog first, then articles. Keep module .props as the compile-order owner, including its new generated route contract and authored binding files. Regenerate checked-in surfaces and scaffold output in their respective changes.

**C3 exit checks:**

- Compile a minimal host named HostProbe that imports each module with its own environment type and no Server.Env/Server.Identity compatibility namespace.
- Run content handlers with controlled services; assert authorization, comment attribution, cookie handling, SQL parameters and event payloads.
- Verify identity merge/disconnect affects every composed content module and no uncomposed module.
- Run SQL checks and default → NDCT → default server builds without regenerating between site switches.
- Generated route tests preserve existing HTTP methods, paths and decoding rules.

## 7. C4 — Consolidate snapshots and make blob access policy generic

**Files:** Blog Models/Api.fs and module-owned Server/Snapshots.fs; Blog Services/Composition; app Worker/ModuleServices configuration; Hedge Router/Workers; Gen/scaffold wiring; extension publishing controller.

Blog owns the complete source-snapshot feature because ItemSnapshot already belongs to Blog.Domain. Move the capture handler, snapshot persistence and reference-serving logic into the blog module. Leave HTML rewriting beside that feature for now; do not create a general archive package without another consumer.

Add a typed Blog capture request/response endpoint at the existing POST /api/blog/snapshot path. Retain the itemId, sourceUrl and html request keys and the id response key; itemId becomes a typed item reference. Preserve the existing acceptance of an omitted/null sourceUrl as an empty stored value. The extension uses its generated client. Validate at the boundary and handle malformed JSON through the established API error contract.

Use typed internal capture-kind/outcome values and explicit conversion to the existing stored strings. Retain blog_snapshots, its columns, FK and R2 keys. This is ownership and validation work, not a migration or image backfill. Source-page images remain external; only the post's selected image is rehosted.

Keep `GET /archive/<snapshotId>` as an explicit raw-HTML feature route owned by Blog.Snapshots and mounted by the app. HTML serving does not masquerade as a JSON endpoint. Microblog keeps snapshot capture/reference access enabled; Justat keeps it disabled unless separately requested. Disabled capture returns HTTP 404 in the existing JSON error envelope and does not write; the host does not mount the reference route. Existing snapshot storage remains protected even when capture is disabled.

Replace the framework's literal archive-prefix branch with a typed BlobServingPolicy on WorkerConfig containing configured private key prefixes. The generic blob route decodes the key once, rejects malformed encoding, and applies whole-prefix access rules before reading or serving an object. It knows no blog route or archive directory name. The blog feature supplies its private storage prefix; consuming apps configure the policy, and other apps/scaffolds explicitly configure their own policy.

Update policy configuration, app wiring and the removal of the hard-coded block together. All existing archive keys must remain denied through the public blob route throughout the transition, including percent-encoded paths. Do not rely on content sanitization as the public-access barrier.

The dedicated HTML response enforces its own isolation with a response-level CSP sandbox without allow-same-origin, allow-scripts or allow-forms, plus script-src 'none', base-uri 'none', form-action 'none' and nosniff. Retain the existing restrictive default-src and permitted image/font/media schemes. It must remain isolated when opened directly; a removed reader iframe cannot supply its protection. Sanitization is additional protection. Verify the actual emitted headers and rendering behaviour on that route.

Keep generic MIME checks, byte upload/serve and remote-image copying in Hedge.Workers. Selection of the image, fallback behaviour, storage prefix and whether capture is enabled belong to the publishing/content feature and app configuration. Image remains the framework schema semantic; broader adoption on article hero fields is separate work.

**C4 exit checks:**

- Capture with the generated client, persist through an in-memory/local D1/R2 fixture, then read the same snapshot through the dedicated route.
- Test enabled and disabled hosts, authorization, malformed input, missing items and failed storage. A failed snapshot cannot turn a successfully published item into a failed item submission.
- Existing and newly captured archive objects are unavailable through public blob URLs, including encoded keys; ordinary images still serve correctly.
- Test direct reference navigation with hostile HTML fixtures in a browser; verify response-level isolation independently of sanitization.
- Framework source/scaffold defaults contain no blog-specific archive prefix or route.
- Schema/DDL and existing JSON/URL contracts remain unchanged apart from newly generated representations of the already-existing snapshot API.

## 8. C5 — Converge standalone hosts and remove transitional ownership

**Files:** packages/content-client/{Identity,IdentityView,HostContext,Lifecycle}.fs; standalone entries under apps/articles and apps/microblog; both content modules' client App/Types/Shared files; obsolete client adapters; README and notes/MODULES.md/MONOREPO.md.

Use the existing Content.Identity and IdentityView as the single client implementation. Migrate NDCT's standalone articles host first, then microblog's standalone blog host. Neither adopts Justat's two-module shell. Each host owns one router, one identity model and its own chrome, and supplies the same content contract used by Justat.

Move NDCT's hero and tenant-specific navigation/sidebar decisions into app hosts. Shared feed, detail and comment rendering stay in their content modules. Preserve existing branding, CSS and headline-fitting behaviour, with fitting callbacks scoped like other view resources.

Remove module-owned identity menus/messages/state once every consuming entry has migrated. Content modules receive an explicit session snapshot for rendering/submission; a compatibility GuestSession field may remain only as that snapshot, never as an independent identity authority. Migrate all consumers before deleting old standalone full-app APIs, default-transport wrappers or fixed-ID editor helpers.

Complete the default → NDCT → default matrix and the microblog root/subpath matrix. Update repository docs to describe the implemented owners and commands, and retire instructions that tell developers to copy identity code or omit extension builds. The unified shell plan remains the authority until its exit evidence is recorded.

**C5 exit checks:**

- One identity sync/providers request and one identity control per document in all three host shapes.
- Claim, merge, revert and disconnect work on both content modules and standalone hosts; active attribution refreshes and inactive caches are invalidated.
- No content module imports host-specific server namespaces, owns a browser-global identity authority, or depends on a default browser transport.
- No shell message filter enumerates child operation results; no content draft depends on an editor instance surviving navigation.
- No deprecated compatibility API remains without an identified external consumer and an explicit follow-up.
- All acceptance checks below pass; record any unrun check as incomplete work.

## 8·C6 — Alerts as an idealist-only module (post-consolidation)

**Files:** new `packages/modules/alerts/**` (mirror `packages/modules/blog`); framework `packages/hedge/src/Hedge/{Router,Workers}.fs` for a `Scheduled` capability; `apps/microblog/gen-modules.idealist.json`, conditional `.props`, `Routes.idealist.fs`/`AdminGen.idealist.fs`, `wrangler.toml` `[env.idealist.triggers]`, `package.json` `deploy:idealist`, `src/Server/Worker.fs`.

Sequenced after C5, and never before C3, so the alerts module is *born* on the finished boundaries instead of being migrated through them. Rationale: it is almost entirely a server+cron feature (generic admin, no bespoke client), so its hard prerequisite is **C3** (module `Services`/generated `Handlers`/`Composition.bind`, no `Server.Env` coupling); its closest sibling is **C4** (per-site *feature enablement* — the "cron only on idealist / disabled → 404, host mounts the route" pattern reuses C4 directly). C1/C2 barely touch it. Built last, it is the first brand-new module on the consolidated architecture — an end-to-end validation of C1–C5.

Scope, drawn from the alerts branch (the pre-module monolith — this is a *port*, not a cherry-pick):
- Extract a self-contained `packages/modules/alerts`: `Source`, `PendingPost`, and a `Promotion` side table (`alerts_sources`/`alerts_pending_posts`/`alerts_promotions`), the poll→pending→promote server logic (`parseFeed` regex Atom parser is the documented swap point), and generic admin CRUD.
- **Crux — never touch blog/darwin.news schema:** `origin_entry_key` (today a column on the blog item) is a non-load-bearing dedup+"already-promoted?" stamp. Move it into the alerts-owned `alerts_promotions` table (mirroring blog's own `ItemSnapshot` side table). A module must not add a column to another module's table, and a cross-module `ForeignKey<Blog.Item>` is rejected — keep provenance a bare `EntryKey`. Promotion still *inserts* into the blog feed via blog's public create surface (mind main's `ItemCreate.ArticleDate` requirement and the surrogate `item_tags.id`).
- Add the framework `Scheduled`/cron hook (inert without a `[triggers]` block, backward-compatible estate-wide) and compose alerts for idealist only via `gen-modules.idealist.json` + `HEDGE_SITE=idealist`, the ndct precedent mirrored (idealist *adds* a module vs ndct *omits* one).

**Out of this order (separate action, like the darwin.news cleanup):** the live `idealist-db` data migration (rename `alert_*`→`alerts_*`, seed `alerts_promotions` from existing `origin_entry_key` values, drop the column, plus the standard blog drift) and the `--env idealist` deploy. Until C6 lands, idealist keeps running from the `alerts` branch. Retire `alerts`/`alerts-prerebase` once idealist runs from main.

**C6 exit checks:** `test.sh` gen-stability now exercises `gen-modules.idealist.json` + the alerts module surface; the microblog builds with and without `HEDGE_SITE=idealist`; darwin.news (and every non-idealist tenant) build byte-identically with no alerts tables and an unchanged `blog_items`; a controlled end-to-end run adds a `Source`, the cron produces a `PendingPost`, approval promotes it into the blog feed with an `alerts_promotions` row.

## 9. Cross-cutting acceptance and compatibility

| Concern | Required evidence |
| --- | --- |
| Generation and schemas | Existing gen stability, module surfaces, SQL checks, wrapper/schema codec round trips and scaffold checks pass. New generated contracts have fixtures. No unintended DDL or wire changes. |
| Consumer builds | Framework changes type-check all current apps plus Admin and extension. Affected clients/servers Fable-compile and bundle from clean outputs. Both extension packages are assembled and inspected. |
| Composition | Justat has one public shell for articles/blog plus admin; NDCT remains articles-only; microblog remains blog standalone. Manifests, conditional imports and emitted entries agree. |
| Navigation | Deep links, refresh, Back/Forward, query/fragment handling, normal SPA clicks, modified clicks, new tabs, anchors, downloads and external links retain correct ownership. Unknown routes cannot masquerade as another module. |
| URL boundaries | Whole-segment mounts distinguish /blog from /blogger. Deployment /st prefixes transport and assets once; it does not turn /api/blog into /blog/api/blog. Existing microblog subpath deployment is preserved. |
| Identity | Boot, unavailable providers, claim focus, same-origin return validation, merge/revert/disconnect and attribution refresh pass with delayed responses. Login remains a normal OAuth document navigation. |
| Resource lifetime | Delayed reads/writes, rapid same-module/cross-module navigation, deferred editor creation and repeated disposal pass with no stale effects or leaked resources. |
| Publishing | Real API/IPC boundaries cover pinned destination, image upload/fallback, structured errors, snapshot outcome and form generation. Install each built extension for a browser smoke test; bundling alone is insufficient. |
| Metadata and assets | Article/blog social metadata, canonical URLs, auth/API routes, blob MIME types, WebSocket upgrades and existing standalone mount consumers retain behaviour. Verify asset Content-Type as well as status because SPA fallback can mask missing files. |

The browser shell acceptance sequence is: paginate articles → switch to blog → paginate blog → open detail/reply and type → switch back → Back/Forward → return to the draft → submit while navigating. Assert no document reload for hosted navigation, one header/main/identity control, retained valid pages/drafts, correct outcomes and continuing pagination.

Justat retains its sidebar on articles routes and omits it on blog routes. The shell owns that decision. Preserve path-mode startup and parse query/fragment separately; do not use a hash-oriented router initializer for public path routes. Preserve safe OAuth return URLs and claim focus, and consume claims once.

Existing path-mounted bundle support remains a framework capability even though Justat now uses integrated components. The old cross-document claim handoff is removed only after confirming no supported consumer still needs it; it is not reintroduced into Justat.

Run targeted tests as each change lands and the existing complete pipeline at each mergeable checkpoint. Record SDK, commands, target composition and failures/unrun checks. A green .NET build is not evidence of a working Fable artifact or browser flow.

## 10. Delivery, rollback and definition of done

Use small commits within each checkpoint: shared contract/runtime first, one consumer at a time, then compatibility cleanup. Keep adapters until the next consumer is verified. Changes to generated contracts include their generator, generated output, scaffold consumers and tests in the same reviewable checkpoint.

Source and wire compatibility let each checkpoint roll back to its preceding verified artifact without data reversal. Rollbacks must retain equivalent archive isolation and stale-operation protection; do not restore a known-vulnerable artifact merely to restore older wiring. Production rollout is a separate action after this implementation and its review.

The implementation report must map C0–C5 to commits, identify removed conventions/compatibility APIs, list checks and artifact locations, and name any remaining exception with its owner. Completion means the boundaries are exercised through real consumers and the state/operation guarantees survive the cross-host tests.

Deferred work remains: persistent cross-reload drafts, durable background publishing queues, richer per-history-page caches, generated client mount metadata, arbitrary simultaneous-module UI, binary/NuGet packaging, new shell consumers, image backfill and AWS retirement. None is needed to complete the work above.
