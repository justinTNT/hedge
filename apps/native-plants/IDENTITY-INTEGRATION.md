# Plan: login and personal contributions

Date: 24 September 2026. Status: login and personal contributions implemented.

## Current implementation — verified sign-in required

Google live sign-in was confirmed by the owner on 25 September. Notes, correction
read/unread review, private photo uploads, personal hero choices, offers and public
promotion are implemented. Notes and photo features now require verified sign-in,
with `CONTRIBUTIONS_REQUIRE_LOGIN=true` locally and on the Cloudflare preview.
Anonymous visitors see no notebook or personal photo controls; API reads, writes
and media enforce the same policy. Earlier anonymous contributions are retained
and can still be claimed on OAuth login. The earlier rollout notes below are history.

See [CONTRIBUTIONS.md](CONTRIBUTIONS.md) for current behavior, schemas, limits,
migration and verification; [PREVIEW.md](PREVIEW.md) documents the deployed private Cloudflare preview. The detailed plan
below is retained as design history; statements describing slices as future work
are its original baseline, not the current implementation status.

## Implementation progress — 24 September

Slice 1 now composes the shared identity model/server handlers, Google and GitHub
configuration, a Login/account control without anonymous details, direct account
activation, safe page returns, and same-origin browser logout. Public guest uploads
are explicitly disabled independently of session support. Local signing secrets
are generated and the additive identity migration is applied; botanical data is
preserved. The owner will supply OAuth client credentials later, so actual provider
sign-in remains unverified and the live Login panel explains its unavailable state.

The former `IsCuratorReturn` seam is now `ActivateOnReturn`; existing content hosts
retain their claim/curator policies. Shared browser session reads and logout use
generation checks and a cookie-operation lock (across tabs where Web Locks exists).
`AuthConfig.subject` is ready for contribution ownership without granting admin
powers. Owner admin now includes read-only identity lookup for assigning curator
grants; raw guests and private contribution tables remain excluded. See
[the role-assignment workflow](CONTRIBUTIONS.md#policy-and-ownership).

See [README setup](README.md#configure-sign-in) for variables and callbacks.
The contribution slices are now implemented, as described in the current status above. The baseline inventory/gaps below describe what this plan was
written against, rather than claiming those implemented gaps still exist.

This replaces the earlier integration outline with the requested Native Plants
experience. It is based on the current `native-plants` working tree at `71ae033`,
which already contains main's identity/admin extraction and subsequent fixes.
The uncommitted catalogue, photograph, map and glossary work remains the baseline.

## Login-slice verification

- Native Plants production build passed; final client/site rebuilds passed after UI refinements.
- Native Plants tests: 18 importer checks and 44 Node checks passed, including 17
  identity client/worker checks. Seven shared session-runtime checks also passed.
- Full repository `./test.sh` passed, including generated artifacts, SQL/migrations,
  existing hosts, Justat production build and the new-app scaffold. The initial
  Justat failure was missing local Vite dependencies; installing Articles' locked
  dependencies resolved it without source or lockfile changes.
- Chrome verified the Login panel, anonymous presentation, narrow-screen layout,
  Escape dismissal and absence of console errors. The temporary viewport was reset.
- The live migration preserves exact record fingerprints for 531 plants, 1,872
  photo records, 295 maps, 95 glossary terms and 145 references. Local backup:
  `/tmp/native-plants-before-identity.sqlite` (temporary, outside the repository).
- Google/GitHub callback integration is tested with simulated provider replies.
  Real provider login is intentionally pending the owner's credentials/callback
  registration. No production deployment or fake login endpoint was added.

## User experience

Anonymous readers see a **Login** link towards the top right. They continue to
browse the complete public guide. Do not display an anonymous name, avatar,
identity selector or guest identifier. A signed session may exist internally;
that is an implementation detail, not an account presented to the reader.

Login offers the configured OAuth providers and returns the reader to the same
species/page. There is no anonymous-content claiming screen in this app. After
login, show a compact account control and expose **Add note** and **Add photos**
on species pages. Ordinary contributors need no role grant or admin key.

| Action | Result |
| --- | --- |
| Add/edit a note | A private note attached to that species, visible to its author. |
| Mark a note as a correction | Offer that note to the administrative correction queue; it remains visible to its author and authorized reviewers. |
| Upload a photograph | Append it to the author's view of that species' photograph roll. Other readers do not see it. |
| Make one of my photographs the hero | Use it as the hero for **my view of this species**; persist the preference across visits/devices. |
| Use the site's hero | Clear my preference and use the current curated hero. |
| Offer a photograph for public sharing | Put it in the editorial review queue. Offering alone does not publish it. |
| Editor promotes an offered photograph | Add it to the curated public selection, preserving credit and its contribution link. |
| Editor marks a correction read/unread | Update the review queue. Reading a correction does not silently change the species account. |

The personal interpretation of “hero” is deliberate: contributors control their
own view; the site retains editorial control of its public selection. Initially
this preference affects the species detail page, not browse cards/search results.

Notes can be plain text for this slice. Keep correction and sharing controls next
to their respective note/photo; no separate social profile or activity feed is
needed. Offer status and inclusion in the public selection should be distinct.

## Photograph composition

Compose the displayed photographs from the curated selection plus the signed-in
user's contributions. Keep the existing responsive layouts and photograph viewer,
including the rule that an image below 200px in either dimension has no enlarged
view. There is no five-image cap.

- Personal images appear automatically, with a small “Yours” treatment. Uploading
  does not require offering the image for public use.
- A personal hero moves to the hero position, without repeating it among the
  supporting photographs. The site's former hero remains available in the roll.
- If the selected personal image is deleted/unavailable, fall back to the site's
  hero. If the site has no photographs, the user's photographs can still display.
- A promoted contribution appears once for its author. The private response maps
  contribution IDs to published photo IDs; the public catalogue does not need
  owner/account identifiers to achieve this.
- Logout clears notes, personal photographs, hero preference, drafts and any open
  private-photo viewer. Navigation and account changes reject stale responses.

## What Hedge already supplies, and what remains

| Existing implementation | Use here / remaining work |
| --- | --- |
| `Hedge.GuestSession`, `GuestCookie`, router OAuth endpoints | Reuse signed-session and provider verification mechanisms. Native Plants currently sets `OAuth = None` and `GuestSession = None`. |
| `Identity.Server.activeSubject` | Resolve the active verified `(provider, provider_user_id)` for ownership, without requiring grants. |
| `Identity.Handlers` and shared identity schema | Reuse account persistence and OAuth adoption. Supply empty comment/re-attribution policies; these are supported. |
| Optional `Identity.Grants` and `Hedge.AccessControl` | Reuse for delegated reviewers, with app-defined permissions and per-request checks. |
| `Hedge.Admin.Authorize` and permission-aware admin discovery/CRUD | Keep owner-key access and explicit delegated resource permissions. This is **not** a row-ownership or review-action implementation. |
| Native Plants' projected, cached public catalogue | Keep it public and shared. Fetch personal material separately. |
| `BlobServing.PrivatePrefixes = ["private/"]` in this app | Already prevents generic public serving of that prefix. Private upload/read handlers still need implementing. |

Three small integration seams need explicit treatment before enabling login:

1. **Direct authenticated return.** The shared OAuth completion hook still calls
   its policy `IsCuratorReturn`. Otherwise it redirects through `/auth/claim`.
   Generalize that shared policy to express direct activation/return versus the
   existing claim flow. Native Plants chooses direct activation, with validated
   same-site return paths and a safe fallback. Preserve existing hosts' claim
   behavior. Do not add a Plants-specific exception to identity or copy its handler.
2. **Separate sessions from anonymous uploads.** In the current router,
   `GuestSession = Some ...` also enables `POST /api/blobs/guest`, which accepts
   anonymous signed guests and writes publicly served images. That route runs
   before app routes. Add explicit configuration for this existing upload
   capability: existing comment hosts retain their behavior; Native Plants disables
   it and provides authenticated private uploads. App-route guards cannot fix this.
3. **Browser logout.** There is no simple shared logout endpoint in the inspected
   router. `Identity.Handlers.disconnect` moves the provider identity to another
   guest; it is account disconnection, not merely logging this browser out.
   Add/reuse a narrowly scoped cookie-clearing session operation rather than
   relabeling disconnect. Keep other devices' account associations intact. Clear
   client session state and coordinate outstanding requests/tabs so late responses
   do not restore personal UI. This does not introduce per-device token revocation.

The `/api/auth/me` envelope and shared client session accessor contain guest
details. Adapt them to the app's `SignedOut | SignedIn` presentation, accepting
only a non-anonymous verified identity. Do not infer authentication merely from
an accepted guest cookie, a generated display name, or an identity object being
present. Filter anonymous identities in the host's resolver as well as the UI.

## Responsibility boundaries

| Layer | Responsibility |
| --- | --- |
| Hedge framework | Signed cookies, OAuth protocol, HTTP/admin enforcement, explicit upload capability configuration, generic session logout mechanism. |
| Shared identity module / optional grants | Identity persistence and lifecycle, verified subject resolution, completion policy, grant storage. |
| Native Plants app | Species notes/photos, ownership checks, personal hero, correction/read state, offer/promotion rules, private media access and review UI. |
| Host/deployment configuration | Providers, secrets, app composition, role-to-action mapping, private storage prefix. |

Do not create another reusable contributions module yet. This is a distinct app
with its own botanical/editorial behavior. The integration exercises the shared
identity capability without importing Microblog/Alerts implementation namespaces.

## App-owned records

Exact names can follow existing model conventions; the important contracts are:

| Record | Fields / invariants |
| --- | --- |
| `PlantNote` | ID, plant ID, owner provider/account pair, text, correction flag, revision, reviewed revision (nullable), timestamps/deletion. |
| `PersonalPlantPhoto` | ID, plant ID, owner pair, private original/display/thumbnail keys, dimensions/MIME, caption, credit, offered-for-public flag, revision, timestamps/deletion. Storage keys are server-owned. |
| `PlantViewPreference` | Owner pair + plant ID, optional personal hero photo ID. Composite unique constraint on the owner pair and plant. |
| Published `PlantPhoto` extension | Nullable unique source-contribution ID, retaining the existing public image, thumbnail, caption, credit and sort order. Existing imported photos have no contribution link. |

Use the existing `UniqueTogether` generator support for composite constraints.
Do not use guest IDs, email addresses, names or mutable identity row IDs as durable
ownership. Set owner fields from the authenticated request, never a submitted body.

The verified provider/account pair survives identity-row deduplication and guest
adoption. It does **not** assert that someone's Google and GitHub logins are one
person: those are separate owners unless a later explicit account-linking policy
is introduced. Logging back into the same provider account restores its material.

For corrections, “read” means the current revision has been reviewed. Editing a
submitted correction advances its revision and makes it unread again. Mark-read
must target the revision actually seen, so a concurrent edit is not lost. Mark-
unread clears the review marker. Read/unread is shared editorial state initially,
not a separate inbox per reviewer. A note unmarked as a correction leaves the queue.

Compose the existing app model assembly, `IdentityModels`, and optional
`GrantModels` through the generator's supported composition entries. Add their
project references and shared server props; do not replace the existing app slice
or rename its tables. Use additive migrations and preserve all imported/editor-edited
botanical records. Do not expose personal tables through unrestricted generated CRUD.

## Reads, writes and private media

Keep two data flows:

1. **Public:** the current catalogue snapshot, in-memory search/typeahead and
   species detail data. No private notes, uploads, account data or preferences.
2. **Personal:** an authenticated, owner-scoped species payload containing notes,
   personal photos/status and hero preference. Read this from D1 on demand; it is
   small user-specific data and does not belong in the shared catalogue snapshot.

App-owned typed endpoints provide that read plus note create/edit/delete, photo
upload/metadata/offer changes, and hero selection/reset. Every protected operation
resolves the current subject; every object lookup checks ownership and plant
association. Setting a hero verifies that it is the caller's available photograph
of that species. No generic full-row update endpoint for these records.

Uploads go to opaque keys under `private/native-plants/`. Serve them through an
authenticated `/api/...` media endpoint so current worker-first routing applies.
Allow the owner, and an authorized reviewer only while the image is eligible for
their review. Raw `/blobs/private/...` remains unavailable. An unguessable URL by
itself is not authorization.

Use bounded raster-image uploads with validated type/size, dimensions, thumbnail
generation and an explicit per-user storage limit. Do not embed an unsigned public
blob URL in a “private” photo record. Strip location/other unwanted metadata from
public renditions at promotion; preserve useful photographer credit explicitly.
Handle failed uploads without leaving broken rows or untracked stored objects.

Personal responses and media use private/no-store caching and stay out of shared
client storage/catalogue revision calculation. Mutating personal data refreshes
only that user's view. Promotion invalidates the public catalogue using its
existing refresh policy. Propagate session renewal cookies on protected responses,
including denials where the shared guard supplies them. Apply same-origin write
protection to cookie-authorized mutations.

## Administrative review

Use existing owner-key authorization and optionally an enabled reviewer/curator
grant. The app maps that grant to bounded review actions; a role must not grant
generic access to all private records, identities or grant management.

Provide two app-owned review queues in the administrative experience:

- **Corrections:** species, submitted note, author attribution and read/unread
  state; open the species account and mark the current revision read/unread.
  Applying an accepted correction is an explicit authorized species edit.
- **Offered photographs:** species, preview, caption/credit and current status;
  promote an eligible image into the public selection. Unoffered images are absent.

The common admin currently describes CRUD resources, not arbitrary domain actions
or caller-scoped rows. Reuse its authorization mechanisms, but implement these
filtered queries and targeted handlers in Native Plants. Initially a linked
app-owned review view is sufficient; if embedding needs a shared admin navigation
hook, keep that hook small. Do not launch another general admin redesign.

Promotion must recheck the offer/current revision, copy a public rendition to a
public key, create the curated `PlantPhoto`, preserve attribution and invalidate
the catalogue. Enforce one public photo per contribution and make retries safe;
R2 copy and D1 writes are not one transaction. Reconcile failed/stale promotions
without exposing a private storage key or losing the user's original.

New promotions append to the public selection; they do not automatically replace
its hero. A user may withdraw an unpromoted offer. Once selected, the public copy
is an editorial asset: deleting a private upload must not break it. Public removal
is a separate editorial operation; describe this distinction beside the offer
control before enabling publication.

“Monitored by admin” means an actionable unread correction queue in this slice.
Email notifications, review conversations and automated botanical edits are not
required for this first implementation.

## Implementation sequence

1. **Login end to end.** Compose identity/schema/secrets, address the three shared
   seams above, add the top-right Login/account control and safe return to species.
   Verify anonymous details stay hidden and guest public uploads remain disabled.
2. **Notes and corrections.** Add authenticated ownership and separate personal
   fetching; implement note editing/flagging plus the bounded read/unread admin
   queue. This proves the full contributor-to-editor loop before media storage.
3. **Private photographs and personal hero.** Add protected upload/read endpoints,
   the appended gallery, preference persistence and logout/navigation cleanup.
4. **Offers and promotion.** Add the offered-photo review queue, permission mapping,
   public rendition/promotion, provenance, deduplication and public cache refresh.
5. **Rollout.** Regenerate checked-in artifacts and apply additive migrations before
   enabling routes. Configure a real login provider/callback and signing secrets;
   no production resources exist for this app yet. Verify locally before a separate
   deployment decision.

Each slice should deliver working UI and server enforcement together. Ordinary
login/ownership can land without delegated grants; owner review works first, and
the same bounded actions can then be granted to trusted reviewers.

## Acceptance and verification

- Anonymous navigation remains fully usable; only Login is shown for identity.
  Signing in returns to the species without a claim/merge screen. Failed/cancelled
  login is recoverable. Signed-out writes and anonymous guest uploads are rejected.
- Two real/test provider subjects cannot read or change each other's notes, media
  or preferences, including direct endpoint/URL calls. Tampered owner/plant IDs
  do not change that. Ordinary contributions do not require a role grant.
- The same verified account restores its material on another browser; a different
  provider account does not inherit it. Duplicate identity-row merging preserves
  ownership. Logout does not disconnect the account on other devices.
- Personal hero selection changes only the author's detail page. Reset, deletion,
  no-public-photo species, existing small-image behavior and promoted-photo dedup
  all work. Public browse/search data remains unchanged by private edits.
- Logout, expiry, navigation and account changes clear/reject private state and
  late responses, including open viewers. Private payloads/media never enter the
  public catalogue or public blob route.
- Reviewers see only flagged corrections and eligible offered photos. No-grant and
  revoked users cannot review; grant management remains owner-only. An edit during
  mark-read remains unread. Competing withdrawal/promotion and retrying promotion
  do not publish an ineligible image or create duplicates.
- Fresh and migrated schemas agree; existing catalogue/importer tests and app
  build pass. Shared auth/router changes also run the repository gate and existing
  hosts' login/claim/upload regression checks. Exercise real OAuth separately from
  handler tests; document any provider setup still needed.

## References

- [Identity/admin overarching plan](../../notes/IDENTITY-ACCESS-CONTROL-overarching-plan.md)
- [Identity extraction plan](../../notes/PHASE1-identity-extraction.md)
- [Native Plants architecture and data flow](README.md)
- [Shared identity subject resolution](../../packages/modules/identity/src/Server/Identity.fs)
- [Shared identity completion/lifecycle handlers](../../packages/modules/identity/src/Server/Handlers.fs)
- [Framework auth and blob routing](../../packages/hedge/src/Hedge/Router.fs)
- [Shared admin authorization contract](../../packages/hedge/src/Hedge/Admin.fs)
