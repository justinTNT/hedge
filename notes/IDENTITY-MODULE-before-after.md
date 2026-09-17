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
| **Identity module** (`packages/modules/identity`) | Guest/Identity schema + DDL; identity SQL + operations (anonymous/provider policy, activate, merge, fresh, disconnect); identity API/codecs; the OAuth-completion/adoption policy; client Elmish state + signals; badge/switcher UI; **default** component CSS. |
| **Framework / libraries** (Hedge) | Generic routing; the signed-cookie crypto + `Hedge.GuestSession` policy; transport; OAuth **provider-protocol** mechanisms (`Hedge.OAuth`). Kept as-is. |
| **Host / app author** | Compose one identity instance; supply environment resources + which providers exist; place the control in chrome; wire the client child + fan its signals to content modules; **compose the attribution capability from its selected content modules.** |
| **Content modules** (blog, articles) | Their own comments, `IdentityRef` author references, and the statements that count/reassign their content — exposed to identity through a narrow host-composed capability, never enumerated by identity. |
| **Deployment author** | Provider secrets (via `wrangler secret`), and identity CSS overrides through documented theme hooks. Nothing else. |

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
          Secret      = fun (e: Env) -> e.OAUTH_SECRET
          GuestCookie = fun (e: Env) req -> GuestConfig.deps e req      // the signed-cookie policy
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
usable control; and schema/generated output shows no unintended diffs. **Out of scope:** alternative
auth backends, a general plugin/lifecycle system, upload controls, and any new identity feature.

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
