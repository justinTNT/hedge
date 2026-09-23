# Phase 1 — identity extraction: execution blueprint

Working doc for `notes/IDENTITY-ACCESS-CONTROL-overarching-plan.md` Phase 1. Branch:
`identity-access-admin` (off `main` @ `abd69bb`). Derived from two code-surface maps (2026-09-23).

## The mechanism (verified — no generator change needed)

The generator's **identity slice** is the un-owned, unprefixed root module: manifest entry
`{"identity": true}` (`Gen/Program.fs:1953-1954`), reflected from an assembly via `Assembly.Load`
(`:1972`). It already accepts **`"assembly"` / `"namespace"` overrides**, so the slice can point at a
SHARED assembly while staying `Owned=false` — meaning its Db/AdminGen/schema stay **app-local-combined**
and tables stay unprefixed (`guests`/`identities`/`grants`). Every `IdentityRef` FK resolves by the
domain SHORT NAME `Identity` → table `identities` (`:110-113`, `:749-756`), so as long as the shared
module keeps the type names `Guest`/`Identity`/`Grant` and unprefixed tables, all
`blog_comments`/`articles_comments` FKs resolve unchanged.

**Grants stay opt-in** by making Grant a SEPARATE shared assembly and adding a SECOND identity-slice
entry only in the manifests that want it (microblog/idealist), e.g. `{"identity": true,
"assembly": "IdentityGrantModels", ...}`. Articles omits it → no `grants` table. No generator change.

## Extract vs keep (from the surface maps)

**Extract into `packages/modules/identity` (currently duplicated microblog+articles, byte-identical):**
- Models: `Guest`, `Identity` (shared assembly); `Grant` (separate opt-in assembly). microblog is the superset.
- Server logic: `Server.Identity` fns 1-8 (`activeFor`…`hasLinkedIdentity`) + the identity `Sql.*` block;
  the identity handlers (`resolveIdentity`, `onOAuthComplete`, `switchIdentity`/`activate`/`revert`,
  `disconnectIdentity`, `getIdentities`, `mergeDuplicateIdentities`, `identityJson`, `cacheAvatar`);
  the identical `GuestConfig.fs` → a parameterized builder; the `/api/auth/{activate,revert,disconnect,
  identities}` route prelude + `OAuthConfig` binding from `Worker.fs`.
- Client: the claim/return glue (`parseClaimFromRoute`/`ClaimRoute`/`Signal` fan-out) duplicated in
  `Blog/Host.fs` + `Shell/App.fs`. (The `Content.Identity`/`IdentityView`/`Client.GuestSession` cores
  are already shared — leave them.)

**Keep host-supplied / injected (coupling seams):**
- `AttributionPolicy.fs` (`reassignStatements`, `commentTables`) + the `Attribution.fs` builders it feeds
  (consumed at Handlers `mergeDuplicateIdentities`/`onOAuthComplete`/`switchIdentity`). Inject as Deps.
- The `/curator` special-case → make onOAuthComplete's auto-activate a **host-supplied returnTo predicate**
  (microblog only); `Curator/App.fs` document-nav + `AccessConfig`/`activeSubject`/`hasGrant` stay microblog.
- Access-control (Grant/`grantEnabled`/`guestNotDeleted`/`AccessConfig`) = the optional grants sub-surface.
- Per-app: `Env`, OAuth provider set + secrets, `Mounts`/`BlobServing`/`Scheduled`, darwin `getRhymes`.

**Invariants to preserve:** cookie name `hedge_guest` + signing/audience/lifetimes; `HardCutover`
migration; D1 batch boundaries (bound-not-run `ensure*Stmt`; single-batch `Attribution.reassign`);
`IdentityRef`→`identities` FK; activation/switch/adopt/merge/disconnect/deletion semantics; grant key
`(provider,provider_user_id,role)` surviving merges; `/api/blobs/guest`.

## Ordered slices (each separately reviewable; apps stay working between)

1. **Shared Models, zero-output-change.** Create `packages/modules/identity` Models assembly(ies) with
   Guest+Identity (and a separate Grant assembly). Reference them from each app's Gen project; repoint
   `gen-modules*.json` identity slice(s) via `assembly`/`namespace`. Regenerate ALL compositions;
   **assert byte-identical `schema*.sql`/`Db.fs`/`AdminGen*` (gate green)** — proves the move is
   schema-neutral before any logic moves. Grant slice added only to microblog/idealist manifests.
2. **Shared server logic.** Move `Server.Identity` fns 1-8 + identity `Sql` + the identity handlers +
   `GuestConfig` into the module as parameterized code (attribution injected via a Deps record; the
   `/api/auth/*` POST prelude + `OAuthConfig` binding become a module-provided "mount identity" helper).
   Migrate microblog first, then articles (Justat/NDCT). articles gains nothing it lacks (no Grant).
3. **Client claim/return glue + return/activation policy.** Consolidate the duplicated claim handling;
   replace the literal `/curator` suffix with an explicit host-supplied return/activation policy.
4. **Grants optional support + Slice G key rotation.** Package Grant persistence/binding as the opt-in
   sub-surface; carry forward graceful key rotation (GUEST_KEYRING) as a separately-reviewable part.

## Verification each slice
`./test.sh` (gen stability, module surfaces, per-site glue, check-sql fresh==migrated, SchemaCodec,
HostProbe, client matrix) + schema equivalence for microblog/idealist/justat/ndct + browser flows at
root and `/st` (owner login, delegated login/return, no-grant/revoked, switching, uploads). Rollout:
package relocation ≠ schema change; if a composition newly gains a table, generate+review+apply its
migration before code that queries it.
