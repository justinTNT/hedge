# Plan: curator access-control capability (hedge)

## Context

Alerts curation (approve/dismiss/edit-framing of pending posts) is today only doable by the single
`ADMIN_KEY` owner via the generic admin. The owner wants to delegate bounded editorial work to trusted
people **without** complicating the simple owner admin. hedge has **no role/grant concept today**
(authorization is binary: anonymous/guest vs. the one admin key). This adds a small, reusable
access-control primitive with **alerts as its first consumer** — and its boundaries also give a
plausible route to delegated editing on a future host (e.g. Grassophy) once that host has identity.

**Settled decisions**: owner grants from the admin (Q1); curator can approve/dismiss + edit framing
(Q2); dedicated curator surface, common admin untouched (Q3); small general primitive (Q4); access
control is framework auth infrastructure consumed by alerts via injection (no alerts→AC dependency);
grants keyed by the merge-stable `(provider, provider_user_id)`.

This plan incorporates review feedback (`plan-feedback-22-sept.md`), based on `main@f8dac81` /
`alerts-module@4241c58`. Those are amendments, not new settled decisions.

## Prerequisite — branch merge (step 0)

Merge `alerts-module → main` first (framework work belongs on main; idealist must stop drifting).
Last trial showed two **additive** conflicts (`apps/microblog/package.json`, `apps/microblog/wrangler.toml`)
resolved by union — but **re-run the conflict assessment against the actual branch tips at merge time**;
don't treat "two conflicts" as fixed. `./test.sh` green, then branch `access-control-curator` off `main`.

## Architecture (mirrors `GuestSession`'s file split; wired differently)

- **Capability**: new `packages/hedge/src/Hedge/AccessControl.fs` (sibling of `GuestSession.fs`) —
  `Deps`, `requireRole`, deferred `Service`. Reuses `GuestSession.requireGuest`/`readCookie`.
- **NO `WorkerConfig` field.** `GuestSession` is in `WorkerConfig` only because the *router itself*
  consumes it (OAuth, `/api/auth/me`). Access-control has **no router consumer** — it's consumed by
  alerts via host injection. Adding an unused field would force every `createWorker` to change for zero
  behavior. Opt-in = the host **constructs and injects** the service where needed. (Add a router hook
  later only if the router gains a concrete role-gated responsibility.)
- **Schema**: a `Grant` type in `apps/microblog/src/Models/Domain.fs` (the identity model) → the
  `{ identity:true }` gen slice emits the `grants` table + admin CRUD descriptor + Db helpers, exactly
  like `guests`/`identities`.
- **Host binder**: new `apps/microblog/src/Server/AccessConfig.fs` (sibling of `GuestConfig.fs`) —
  builds `AccessControl.Deps` from `env` + a per-call request, reusing `GuestConfig.deps`,
  `Server.Identity.activeFor`, and a new `hasGrant` SQL.
- **Consumer**: alerts gets an injected, **request-deferred** authorization capability; new alerts
  curator endpoints; the idealist host maps `AccessControl` onto it. Alerts imports nothing from AC.
- **Page**: an app-owned `curator.html` shell; reusable queue/editor behavior lives *with alerts*.

## Grant schema + composite uniqueness + revocation

`Grant` fields: `Id (PK)`, `Provider`, `ProviderUserId`, `Role` (`"curator"` first), `Enabled: bool`,
`GrantedAt`, `GrantedBy: string option`.

- **Composite uniqueness on `(Provider, ProviderUserId, Role)` — Option B, as TABLE metadata.** It
  describes a relationship *between* columns, so model it as a table-level attribute (not a field attr).
  It must cover **schema generation AND migrate expected-index behavior**, validate the referenced field
  names, and update `SchemaCodec` if the representation crosses the types boundary (the `/api/admin/types`
  round-trip test will catch a miss). Do **not** commit to a "~30 line" estimate — scope it properly.
  (Rejected: a synthetic concatenated key column — avoidable consistency burden.)
- **Revocation via `Enabled`**: revoke = set `Enabled=false` through ordinary admin CRUD; reactivate =
  `true`. `hasGrant` requires `Enabled=1`. This avoids soft-delete colliding with the unique index.
- `hasGrant` query: `WHERE provider=? AND provider_user_id=? AND role=? AND enabled=1` (tiny table).

## `requireRole` guard (`Hedge.AccessControl`)

`requireRole deps role cookie`: `requireGuest` → `Accepted{GuestId}` → `deps.ActiveSubject guestId`
(active identity → `{Provider;ProviderUserId}`, honoring `guests.deleted_at`, see semantics) →
`deps.HasGrant provider providerUserId role`. Returns a **three-way** result, preserved end-to-end:
`Authorized {Subject; Replacement}` | `AuthRequired {Replacement}` | `Forbidden {Replacement}`
(no accepted session → AuthRequired; accepted but no active/non-anon identity → AuthRequired; identified
but no enabled grant → Forbidden). Rejects `provider='anonymous'`. **Replacement (renewal cookie) is
carried separately from the decision** — a valid session may need renewal even when it lacks the grant.

## Authorization semantics (settle explicitly)

The check verifies a *previously-linked* account selected through a remembered guest session — it does
**not** prove the current browser just authenticated. Given that:
- **Grants apply to the ACTIVE identity**, not any linked identity.
- Switching to the anonymous identity **removes** curator access; switching back **restores** it.
- **Disconnect** removes that session's access but leaves the account's grant usable after another login.
- **Revocation takes effect on the next request** (every request does a fresh `hasGrant` lookup; no caching).
- **Guest deletion**: today cookie acceptance and `activeFor` do **not** check `guests.deleted_at`.
  Grants aren't revoked by guest deletion (they key on the provider pair) — revocation is `Enabled`.
  But for defense-in-depth the resolver **should honor `guests.deleted_at`** so a deleted guest's live
  session can't authorize. (Decision: enforce it.)

## Alerts consumption (injected, request-deferred, cron-safe)

- `Alerts.Services` gains an authorization field that **takes the request at call time** — a
  module-local DU so the three outcomes survive (alerts names no AC type):
  `type CuratorAuth = Allowed of {| SetCookie: string option |} | AuthRequired of {| SetCookie: string option |} | Forbidden of {| SetCookie: string option |}`
  and `AuthorizeCurator: WorkerRequest -> JS.Promise<CuratorAuth>`.
  **Critical**: `alertsServices` serves both HTTP *and cron*, so its constructor must stay
  request-free and guest-config-free — the request is supplied when `AuthorizeCurator request` is
  called. (The earlier draft threading `request` into the constructor would break the scheduled path.)
- New `packages/modules/alerts/src/Models/Api.fs`: `GetQueue` / `ApproveForPublication` / `Dismiss` /
  `EditFraming` (module-relative; gen applies `/api/alerts`). Regenerate the module surface.
- New `packages/modules/alerts/src/Server/Curator.fs`: each handler calls `AuthorizeCurator request`
  first and maps `AuthRequired → 401`, `Forbidden → 403`, `Allowed → act` (attaching `SetCookie` if
  present). `Composition.bind` wires the four (was the no-op).

## Curation state machine (define before the endpoints)

Curator actions are gated by state; **approved entries are frozen** in v1:

| State | Curator actions |
| --- | --- |
| Undecided (`approved=0 AND rejected=0`) | Edit framing, **Approve for publication**, Dismiss |
| Approved, awaiting publication | View status only — framing frozen |
| Published or dismissed | No further curator mutations |

- Rename "promote" → **"Approve for publication"** (the hourly cron publishes; it selects ≤20/run, so
  "within an hour" isn't guaranteed — don't claim it).
- **Enforce state in the mutation SQL and check affected-row counts**:
  - `approvePendingPost` = `UPDATE … SET approved=1 WHERE id=? AND approved=0 AND rejected=0`
  - `rejectPendingPost`  = `UPDATE … SET rejected=1 WHERE id=? AND approved=0 AND rejected=0`
  - `updateFraming`      = `UPDATE … SET title=?,snippet=?,owner_comment=? WHERE id=? AND approved=0 AND rejected=0`
  - If `changes == 0`, another curator already acted → return a **409 conflict** (define this response;
    a stale page must not silently succeed).
- **Why freeze**: the cron reads an approved row, fetches its og:image, then publishes from *that
  snapshot*. D1's batch transaction covers only the batch statements — **not** the earlier read + network
  fetch. Leaving approved rows editable/dismissible would let stale framing, or a dismissed-then-published
  entry, slip through. If editing/withdrawing approved entries is ever required, add an **atomic
  eligibility+version check to the publication path** (a separate SELECT is insufficient); freezing is
  the smaller, safe v1.

## Owner grants a curator (Q1) — common admin untouched

Owner CRUDs `grants` via the existing admin (`/api/admin/Grant`, ADMIN_KEY-gated, no admin change).
To find `(provider, provider_user_id)`: the idealist admin already lists the `identities` table
(name/email/provider/provider_user_id) — owner reads it there and creates a Grant with `role="curator"`,
`enabled=1`. (Nice-to-have: regenerate the identity descriptor read-only — a descriptor tweak, not an
`Admin.fs` change.)

## Curator page (app-owned shell, alerts-owned behavior)

- **Ownership split** (same principle as comments/species): reusable **queue/editor behavior + styles
  live with the alerts module**; the **app** owns the HTML entry, identity controls, branding, and
  navigation. Reuse the existing identity subsystem + `packages/rich-text` — do not reimplement either.
- `apps/microblog/curator.html` (like `admin.html`) + a thin app client wiring alerts' curation UI;
  register `curator` in `vite.config.js` inputs; served as a **static asset** (so `/st/curator` →
  `_site/st/curator.html` directly, like `/st/admin`); API calls via the base-path-aware client fetch.
- **OAuth return flow (must be explicit)**: the existing claim handler returns through the **blog's
  client-side router** and opens its identity selector — navigating that router to `/curator` does NOT
  load `curator.html` (separate document). Define the handoff: after identity selection/activation,
  perform a **document navigation** to the curator page. **Test the whole `/st` journey** — login →
  callback → claim → activation → return — not just prefixed API calls.
- States via `/api/auth/me` + endpoint results: not logged in → provider login (`returnTo` = curator);
  logged in, no grant → queue returns **403** → "signed in as <name>, no curator access"; curator →
  queue renders with Approve / Dismiss / Edit-framing (rich-text for owner_comment).

## Build sequence

0. Merge `alerts-module → main` (re-check conflicts), `./test.sh`; branch `access-control-curator`.
1. Framework: `AccessControl.fs` + `Hedge.fsproj` (no WorkerConfig field).
2. Schema: `Grant` (+ Enabled) in `Models.Domain`; the composite-unique table-metadata Gen change
   (schema gen + migrate index set + SchemaCodec); `npm run gen` and `HEDGE_SITE=idealist npm run gen`.
3. Host: `AccessConfig.fs` (request-deferred service) + `Server.Identity.hasGrant`/`Sql.fs` + `Server.fsproj`.
4. Alerts: `Api.fs` + regen surface + `Curator.fs` + state-guarded `Sql.fs` + `Composition.fs` +
   `Services.fs` (`CuratorAuth` DU, request-deferred field) + `alerts.server.props`.
5. Host injection: `ModuleServices.idealist.fs` — `alertsServices env` stays request-free;
   `AuthorizeCurator = fun request -> map (AccessConfig.service env request).Authorize "curator" request`.
6. Page: `curator.html` + app client + alerts-owned curation UI + `vite.config.js`; wire the OAuth
   document-navigation return.
7. Regenerate all three gen passes; `./test.sh`; deploy + migrate (see rollout).

## Verification

- **Gate `./test.sh`**: regenerate + commit every diff-checked artifact (module surface, idealist glue
  `Routes.idealist`/`AdminGen.idealist`/`schema.idealist.sql`, default `schema.sql`/`AdminGen.fs`).
  `check-sql.sh` EXPLAIN-prepares the new SQL and asserts fresh==migrated schema (why the composite
  unique must be generated, not migration-only). Add the `SchemaCodec` case for the new table attribute
  or the `/api/admin/types` test fails.
- **E2E (local wrangler dev, idealist)**: owner grants a curator; curator logs in, opens `/st/curator`,
  approves (→ `approved=1`; next cron publishes to blog_items + writes alerts_promotions), dismisses,
  edits framing on an undecided row. **State machine**: framing/dismiss on an already-approved row →
  409; a second curator racing the same row → 409. **Denials**: no grant → 403; logged out → 401;
  anonymous-active → 401. **Semantics**: switch to anonymous → access lost; switch back → restored;
  disconnect → session loses access, grant persists after re-login; set `Enabled=0` → next request
  denied. **Guest deletion**: deleted guest's session can't authorize. **Merge survival**: trigger an
  identity merge → curator access persists (keyed on the provider pair). **Admin unchanged**: `/st/admin`
  still ADMIN_KEY-only; curator can't reach `/api/admin/*`. **Full `/st` OAuth journey** works end-to-end.

## Rollout (corrected — generate ≠ apply)

`npm run migrate:remote` **generates** a migration; it does **not** apply it (the generator prints a
separate apply command). Sequence:
1. Merge alerts → current `main`, resolve + verify, branch.
2. Generate and **review** the additive migration for idealist.
3. **Apply explicitly**: `npx wrangler d1 migrations apply idealist-db --remote --env idealist`.
4. Deploy the code that queries `grants` (`npm run deploy:idealist`), then verify live authorization + curation.

With `Grant` in the shared identity model, `grants` lands in every microblog tenant's schema (harmless
empty table, like identities) — each needs the same apply step. The obligation is real; sequence them,
or use a dedicated idealist-only gen slice (more plumbing, deferred).

## Risks / gotchas

- Cron-safety of `alertsServices` construction (request-deferred authorization) — the key correctness point.
- Approved-entry freeze + affected-row/409 checks vs. the cron's read-then-fetch-then-publish snapshot race.
- Composite-unique as table metadata across gen + migrate + SchemaCodec — scope it, don't estimate blind.
- Curator page OAuth return needs a real document navigation, not SPA-router navigation; test the full `/st` journey.
- `guests.deleted_at` not currently honored by acceptance/`activeFor` — enforce in the new resolver.
- Regen discipline: forgetting to regenerate+commit a gate-checked generated file is the top failure mode.
- Scope: substantial multi-layer feature (framework + schema + module endpoints + app page + merge), not a quick tweak.
