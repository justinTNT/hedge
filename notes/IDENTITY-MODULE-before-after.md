# Identity module — before/after consuming-host example

Status: **design artifact, no code changes.** First deliverable of the identity-module extraction
(step 2 of [IDENTITY-MODULE-investigation.md](IDENTITY-MODULE-investigation.md): "write the consuming
host example first"). Written 2026-09-17, grounded in a full inventory of the current identity
surface in Microblog and Articles. It exists to answer one question before we commit to the churn:
**does making identity a composed module actually make a new host easier to write, and what exactly
changes?** Consumes the session boundary the signed-guest-cookie work already established
([guest-comment-image-uploads] / [GUEST-COOKIES]); does not redesign credentials.

## Target ownership (the division to hold to)

| Owner | Owns |
| --- | --- |
| **Identity module** (`packages/modules/identity`) | Guest/Identity schema + DDL; identity SQL + operations (anonymous/provider policy, activate, merge, fresh, disconnect); identity API/codecs; the OAuth-completion/adoption policy; client Elmish state + signals; badge/switcher UI; **default** component CSS; **the guest-signing key configuration — reading the host's keyring env and wiring `Active`+`Previous` into the signed-cookie policy (Slice G).** |
| **Framework / libraries** (Hedge) | Generic routing; the signed-cookie crypto + `Hedge.GuestSession` policy; transport; OAuth **provider-protocol** mechanisms (`Hedge.OAuth`). **Plus, for Slice G: the keyring smart constructor + migrate-on-use rotation in the session policy** (see §Slice G) — generic mechanisms, so they stay framework-level. |
| **Host / app author** | Compose one identity instance; supply environment resources + which providers exist; place the control in chrome; wire the client child + fan its signals to content modules; **compose the attribution capability from its selected content modules.** |
| **Content modules** (blog, articles) | Their own comments, `IdentityRef` author references, and the statements that count/reassign their content — exposed to identity through a narrow host-composed capability, never enumerated by identity. |
| **Deployment author** | Provider secrets (via `wrangler secret`), **the signing keyring secret (`GUEST_KEYRING`, or `GUEST_SECRET` for a single key), and rotation ops** (add a key, retire the old one), and identity CSS overrides through documented theme hooks. Nothing else. |

App-author vs deployment-author line: the **app author** picks the module + providers and writes the
composition once; the **deployment author** only sets secrets and colours/fonts.

## BEFORE — what a host carries for identity today

Two buckets, from the inventory. **Bucket A is genuine duplication** (byte-identical across
apps, modulo comments) — it should become module-owned. **Bucket B is intentional per-composition
policy** — it can't just move; it must become a seam.

**A. Duplicated storage/plumbing (move to the module):**
- `src/Models/Domain.fs` — `Guest` + `Identity` types (identical bar the header comment).
- `schema.sql` — `guests`/`identities` DDL + 3 indexes (byte-identical).
- `src/Server/generated/Db.fs` — `GuestRow`/`IdentityRow` + CRUD + `Tables` (byte-identical; it *is* the identity slice).
- `src/Server/generated/AdminGen.fs` — `guest`/`identity` `AdminTable` descriptors (identical; only the composed `tables` registry line differs).
- `src/Server/Identity.fs` — `activeFor/listFor/belongsToGuest/setActive/ensureGuestStmt/ensureAnonymousStmt/legacyEligible/hasLinkedIdentity` (byte-identical).
- `src/Server/Sql.fs` — the 17 identity-only statements (`activeIdentityForGuest`, `ensureGuest`, `insertProviderIdentity`, `identityBelongsToGuest`, `legacyGuestEligible`, … — byte-identical; articles' header even says "ported verbatim from microblog").
- `src/Server/GuestConfig.fs`, `src/Server/AdminConfig.fs` (byte-identical); `src/Server/Env.fs` identity fields (`OAUTH_SECRET`, `GUEST_SECRET`, `GUEST_MIGRATION_START`, `GUEST_BRIDGE_UNTIL`, `GOOGLE_*`, `GITHUB_*` — identical).
- Client: `content-client/Identity.fs` + `IdentityView.fs` + `identity.css` — **already single-copy** (both apps file-link them); the module would adopt them.

**B. Intentional policy (becomes a seam, not a move):**
- **Attribution is forked.** Articles is policy-driven cross-module — `Server/AttributionPolicy.fs`
  (`commentTables = [Articles.Db.Tables.comment; Blog.Db.Tables.itemComment]`, `reassignStatements =
  [Articles.Sql.reassignComments; Blog.Sql.reassignComments]`), a `.ndct.fs` variant for the
  articles-only build, and dynamic builders `Attribution.countCommentsSql`/`findByProviderGlobalSql`.
  Microblog has **no** AttributionPolicy — it hardcodes single-table `blog_comments` literals in
  `Server/Sql.fs` (`findIdentityByProviderGlobal`, `countCommentsForIdentity`, `reassignComments`).
- **The app `Server/Handlers.fs` identity functions therefore differ** (not byte-identical):
  `onOAuthComplete`, `switchIdentity`, `disconnectIdentity`, `getIdentities`,
  `mergeDuplicateIdentities` each call the app's attribution flavour. (Microblog's Handlers also
  carries unrelated darwin.news `getRhymes` code.)
- **Auth routes are hand-wired** per host (`Server.Worker.authRoutes`: `POST /api/auth/activate|revert|disconnect`, `GET /api/auth/identities`); `/api/auth/me|providers|:id/login|:id/callback` are framework-served from the host's `OAuth` config block; `/auth/claim` is client-consumed (no server route).
- **Client host-glue is duplicated-but-divergent** across three host files (`microblog/.../Blog/Host.fs`, `articles/.../Shell/App.fs`, `articles/.../Articles/Host.fs`): claim-param parsing, `Identity.init/update/view`, `Signal`→child fan-out. The Justat **shell** fans the session to *two* children behind an activation/`LeaveCompleted` barrier; the two single-module hosts fan to one.

## AFTER — the consuming host

**Manifest** — identity stops being the magic root slice and becomes a composed module:
```jsonc
// gen-modules.json      BEFORE: { "identity": true }
[ { "module": "../../packages/modules/identity" },
  { "module": "../../packages/modules/blog" } ]
```
The identity module ships its own `Models`, `generated/{Db,Codecs,AdminGen,ClientGen}`, schema slice,
and `.props` — exactly like blog/articles do now.

**Server (host `Worker.fs`)** — construct one instance, supplying resources + the attribution
capability composed from the host's content modules:
```fsharp
let private identity =
    Identity.compose
        { Db          = fun (e: Env) -> e.DB
          Secret      = fun (e: Env) -> e.OAUTH_SECRET                   // OAuth state signing
          // Slice G: the host hands over its signing KEYRING; the module parses it (Active+Previous)
          // and wires the signed-cookie policy. `GUEST_KEYRING` JSON, or `GUEST_SECRET` = one key.
          Keyring     = fun (e: Env) -> e.GUEST_KEYRING
          Providers   = fun (e: Env) ->                                  // which providers exist here
              [ Identity.google e.GOOGLE_CLIENT_ID e.GOOGLE_CLIENT_SECRET
                Identity.github e.GITHUB_CLIENT_ID e.GITHUB_CLIENT_SECRET ]
          // host composes attribution from ITS content modules — preserves the cross-module vs
          // single-table difference; identity never names a content table itself:
          Attribution = { CommentTables      = [ Articles.Db.Tables.comment; Blog.Db.Tables.itemComment ]
                          ReassignStatements = [ Articles.Sql.reassignComments; Blog.Sql.reassignComments ] } }

let exports = createWorker {
    Routes       = fun req env ctx -> identity.authRoutes req (env :?> Env) |> orElse (moduleDispatch …)
    OAuth        = Some identity.oauthConfig      // ResolveIdentity + OnOAuthComplete now module-owned
    GuestSession = Some identity.guestDeps
    Admin        = Some (adminWith identity.adminTables)
    Mounts = []; BlobServing = … }
```
Microblog's instance is the same call with a one-element `Attribution` list — which **removes** its
bespoke single-table literals and the whole fork. NDCT supplies the articles-only lists (today's
`.ndct` policy) — still host-selected, now through one seam.

**Client (host)** — unchanged shape, but the child + view come from the module:
```fsharp
let idModel, idCmd = Identity.init claimFocus            // model/update/view from the module
// update: Identity.update msg model.Identity  ->  fan Signal (SessionChanged/ReloadContent/Failed)
//         to the host's content child(ren)  [host-owned: the fan-out is composition-specific]
// view:   Identity.view model.Identity dispatch  placed in the host's header/chrome
```

**Deployment author** — `wrangler secret put GOOGLE_CLIENT_SECRET …`, and CSS overrides via the
documented hooks; an empty deployment stylesheet still renders a usable control.

## What disappears from Microblog & Articles

Per host, these stop being host files (module-owned after extraction): `Models/Domain.fs` identity
types · the `guests`/`identities` DDL in `schema.sql` · `generated/Db.fs` · the `guest`/`identity`
descriptors in `generated/AdminGen.fs` · `Server/Identity.fs` · the 17 identity statements in
`Server/Sql.fs` · `Server/GuestConfig.fs` · the identity fields in `Server/Env.fs` ·
`Server/AdminConfig.fs` (becomes a one-liner including `identity.adminTables`) · the identity
functions in `Server/Handlers.fs` (`resolveIdentity`, `onOAuthComplete`, `switchIdentity`,
`disconnectIdentity`, `activate/revertIdentity`, `getIdentities`, `mergeDuplicateIdentities`) · the
hand-wired auth routes in `Server/Worker.fs`. Microblog additionally sheds its single-table
attribution literals; Articles' `AttributionPolicy.fs(.ndct)` shrinks to the host-supplied
`Attribution` value above.

**What the host still writes:** the manifest entry, the `Identity.compose { … }` call (resources +
providers + the attribution lists from its content modules), the client `init/update/view` +
signal fan-out, and the UI placement. That's the whole integration surface.

## What does NOT move cleanly (the real work — "more than moving files")

1. **Gen needs an owned-identity path (the structural blocker).** Today `{ "identity": true }` is
   modelled as the app's **non-owned root `Models` slice** (`Program.fs`: `rootModule.Owned=false`;
   `runSite` emits the identity `Db/Codecs/AdminGen/ClientGen` for the *site*, while owned modules
   emit their own surface via `emitModuleSurface`). Making identity a real module means it becomes an
   `Owned` module with its own `module.json`/`Namespace`/`generated/` — a new generator path, not a
   config tweak. `schema.sql` + `Routes` stay combined (they already span modules), and `IdentityRef`
   already decouples content from the identity type (`Program.fs` maps it to a bare FK), so this is
   scoped, but it is the biggest change and must show **no** schema/FK/route/admin diffs.
2. **Attribution becomes a host-supplied capability (this resolves the fork, doesn't block it).** The
   module owns the *builders* (`countCommentsSql`, `findByProviderGlobalSql`, `reassign`); the host
   passes `{ CommentTables; ReassignStatements }`. Constraint from the investigation: the module must
   run reassignment in the **same D1 batch shape** as today (one batch over the statement list), not
   one callback per module — verify batch boundaries are byte-for-byte preserved, and cover the
   zero-participant case (identity composed with no attributed content).
3. **Left in place on purpose (flag, don't absorb):**
   - **`.avatar` base CSS is host/content, not identity.** `identity.css` only *sizes* `.avatar` in
     composite selectors; the base shape lives in host `styles/base.css` and comment avatars reuse it
     (`comments.css .comments .avatar`). Keep the base rule in shared content CSS — the identity
     module must not absorb comment presentation.
   - **Per-host client glue stays host-owned.** The shared `Content.Identity`/`IdentityView` already
     exist; extraction does not de-duplicate the init/update/view/claim/fan-out glue, which is
     genuinely composition-specific (shell → two children). A thin helper for the single-module case
     is a *separate* optional de-dup, not part of identity's core.
   - **Scaffold opt-in.** Today a new app ships `identity.css` + `guest-session.js` **inert** (no
     schema/routes/F#/wiring). Post-extraction, a new host opts in by adding the module to its
     manifest + the `compose` call; the scaffold can offer an `--identity` flag instead of shipping
     dead artifacts.

## Slice G — graceful key rotation, folded into the module

Rotating `GUEST_SECRET` in place today invalidates every cookie at once (safe, but a full guest
reset — fine for compromise, costly for a routine roll). The cookie envelope (`Hedge.GuestCookie`)
already supports multiple live keys (`Config.Active` + `Config.Previous` with retirement epochs,
selected by `keyFor`); Slice G finishes it and lands **with** the identity module so the module ships
graceful rotation from day one. Two parts:

**G1 — Framework (Hedge, generic mechanisms): migrate-on-use + a keyring constructor.**
- `verify` must report WHICH key verified a token. Today `Verification.Signed of Claims` discards the
  key id (the envelope's `keyId` segment is known at verify but dropped). Change to
  `Signed of Claims * keyId: string` — the only shape change; match sites are
  `GuestSession.requireGuest`/`resolveOrBootstrap` + the fixtures.
- `requireGuest`/`resolveOrBootstrap` re-sign with the active key when `keyId <> Config.Active.KeyId`
  (a retiring key), **in addition to** the existing `needsRenewal` (expiry) rule. That is
  migrate-on-use: any visit re-signs a still-valid old-key cookie onto the active key. It is what
  actually preserves active guests during a rotation window — the 30-day renewal does **not** (it
  fires only < 30 days before a 365-day expiry, per reviewer B's finding 3). The replacement rides the
  existing `Authorized.Replacement`/bootstrap cookie path, so no handler changes.
- `Hedge.GuestSession.configFromKeyring : keyringJson -> audience -> Config` — a smart constructor
  parsing `{"active":"k2","keys":{"k2":{"secret":…},"k1":{"secret":…,"retireAt":<epoch>}}}` into
  `Active` + `Previous`. **Back-compat:** a plain (non-JSON) value = a ring of one active key `k1`, no
  previous — so today's `GUEST_SECRET` deployments keep working untouched. Fails closed on a
  malformed/empty ring or a sub-32-byte active secret (reusing `configFor`'s validation).
- Fixtures (extend slice A/B): old-key token → verify reports its key → requireGuest re-signs onto the
  active key (Replacement Some, new token verifies as active); active-key token not near expiry → no
  re-sign; a `Previous` key past `retireAt` → `Invalid`; keyring parse/round-trip + back-compat.

G1 is small, contained, and has **no module dependency** — it could land earlier to de-risk a
rotation before the module. Bundled here per the decision to ship it with the module.

**G2 — Ownership (identity module): the keyring is the module's host-config surface.**
- Per-app `Server.GuestConfig` (today `configFor "k1" env.GUEST_SECRET host []`) becomes the identity
  module's guest-signing config: the module reads the host-supplied `Keyring` value (the
  `Identity.compose { Keyring = … }` field above) and calls `configFromKeyring keyring host` to build
  `Deps`. The host supplies only the env value; the deployment author supplies the secret and runs
  rotations.
- The migration params (`GUEST_MIGRATION_START`/`GUEST_BRIDGE_UNTIL`, today in `GuestConfig`) move
  with it as module config.

**Rotation runbook (deployment author, once G ships):** add a new active key to `GUEST_KEYRING`
(`active:"k2"`, keep `k1` under `keys` with `retireAt = now + window`), deploy; drop the retired `k1`
after the window. Migrate-on-use moves any guest who visits within the window; only never-visiting
guests reset. `retireAt = now` reproduces an immediate hard reset (compromise).

**Sequencing:** land G1 in extraction step 4 (server behaviour, when the module takes over the session
config) so the module owns G2 from the start; G1's fixtures run in the Hedge fixture set. If a
production rotation is needed before the module lands, pull G1 forward standalone (Hedge-only).

## Compatibility preserved (non-negotiable during extraction)

Same `hedge_guest` signed cookie + `Hedge.GuestSession` policy; same `guests`/`identities` tables,
data, indexes, and `IdentityRef` joins; same `/api/auth/*` + OAuth login/callback + `/auth/claim`
behaviour; identity remains active for the **document's lifetime** (no content enter/leave
semantics); attribution keeps its **current batch boundaries**. Verified by schema/generated-output
diffs and the existing fixture set (anonymous creation, provider reuse, activate, merge/fresh,
disconnect, duplicate resolution) run against exactly the composed participants.

## Completion criterion & scope

**Done when:** all three host shapes (Microblog, Justat shell, NDCT) consume **one** identity
implementation with **no copied identity behaviour**, through the small documented surface above
(manifest entry + `compose` + client wiring + placement); an empty deployment stylesheet renders a
usable control; schema/generated output shows no unintended diffs; **and (Slice G) a keyring rotation
migrates a visiting guest onto the new key with no reset — covered by the G1 fixtures plus one live
rotation check.** **Out of scope:** alternative auth backends, a general plugin/lifecycle system,
upload controls, and any new identity feature (Slice G is rotation of the *existing* credential, not a
new feature).

## Does this make Hedge easier to use? (the verdict this example tests)

**Yes, materially — with eyes open.** A new host stops owning ~10 duplicated storage/plumbing files
and the duplicated identity handlers, and gets identity by adding one manifest line + one `compose`
call + placement. The attribution fork actually *collapses* into one seam (Microblog loses its
bespoke literals). The honest cost: it is **not** a file move — it needs (1) a new Gen owned-module
path for identity, (2) the attribution capability with preserved batch boundaries, and (3) explicit
decisions to leave `.avatar` base CSS and per-host client glue where they are. Recommendation:
**proceed**, sequenced as the investigation's steps 3→4 (CSS/client package first behind adapters,
then schema/server behind the attribution capability, Microblog → Justat → NDCT), with this
before/after as the target surface. The client-glue de-dup and scaffold `--identity` flag are
worthwhile **follow-ons**, not blockers.
