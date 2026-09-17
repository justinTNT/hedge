# Signed guest cookies — per-deployment rollout worksheet

Status: **implementation complete, UNDEPLOYED, no secrets provisioned.** This is the handback
worksheet for the separate rollout task (deployment, secret provisioning, and migration application
are explicitly out of scope of the implementation — see
[work order §"Execution and completion"](SIGNED-GUEST-COOKIES-work-order.md)). Nothing here has been
deployed. Written 2026-09-17.

Implementation commits (branch state): slices A–F — `feat(guest-cookie)` A (envelope), B (policy),
the Hedge-layer relocation, C (server consumers), D (upload path), E (browser readiness), F (this
worksheet + config-driven migration mode). `./test.sh` green throughout.

## What ships in the code

- One shared signed credential `hedge_guest = v1.<keyId>.<payload>.<mac>` (HMAC-SHA256, WebCrypto
  verify, purpose-separated, audience = request host, 365-day lifetime, renew < 30 days left).
- One policy (`Hedge.GuestSession`) behind every reader/writer: `/api/auth/me`, OAuth
  start/callback/adoption, identity list/activate/revert/disconnect, both comment handlers, and the
  guest upload. No route accepts an arbitrary client-supplied guest id; no fallback from an invalid
  signed token to legacy parsing.
- Credential-free upload keys `comment/<randomObjectId>/<file>`; the owning guest is kept only in
  **private** R2 custom metadata. **Old image URLs keep serving unchanged.**
- Fail-closed: a guest-enabled deployment with no/short `GUEST_SECRET` returns errors on guest
  operations (content reads still work); apps with `GuestSession = None` need no secret.

## Configuration surface (per deployment)

| Binding | Required? | Meaning |
| --- | --- | --- |
| `GUEST_SECRET` | **Yes**, for guest-enabled apps | Signing secret, **≥ 32 random bytes**. No fallback to `OAUTH_SECRET`/`ADMIN_KEY`/empty/dev constant. Provision as a Cloudflare **secret**. |
| `GUEST_MIGRATION_START` | Only to enable a bridge | Absolute epoch seconds; legacy guests created before this may be eligible. |
| `GUEST_BRIDGE_UNTIL` | Only to enable a bridge | Absolute epoch seconds; the bridge is active only while `now < this` **and** `GUEST_MIGRATION_START` is set. Absent/expired → **hard cutover**. |

`keyId` is currently the fixed literal `"k1"` and `Previous = []` in `Server.GuestConfig.deps`.
Audience is the request host automatically. `Secure` is set except when `ENVIRONMENT = "development"`.

> **Rotation caveat (see Deferred → Slice G).** The *envelope* supports graceful key rotation
> (`Config.Previous` verifies old tokens against retired keys until their retirement epoch while the
> active key signs new ones), but `Server.GuestConfig` does **not** yet source keyId/previous keys
> from config. So **changing `GUEST_SECRET` in place today invalidates every existing cookie at
> once**: an old-secret `v1.k1.…` token fails the MAC → `Invalid` → the guest is silently
> re-bootstrapped to a fresh guest (a write in flight 401s, then the client's `ensureSession`
> re-bootstraps and retries). It fails safe (never accepted/forged), comments/identities are intact,
> and OAuth-linked identities recover via re-login — i.e. an estate-wide hard cutover at rotation
> time. That is the desired behaviour for **key compromise**, but costly for a routine roll. Do
> graceful rotations only after Slice G wires it.

Provision the secret (the operator does this — Claude cannot set secrets):

```
# ≥ 32 random bytes, per guest-enabled deployment:
openssl rand -base64 48 | wrangler secret put GUEST_SECRET   # (repeat with --env <name> per tenant)
```

## Exposure assessment → migration mode

**Every guest-enabled deployment on this estate is treated as EXPOSED, so all use a hard cutover.**
Reason: the *previous* guest upload key scheme was `comment/<guestId>/…`, published in image URLs, so
any tenant that ever accepted a guest comment-image upload has public guest ids. Work-order rule 4:
an exposed credential is never re-signed merely because its row exists → hard cutover. (The new key
scheme is credential-free, so future uploads do not expose ids, but past uploads already did.) No
`GUEST_MIGRATION_START`/`GUEST_BRIDGE_UNTIL` is set anywhere; the code default (hard cutover) is
correct with no extra config.

The bridge mode is fully built + fixture-tested and available for any **future** deployment whose
legacy ids provably stayed private; there is none here.

| Deployment | Host (audience) | Guest-enabled | Exposure | Mode | `GUEST_SECRET` | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| articles / justat | justat.at | yes | exposed (upload keys) | hard cutover | to provision | articles + blog composed |
| articles / ndct | (ndct host) | yes | exposed | hard cutover | to provision | articles-only |
| microblog / darwin.news | darwin.news | yes | exposed | hard cutover | to provision | |
| microblog / usba.se | usba.se | yes | exposed | hard cutover | to provision | |
| microblog / mtmuse | (mtmuse host) | yes | exposed | hard cutover | to provision | |
| microblog / wtfail | (wtfail host) | yes | exposed | hard cutover | to provision | |
| microblog / ntaidc | (ntaidc host) | yes | exposed | hard cutover | to provision | |
| microblog / nonukes | (nonukes host) | yes | exposed | hard cutover | to provision | |
| microblog / idealist | id-ea.li/st | yes | exposed | hard cutover | to provision | **HELD for C6 — do not deploy** (`/st` sub-path) |
| music, archive, basewatch | — | **no** (`GuestSession = None`) | n/a | n/a | none needed | no guest identity/comments |

(Fill in the parenthesised hosts from each tenant's `wrangler.toml` at rollout; audience binds to the
runtime host automatically, so the table value is documentation, not config.)

## Consequences of the hard cutover (state in rollout notes)

- Existing **comments and identity rows are untouched.** No data migration.
- An existing **anonymous** guest gets a fresh signed guest on next visit — its prior anonymous
  attribution is not reclaimed (a public id can't prove ownership). This limits seamless migration,
  not the ability to comment/upload.
- An existing **OAuth-linked** identity is recovered by signing in again (verified adoption re-cookies
  the browser to the owning guest).
- In-flight logins that span the cutover may need restarting (the callback requires an accepted
  cookie).

## Deferred (not in this release)

- **Slice G — graceful key rotation via a keyring (ROADMAP, wanted).** Rotating without logging every
  guest out means holding more than one live key at once (the active signer + not-yet-retired
  verifiers), so the single `GUEST_SECRET` becomes a **keyring**. The envelope +
  `Hedge.GuestCookie.keyFor` already implement key selection + retirement, and slice A's fixture
  already covers rotation/retirement — this slice is purely the app-config wiring plus a fixture
  asserting an old-key token still verifies until retirement while the new key signs.

  **Decided storage shape:** one JSON secret `GUEST_KEYRING` (a Cloudflare *secret*, all of it
  sensitive), e.g. `{"active":"k2","keys":{"k2":{"secret":"…"},"k1":{"secret":"…","retireAt":<epoch>}}}`.
  One encrypted binding, atomic rotation (put the new ring + deploy), no dynamic `env["GUEST_SECRET_"+id]`
  lookups. `Server.GuestConfig.deps` parses it into `Hedge.GuestSession.configFor`'s `Active` +
  `Previous` (replacing the hardcoded `"k1"` / `[]`); malformed/empty → `configFor` throws → fails
  closed. Keep plain `GUEST_SECRET` as back-compat shorthand for a ring of one active key `k1`, no
  previous — so nothing already deployed changes until it actually rotates.

  **To rotate:** add a new active key, move the outgoing key to `keys` with `retireAt = now + window`,
  deploy; drop the retired entry after the window. Two properties make the window cheap: (1) **renewal
  auto-migrates active guests** — a still-valid token is re-signed with the *active* key on next use
  (30-day renewal window), so a ~30–90 day retirement quietly moves regular visitors onto the new key
  and only genuinely dormant guests reset; (2) `retireAt` is the dial between honouring old tokens
  longer vs. how long a compromised old key stays valid (compromise → retire immediately = the
  current in-place-change behaviour).

  **Until this lands, treat any `GUEST_SECRET` change as a full guest reset** (see the rotation
  caveat under "Configuration surface").
- **In-place image-URL re-key / backfill of old objects** — explicitly deferred by the work order;
  old URLs keep serving, new keys are credential-free, so no rename is needed for safety.
- Upload grants, per-user quotas, attachment metadata, scheduled cleanup, broader file validation
  (v2 §3–5) — separate deferred proposal.

## Rollout order (per deployment; the operator runs this)

1. `wrangler secret put GUEST_SECRET` (≥ 32 random bytes) for the tenant.
2. Deploy the code (fail-closed until the secret exists, so set the secret first or together).
3. Browser-verify: `/api/auth/me` sets a `v1.` cookie; first comment posts; first image upload works
   without an admin key; identity list/switch/merge/disconnect and an OAuth round trip work; a
   no-OAuth host still issues/verifies guests.
4. Move to the next tenant. **idealist stays HELD for C6.**

Rollback keeps signed-cookie support and any fixed cutoff; **never restore unsigned acceptance.** Key
compromise today → change `GUEST_SECRET` in place, which invalidates all cookies at once (safe, but a
full guest reset — see the rotation caveat). Graceful, windowed rotation needs the keyring wiring in
**Slice G** (Deferred); report the resulting reauthentication either way.
