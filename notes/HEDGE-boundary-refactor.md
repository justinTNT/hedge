# Hedge boundaries: typed requests, access state and admin exposure

Date: 25 September 2026

Status: implemented and verified on `hedge-framework-boundaries` and the isolated `native-plants-boundaries` integration branch. Ready for review; not merged to main or deployed.

Framework baseline: `main` at `516f2d9`; working branch `hedge-framework-boundaries` in `~/Play/hedge-framework-boundaries`.

App integration baseline: `native-plants` at `40f0697`; original checkout `~/Play/hedge-native-plants`. Integration branch/check-out: `native-plants-boundaries` in `~/Play/hedge-native-plants-boundaries`. Historical design links below remain pinned to `868a9b1`.

## Outcome

Finish three boundaries exposed by Native Plants:

1. Ordinary contribution JSON uses Hedge's typed contracts, generated codecs and clients.
2. Credential changes and confirmed capabilities drive app/admin navigation through supported client interfaces.
3. An app explicitly selects its admin resources and their supported operations, independently of who is requesting them.

This is one bounded refactor with independently reviewable slices. Preserve the current user experience, data and authorization policy. Success is less handwritten protocol glue and clearer ownership, not more reusable content modules.

## Context and existing decisions

Read these alongside the code; historical plans describe their original baseline:

- [Identity and access-controlled admin handoff](IDENTITY-ACCESS-CONTROL-overarching-plan.md): framework mechanisms, identity persistence and host permission policy.
- [Identity extraction blueprint](PHASE1-identity-extraction.md): shared identity composition and compatibility obligations.
- [Native Plants identity integration](https://github.com/justinTNT/hedge/blob/868a9b172e64aad3409498b223c446d803d9eaa5/apps/native-plants/IDENTITY-INTEGRATION.md): direct login and contribution ownership.
- [Current contributions](https://github.com/justinTNT/hedge/blob/868a9b172e64aad3409498b223c446d803d9eaa5/apps/native-plants/CONTRIBUTIONS.md): authoritative implemented policy, limits, review and storage lifecycle.
- [Private preview](https://github.com/justinTNT/hedge/blob/868a9b172e64aad3409498b223c446d803d9eaa5/apps/native-plants/PREVIEW.md): deployment boundary and existing verification.
- [Future field notes / identification](https://github.com/justinTNT/hedge/blob/868a9b172e64aad3409498b223c446d803d9eaa5/notes/NATIVE-PLANTS-field-notes-and-identification.md): future domain work, outside this refactor.

Native Plants already uses shared identity and grants. This plan does not repeat their extraction or introduce a new role model. App documentation links above are pinned to the reviewed app baseline because those files do not exist on main yet; use the local Native Plants checkout for current integration work.

## Branch ownership and prerequisite

This is one plan with two implementation tracks. The complete plan is maintained here on `hedge-framework-boundaries`; Native Plants keeps a short handoff note rather than a second editable copy.

| Framework branch from main | Native Plants integration branch |
| --- | --- |
| Request-aware generated GET handlers and framework fixtures. | Contribution endpoint contracts, typed operation adapters and old-client compatibility. |
| Optional session-aware browser transport and bounded-read utility if needed. | Personal/Review/Access client conversion and app request policy. |
| Shared admin credential notifications and credential-stale response handling. | Main-app/admin-page capability refresh and navigation. |
| Admin descriptor operation ceilings, generator updates and enforcement tests. | Explicit admin exposure list and read-only Identity. |

The framework branch first ports the reusable portion of `53aa2702c7072631c8750f477bde1cf84cf7c534` ("Support account-focused apps with safe shared sessions and logout"). That source commit is on Native Plants but not the framework baseline. Port its 17 shared files exactly; omit only `apps/native-plants/src/Server/Worker.fs`, whose `AllowGuestUploads = false` line is already present in the app. Record the original commit in the port's commit message and run the repository gate against main plus the port.

Shared identity/grant extraction is already on main. There is no dependency on bringing the entire Native Plants app to main. The prepared branch does not include the Capacitor branch's work; both efforts must reconcile any future edits to shared session, HTTP and admin files before merging.

For each framework slice, use a temporary integration branch/worktree based on Native Plants to combine the proposed shared commits with the corresponding app slice and test the real consumer. Keep app-specific changes out of the framework PR. Merge the reviewed framework work into main only after that integration passes, then bring main back into Native Plants and finish/retain its app commits there. Use one deliberate integration path; avoid independently cherry-picking the same evolving framework commits into several long-lived branches.

Merging the port back into Native Plants will encounter shared changes already present under the original commit hash. Inspect that merge as such; preserve both histories and do not reapply the session patch to the app. No forced rewrite of the published Native Plants branch is required.

### Preparation completed

- Framework prerequisite commit: `0e448ae`, based on main `516f2d9`.
- All 17 ported files were checked byte-for-byte against source commit `53aa270`.
- The full repository `./test.sh` passed in the fresh framework checkout, including generated outputs, host builds, session fixtures and the scaffold production build.
- Framework/app slices 1–5 are now implemented and verified as recorded below. Main, the original Native Plants checkout and deployed applications remain unchanged.

## Implementation and verification — 25 September 2026

- Shared implementation: `9c72c26`; API documentation: `7e48e7e` in
  `packages/hedge/README.md`. Request-context opt-in works for all four GET
  shapes in standalone and module routing. The browser transport's missing
  JSON content-type header was also corrected and covered by a runtime fixture.
- Native Plants implementation: `87438ad`, after deliberately merging the
  shared branch into `native-plants-boundaries`. Shared prerequisites were
  preserved under both histories; the only merge conflict was the test runner's
  insertion point. App code is absent from the framework branch.
- Private v2 JSON uses generated contracts, codecs and clients. V1 adapters
  remain for one compatibility release and call the same typed commands/SQL.
  The streaming cap stays app-owned: authorization precedes reading the body,
  and generated routing receives only the bounded reconstructed request.
- Multipart/media remain app-owned. The upload adapter consumes its response
  inside the session lock, then loads a typed snapshot in a separate lock.
- Owner-key notifications are shared; the main app/admin shell use the same
  capability loader. Storage polling is removed, server revalidation retained,
  and stale credential/session results discarded. Common admin state is cleared
  when the applied key changes. Identity/grant persistence is unchanged.
- Native Plants explicitly registers generated descriptor values. Identity's
  list/read ceiling is enforced for the owner; private tables and future
  generated descriptors remain absent. Other hosts retain existing CRUD.
- Full `./test.sh` passed on both the framework and combined integration trees,
  including nine new compiled boundary/admin fixtures and all existing host,
  session, generator and scaffold checks. Native Plants production build,
  18 importer tests, 95 Node tests and preview checks passed (4,041 referenced
  media paths; 4,610 deploy assets). Schemas and migrations have no changes.
- Browser checks on an isolated local Worker covered anonymous denial, owner
  key application, immediate navigation updates, read-only Identity and typed
  review loading. Verified contributor/curator behavior, revocation, uploads,
  renewal and races have compiled Worker/SQLite coverage; this work did not
  repeat live provider sign-in or browser file selection.
- Remote refs were refreshed and the concurrent `hedge-capacitor-microblog`
  worktree inspected; its committed addition is a plan on the shared baseline.
  No shared implementation from that branch was imported or modified.

Remaining release actions: review the two implementation tracks, merge the
shared branch into main, then bring main into Native Plants and retain the app
commit through the existing integration history. No deployment or old-client
retirement is part of this implementation. See Native Plants `CONTRIBUTIONS.md`
and `PREVIEW.md` for the compatibility window and local verification record.

## Findings the implementation must accommodate

- Catalogue endpoints in `Models/Api.fs` use generated clients/codecs. Contributions use an `Action` string, dynamic fields, handwritten routing and `JSON.stringify`; `Client.Personal.request` promises an arbitrary `'T` without decoding it.
- Current contribution JSON uses PascalCase fields. Hedge's codecs use camelCase. Changing the decoder alone would break existing pages, including tabs open during deployment.
- Generated GET handlers receive decoded parameters and the environment, but no `WorkerRequest`. The Alerts API explicitly uses POST for its queue to obtain the request for authorization. Protected reads need a real request-aware binding.
- Gen currently discovers actual nested `Request`/`Response` records under endpoint modules. F# type aliases alone are not a sufficient declaration. The runtime codec supports lists; the current contribution DTOs use arrays.
- Generated POST routing currently calls unbounded `request.text()` before the authored handler. Contributions currently enforce JSON content type and a streamed 24,000-byte limit. Moving routing must preserve that limit and the authorization/origin checks.
- The browser transport already supports headers, injectable transport and typed HTTP errors. The shared guest runtime already serializes protected requests against logout. Compose these facilities.
- `admin-access.mjs` polls the saved `adminKey` every second because the common admin provides no same-document credential-change notification. Capabilities themselves already come from server authorization.
- `AdminConfig.fs` excludes private tables by name and represents the owner as `AdminSubject` so Identity can be read-only. Admin resource support and caller permission are currently conflated.

## Responsibility boundaries

| Owner | Responsibilities in this change |
| --- | --- |
| Hedge framework | Request-aware generated binding, typed transport integration, credential-change notification, admin operation ceilings and enforcement. |
| Identity module | Existing account lifecycle, active-subject resolution and grant persistence. No plant-specific permissions. |
| Native Plants | Contribution contracts and operations; ownership, viewer tokens, reviewer policy and capability names; exposed admin resources; navigation and page state. |
| App media code | Multipart upload, image preparation/validation, private media delivery and R2 lifecycle. |

Keep taxonomy, catalogue caching, photo promotion semantics, correction status and personal hero choices app-owned. Keep `Hedge.Http` transport-neutral: no DOM, localStorage, guest runtime or plant imports in that module.

## Behaviour that must survive every slice

- Verified contributors see only their own ordinary notes/photos. Five notes and five personal photos per species remain server-enforced, including upload reservations.
- Stable ownership stays keyed on the verified provider/account pair. Legacy anonymous-content claiming, claim tombstones, revision checks and viewer-token checks remain intact.
- Curators review corrections and offered photos; they cannot edit the catalogue, assign grants or browse unoffered material. Grant revocation is checked on each protected request.
- The owner key permits catalogue editing, grant management and contribution review. Identity is read-only. Raw guests, personal tables and claim markers are absent from generic admin.
- OAuth logout clears the browser session and private UI. It does not remove an independently saved owner key. Removing that key does not log out an OAuth identity.
- Existing cookie renewal, same-origin checks, private/no-store responses and private-prefix protection survive the routing changes.
- Old responses cannot restore private content after logout, account/key change, route change or loss of permission. Same-owner refreshes preserve unfinished drafts.
- Published-photo copies survive deletion of the author's private copy. Promotion remains idempotent and retains current R2 cleanup/accounting rules.
- No database/schema migration, reseed, archive import, provider reconfiguration or new privilege is expected.

## A. Bring contribution JSON into the typed pipeline

### A1. Prove the missing framework seam with a protected read

Add a small opt-in endpoint declaration for request context, provisionally `let requestContext = true` alongside `endpoint`. Gen reflects it and passes `WorkerRequest` and `ExecutionContext` to that endpoint's server handler after decoded arguments. Existing declarations keep their current signatures and generated behaviour.

Implement this consistently in the standalone route emitter, module `RouteContract` emitter and handler stubs. It is a request-access declaration, not an authorization policy. Client signatures and wire paths do not change because a server handler needs context. Do not make Native Plants into a packaged content module to obtain request access.

The first vertical slice is the typed capabilities GET. Its handler receives the actual request, applies the existing owner/reviewer policy and returns a generated-codec response with the renewal cookie and private cache headers.

Prove nested DTO decoding, field casing, session serialization and denied responses here before converting all contributions. Use a module fixture to prove the same generated binding works outside the standalone app; do not migrate Alerts' public API in this task.

### A2. Define typed contribution operations

Declare endpoint modules in `Models/Api.fs`, with actual nested request/response records. Shared private DTOs can remain in `Models/Contributions.fs`; adjust project order so the API can reference them. Use supported record/list shapes and decode them at the wire boundary. Arrays may remain an internal implementation choice with explicit conversion.

Use one typed POST record per operation instead of a loose action envelope. Fixed POST paths with a typed `PlantId` field are sufficient; this plan does not require a new `PostBy` endpoint family or general DU-codec support.

Use a versioned private JSON surface under `/api/plants/v2`:

| Method/path | Contract purpose |
| --- | --- |
| GET `/access` | Current `CanEditCatalogue` and `CanReview`. |
| GET `/personal/:plantId` | Notes, photos, hero preference, viewer token and capacities. |
| GET `/review?page=…` | Typed, paginated correction/photo review queue. |
| POST `/notes/save` | Plant ID, note ID, revision, text and correction flag. |
| POST `/notes/delete` | Plant ID, note ID and revision. |
| POST `/photos/update` | Plant ID, photo ID, revision, caption, photographer and offer flag. |
| POST `/photos/delete` | Plant ID, photo ID and revision. |
| POST `/hero` | Plant ID and personal photo choice, including reset to the site hero. |
| POST `/review/correction` | Note ID, revision, read/unread choice and current queue page. |
| POST `/review/promote` | Photo ID, revision and current queue page. |

Paths above are relative to `/api/plants/v2`. Return typed personal/review snapshots after mutations as today. Retain domain validation for lengths, nonempty text, IDs, revisions, parent publication and ownership: successful JSON decoding alone is not business validation. Preserve expected 400/401/403/404/409 behaviour and useful limit/conflict messages.

Factor existing operations away from raw JSON and `WorkerResponse` construction so both old and new HTTP adapters call one implementation. Keep SQL predicates, transactions and media side effects unchanged during that factoring. Do not rewrite the persistence layer just to use generated full-row CRUD.

### A3. Preserve guarded JSON reads and special media paths

Before generated JSON decoding, use a thin app-owned preflight for the private POST route family: authorize the request, enforce same-origin, require JSON and bound the streamed body to 24,000 bytes. Consume the input body once through the capped reader, reconstruct a request from those bounded bytes while preserving URL, method and headers, and forward it to generated routing so its generated decoder is authoritative. Avoid cloning/teeing an untrusted stream into an unread branch that can buffer it. Test the complete path through the Worker.

Keep that preflight mechanical. Domain operations and viewer-token checks remain in app handlers. Any resolved subject is request-scoped; never place mutable caller state in the shared environment or isolate. Rechecking authorization in a handler is acceptable initially if sharing it would require a larger abstraction.

Extract the existing bounded byte/text reader into a small framework HTTP utility if useful for this adapter. Its byte cap is caller-supplied, and it knows nothing about notes or roles. Do not weaken limits by checking only `Content-Length` or change all legacy POST routes in this slice. A generalized middleware/policy framework is outside scope.

Multipart upload and image/blob responses retain their dedicated handlers and URLs. The new upload helper consumes the response and returns `Result<unit, ApiError>` based on HTTP success, then refreshes through the typed personal-read endpoint. It does not interpret the legacy upload response as an unchecked personal snapshot; non-success responses use the shared error interpretation. The existing upload response remains compatible with the older client. All metadata/edit/review JSON goes through typed contracts.

### A4. Compose a session-aware transport

Add an optional browser client adapter over `Hedge.Http.Transport` and the existing `HedgeGuest.withSessionRequest` lifecycle. Expose a typed F# binding for that lifecycle rather than importing an unchecked arbitrary-response function into the app.

- Hold the session lock through response-body consumption; preserve rejection of queued/stale results during logout.
- Keep HTTP rejection and decode failure distinguishable through `Hedge.Http.ApiError`; an HTTP 403 is not a network failure.
- Attach the app's viewer-token and owner-key headers for the relevant operation only. Capture their values and request epoch at invocation; do not retarget an old draft to a new account/key while it waits.
- Use same-origin credentials and `cache: no-store` for protected requests; apply `BASE_PATH` exactly once.
- Keep public catalogue requests on their ordinary transport, without a login/bootstrap dependency.
- Do not nest the non-reentrant session lock. Image upload and protected blob reads each retain one lock boundary.

The app chooses request headers and endpoint policy. The generic transport adapter neither reads plant state nor stores an owner key globally for every request.

### A5. Compatibility and removal

Use camelCase generated JSON on v2. Preserve the current PascalCase routes as thin compatibility adapters over the same operations; an old tab must remain usable through the first deployment. Do not change the global codec casing or emit both schemas from every response.

Migrate Personal, Review and Access clients together to generated calls. Remove their untyped JSON request helper and dynamic `Action` construction. Keep media preparation/delivery helpers narrowly named and separate.

Retain the old adapter for one documented compatibility release. A later cleanup may remove it once the preview's old tabs have been refreshed/retired; keep that explicit cleanup item in this note rather than silently declaring it unnecessary. A rollback to the previous client must still work during the overlap.

## B. Make access state a supported client integration

### B1. Publish a small admin-credential client interface

Provide shared browser helpers to read, set, clear and subscribe to the saved owner credential. The common admin and Native Plants consume the same contract; preserve the existing `adminKey` storage key for compatibility.

- Applying/clearing a key emits a same-document change notification immediately. Typing in the unsubmitted key field does not.
- Normalize cross-document `storage` changes, including removal/clear, into the subscription.
- Event payloads contain no key. A stored value or event is never evidence of authorization.
- Subscriptions return a disposer. Failed storage access must not leave authenticated-looking UI or an uncaught exception; keep storage errors explicit at submission.
- Keep owner credentials separate from the OAuth session. This is not a new universal account store.

Use the normal shared client/build packaging, with thin bindings where F# and the app's admin-page JavaScript both need the helper. Avoid a new script-copy convention per app.

### B2. Use confirmed app capabilities consistently

The app's typed capabilities endpoint remains the one view of its owner/reviewer policy. Both the main app and its admin-page navigation use the same capability loader and invalidation rules; the common admin's own CRUD permissions continue to come from its authorized type discovery.

- On credential/identity change: clear relevant permissions and private review data immediately, increment the request generation and fetch fresh capabilities.
- Reject responses captured under an older credential or generation. Apply the same rule to common-admin discovery/records when the applied key changes, so old requests cannot restore stale access state.
- Direct `/review` navigation checks access before requesting/rendering a queue. An unavailable check shows retry; denial shows access required.
- Refresh on focus/visibility and retain the existing periodic server check for grant revocation. Remove the one-second localStorage watcher; event notification does not replace server revalidation.
- Centralize these rules within the app; the framework supplies credential/session signals, not `CanReview` semantics or a generic role-navigation system.

Keep permission failures observable through typed errors; avoid an automatic retry loop. Preserve the current distinction between contributor login, curator review and owner administration.

## C. Separate admin resource support from caller permission

### C1. Add an operation ceiling to the existing descriptor

Add `SupportedOps: AdminOp list` to `Hedge.Admin.AdminTable` (move the `AdminOp` declaration before the descriptor as needed). Generated descriptors initially emit the current full CRUD set, preserving other apps' behaviour. The host can narrow a descriptor when assembling its admin configuration.

Use one shared effective-operations calculation for discovery and execution:

`effective operations = descriptor-supported operations ∩ caller-permitted operations`

`AdminOwner` supplies all caller permissions but cannot exceed a descriptor's ceiling. `AdminSubject` contributes its existing permission predicate. Anonymous behaviour remains unchanged. Disallowed direct operations return the same forbidden response as current app policy; UI hiding is not the enforcement mechanism.

Do not add a schema attribute, migration feature or second permission language. The initial requirement is satisfied by an admin descriptor and explicit host configuration. Regenerate all affected descriptors and update descriptor fixtures; leave storage schemas unchanged.

### C2. Make Native Plants exposure explicit

Replace the exclusion list with an authored list of generated descriptor values:

| Resource | Supported operations |
| --- | --- |
| Plant, PlantPhoto, PlantMap, GlossaryTerm, SourceReference | Existing CRUD. |
| Grant | Existing owner-only CRUD. |
| Identity | List and read only. |
| Guest, PlantNote, PersonalPlantPhoto, PlantViewPreference, ContributionClaim | Not registered in generic admin. |

Use `AdminOwner` for a validated owner key. The descriptor now supplies Identity's immutable boundary, so the app no longer needs an artificial owner permission predicate. Curators continue to use app review operations; this plan grants them no catalogue/admin CRUD.

Adding a future storage table must not expose it automatically. Prefer explicit descriptor references so a renamed resource produces a compile-time change rather than disappearing behind a string filter. Do not apply the Native Plants exposure list to other hosts.

## Implementation order and review units

| Slice | Framework deliverable | App integration / exit evidence |
| --- | --- | --- |
| 0 | Port the 17 shared files from `53aa270` onto main's baseline. | Repository gate green; original Native Plants session implementation remains unchanged. |
| 1 | Request-aware GET binding and session-aware transport, with standalone/module fixtures. | Typed capabilities vertical slice, response/error tests and session races pass in the integration checkout. |
| 2 | Only the minimal guarded-read support established by the real consumer. | Typed contribution operations and compatible client migration; v1/v2 parity; no duplicated operation logic. |
| 3 | Admin credential notifications and stale-credential handling. | Unified capability lifecycle; same-tab/cross-tab key changes, logout independence and denied direct navigation. |
| 4 | Admin operation ceilings and regenerated descriptors. | Explicit exposure; discovery/direct CRUD agree for owner, curator and anonymous; other hosts retain behaviour. |
| 5 | Framework regeneration, documentation and complete repository checks. | App build/tests/preview checks pass on combined code; integration and compatibility notes complete. |

Keep commits buildable and separate by ownership. The framework work runs on `hedge-framework-boundaries`, while Native Plants remains the consumer and the integration work is checked against its real code. Inspect current main and the concurrent Capacitor work before each shared merge; reconcile overlap explicitly. The branch preparation itself does not merge either track into main or deploy anything.

## Verification

Use the current compiled-Worker/SQLite tests as the behavioural baseline. Add meaningful coverage for the changed boundaries, rather than reproducing every implementation branch.

1. **Contracts:** malformed successful responses fail decoding; malformed request types fail before mutation; nested DTO/list round trips; safe path/query handling; HTTP 401/403/409 versus transport/decode errors; body caps without `Content-Length`.
2. **Request binding:** opted-in GET receives the actual cookie-bearing request in standalone and module routing; existing public GET handler signatures remain compatible; private replies retain renewal and cache headers.
3. **Contribution parity:** old and new adapters produce equivalent database changes, capacities and public copies. Preserve all current ownership, claim, conflict, quota, upload cleanup and publication tests. New client + current media handlers work together; old client + new server still works.
4. **Client lifecycle:** queued writes cancel on logout; account/key changes discard late results and private images; capabilities refresh after same-tab key application and cross-tab removal; OAuth logout preserves owner access only when its independent key remains valid; revocation clears review state on refresh.
5. **Admin matrix:** exact exposed resource list; owner Identity list/read succeed and create/update/delete fail; omitted resources stay absent even for owner; catalogue/grant CRUD remains owner-only; a fixture-only new storage table does not enter Native Plants admin; existing hosts retain supported operations.
6. **Integration:** regenerate all gate-checked host/module outputs, run `./test.sh`, then Native Plants `npm run build`, `npm test` and `npm run check:preview` with the existing local archive/seed available. Keep new framework fixtures in the repository gate. Verify `schema.sql` and migrations have no semantic changes.
7. **Browser:** anonymous, verified contributor, curator and owner journeys; open-tab compatibility; admin help/navigation; key removal/change; contribution editing and review. Use existing accounts/fixtures and restore any temporary grants/content. Check a configured subpath for transport base handling.

Update `CONTRIBUTIONS.md` and `PREVIEW.md` to describe the final integration and compatibility period. Keep credentials, local seeds and media out of commits. Deployment is a later explicit implementation step. Preparing these branches ports existing shared code only; the refactor does not deploy or alter remote application data.

## Scope limits and completion

No generic notebook/media/curation module, identity re-extraction, grant-picker feature, taxonomy/cache rewrite, new role, native-mobile work or composition-manifest rename. Internal file splitting is justified only where it supports typed operations and thin adapters.

The refactor is complete when ordinary contribution JSON has a checked typed boundary; access changes use a documented notification/refresh lifecycle; admin exposure and supported operations are explicit and enforced; existing behaviour passes the checks above; and the sole retained legacy JSON adapter is documented with its retirement condition. No unchecked generic request helper or duplicated contribution policy remains in the new client/server path.
