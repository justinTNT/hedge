# Species notebooks and photo contributions

Implemented 24 September 2026. Available locally and in the private Cloudflare preview.

## Experience

- Signed-in species pages have **Field notes**, with compact edit/delete icons
  and subtly inset note text. A correction checkbox shares a note with reviewers.
- A camera-plus button in the next gallery slot adds a photograph. Photo editing
  and upload feedback stay beside the gallery, separate from field notes.
- Uploaded photos join the author's species gallery. **Make my hero** changes
  only that visitor's species view; **Use site hero** restores the editorial choice.
- **Photo details** edits the caption and optional photographer credit. Offering
  a photo lets reviewers inspect it; it is not published until selected.
- `/review` lists corrections with read/unread actions and offered photos with
  **Include on species page**. Read status follows the reviewed revision: editing
  a correction makes it unread again. Review queues paginate in groups of 50.
- Promotion appends to the public selection. It makes an independent public copy,
  retains the credit and contribution link, and leaves the existing public hero
  order intact. The author sees a promoted photo once. Deleting their private copy
  does not delete the editorial copy; that remains manageable as a PlantPhoto.

As of 25 September, notes, uploads and personal photo features require a verified
Google/GitHub sign-in. Anonymous visitors see the catalogue and the top-right
Login control, without a notebook section or contribution controls. The server
also rejects anonymous personal reads, writes and private-media access.

Earlier anonymous contributions are retained. Their owner remains the verified
signed guest session, never a client-supplied ID or generated display profile.
Keep that cookie to claim the content on sign-in; no data migration is needed.

Signing in automatically moves that browser's notes, photos and hero preferences
to the OAuth-verified `(provider, provider_user_id)`. Existing account hero choices
win conflicts. A transactional claim tombstone prevents late anonymous writes
from recreating abandoned content after an account is adopted. There is no claim
screen and no dependency on an identity row ID or email address.

## Policy and ownership

`CONTRIBUTIONS_REQUIRE_LOGIN=true` is set locally and for the Cloudflare preview.
The server requires a credential-backed active identity by default, including if
the setting is missing or misspelled. Explicit `false` remains only for legacy
anonymous-content integration fixtures; the client displays contribution features
only to verified accounts. `AuthConfig.subject` remains verified-only; the policy
is centralized in `Server.Contributions.owner`. Login still claims legacy content.

Review requires the existing site admin key or an enabled shared `curator` grant.
It checks the grant on every request, so revocation takes effect immediately.
Grant CRUD is in the owner-only admin. Its **Identity** section allows read-only
account lookup; identity creation, editing and deletion are rejected by the server.
Raw guest/session and private contribution tables remain excluded. Reviewers see
only offered photos and corrections, not ordinary private material.

To assign a curator:

1. Have the person sign in to this site once with Google (or GitHub when configured).
2. Open **Identity**, find their name/email, and copy **Provider** and **ProviderUserId**.
3. Open **Grant → New**, paste those two values, set **Role** to `curator`, tick
   **Enabled**, and save. **GrantedBy** is optional. Use the provider account ID,
   not the guest ID, identity record ID or email address.
4. They sign in and open `/review` to read corrections and select offered photos.
   They do not need the admin key and cannot use catalogue or grant CRUD.
5. Untick **Enabled** on the grant to revoke access; the next review request checks it.

The site footer and admin navigation use `/api/plants/v2/access`, which applies the
same owner-key and curator-grant checks as the protected endpoints. Catalogue
links appear only after the saved admin key is validated. Review links and the
review interface appear only for an owner or curator. Direct `/review` navigation
shows an access-required screen otherwise. Capabilities are refreshed on identity
changes, focus and periodic checks; every review request still authorizes afresh.
Logout clears the OAuth identity, not a separately saved owner key: that key
continues to authorize the owner independently. Removing it removes owner access.

There is currently one app-consumed role, `curator`. Arbitrary role names do not
grant additional powers. Anonymous guests cannot hold a usable curator role.

The ownership schema, queries, endpoints, review actions and UI are app-owned.
Hedge supplies signed sessions, OAuth, role verification and the shared request
lock. The public in-memory catalogue never includes private records or owners.

## Per-species limits

Each owner can keep **five active notes and five active personal photos per
species**. The limit is separate for each species and owner; the curated species
gallery and other people's contributions do not use these slots. At the limit,
existing items remain editable and deletable, but adding another requires making
room. Counts stay out of the interface: attempting an addition at capacity
explains the limit and asks the user to delete an existing item. Add controls
remain available so that explanation can be requested. The API independently
enforces the same limit.

Counts are server-owned `NoteCapacity` and `PhotoCapacity` values (`Used`, `Limit`),
not deductions from visible gallery length. The server uses the same slot
predicates for counting and conditional inserts, so concurrent requests cannot
both take the last slot. In-flight photo reservations count before R2 writes;
failed uploads release their species slot. If cleanup fails, retained object keys
and bytes still count against the separate account storage quota.

Current lifecycle policy:

- A deleted personal note or photo frees a slot immediately.
- Marking a correction read/unread does not free a note slot.
- Offering or promoting a photo does not free a photo slot while its personal
  copy remains. Deleting that copy frees the slot and preserves its public copy.
- No new admin lifecycle actions are defined here. Future archive/delete/resolve
  actions must define whether they release a slot, and update the shared counting
  and insertion predicates accordingly. The client refreshes server counts rather
  than maintaining its own assumptions about admin status.
- Existing data and legacy account adoption are non-destructive. If adoption
  produces more than five items, all remain accessible and editable; additions
  stay blocked until the count drops below five. Nothing is trimmed on deployment.

There is no schema migration. Existing account-wide safeguards (2,000 notes,
100 photo records including retained failed/deleted uploads, and 250 MiB) continue to apply.

Focused checks cover the fifth/sixth boundary, owner/species isolation, concurrent
note inserts and pending uploads, promotion/read status, deletion, failed cleanup,
completed-upload retry after a response failure, legacy adoption above the limit,
and quiet client limits with feedback on an attempted addition, without discarding drafts.

## Media and request boundaries

The browser accepts JPEG, PNG and WebP up to 30 MB / 80 megapixels. It renders
orientation into JPEG copies at at most 3200px and 480px. These are presentation
copies, not an original-photo archive. Server ingestion independently bounds the
stream (6 MB total), each file (5 MB / 512 KB), dimensions and JPEG container;
it removes EXIF/GPS/XMP/IPTC/comment segments before storing.

Private R2 keys use `private/native-plants/`. The generic blob route cannot serve
them. `/api/plants/personal-media/:id/{image,thumbnail}` checks ownership or an
offered-photo review grant and returns `private, no-store`, `nosniff` responses.
Media reads deliberately do not renew cookies. Protected JSON requests join the
same cookie-operation lock as login-session refresh/logout. A logout or page/owner
change discards late results and clears private UI. Mutation requests require a
same-origin POST and the current response's opaque viewer token.

Notes use revision checks. Upload reservations enforce at most 100 photos and
250 MiB per owner, including outstanding storage, before R2 writes. A repeated
completed upload ID returns the existing photo. Failed private writes are cleaned
up; if cleanup fails the unready reservation retains the object keys and byte
accounting for maintenance. These are per-owner bounds, not a global anti-abuse
or request-rate limit for anonymous sessions.

Promotion rechecks offer state and revision in its database write. It is
idempotent and cleans unused public copies on handled failures; R2 and D1 do not
share a transaction, so interrupted promotion attempts can require orphan-object
maintenance under `native-plants/contributed/`. Never remove keys referenced by
`plant_photos`. Failed private cleanup remains discoverable in
`personal_plant_photos` (`ready=0`, or `deleted_at` set with `stored_bytes>0`).

## Database and checks

Fresh installations use `npm run db:init`. Existing identity-enabled databases use
`npm run db:migrate:contributions` once (migration `0002_contributions.sql`). This
only adds notes, photos, hero preferences, claim markers and shared grants. The
local database is migrated; existing plants, curated photos, maps, glossary and
reference records were compared to the pre-identity backup and are unchanged.
The post-migration snapshot is `/tmp/native-plants-contributions-migrated.sqlite`.

`npm test` covers signed-anonymous isolation, verified claims/account adoption,
the credential-only switch, role revocation, revision races, promotion/withdrawal
races, media gates, quotas, cleanup, migration equivalence and client state races.
The shared session-runtime suite checks pending/queued requests during logout.
The repository gate also exercises the other Hedge hosts.

Chrome verified anonymous note creation, editing/correction flagging, owner review,
read status returning to the author and notebook layout. The browser upload check
could not finish because the upload permission prompt was dismissed. Worker/API
upload tests pass; a real browser upload remains a manual verification item.
Google/GitHub callbacks have simulated-provider integration coverage. The owner
has now configured Google and confirmed a successful live sign-in on the preview;
GitHub remains unconfigured.

## Private Cloudflare preview

Deployed 25 September 2026 at https://native-plants-preview.justin-8ee.workers.dev.
See [PREVIEW.md](PREVIEW.md) for the ignored credential file, isolated resources,
the outer password gate, OAuth callbacks and repeat deployment commands.

Live HTTPS checks verified denied unauthenticated pages/assets/APIs/callbacks,
secure signed cookies, separate anonymous notebook ownership, same-origin write
checks, owner correction review, private D1/R2 upload/delivery and personal hero
selection. The upload check used the existing API JPEG marker fixture; it does
not replace browser file selection/decoding verification. Temporary notes and
photos were removed through the app. These initial anonymous-mode checks precede
the sign-in requirement. The current policy also has coverage for denied anonymous
reads/writes/uploads/media, unchanged browsing, legacy content adoption, verified
notes/uploads, and anonymous versus verified species-page rendering. Google live
sign-in was subsequently confirmed by the owner; GitHub remains pending.


## Typed API integration — deployed 25 September 2026

Ordinary private JSON now uses the generated `/api/plants/v2` surface declared
in `Models/Api.fs`. Success responses use camelCase: capabilities expose
`canEditCatalogue`/`canReview`; personal and review endpoints return a `personal`
or `review` snapshot containing codec-checked nested records and lists.
`Client.ContributionsApi` adapts these to the existing array-based view models.

| Method | Path beneath `/api/plants/v2` | Operation |
| --- | --- | --- |
| GET | `/access` | Current owner/reviewer capabilities. |
| GET | `/personal/:plantId` | Own notes, photos, hero and capacities. |
| GET | `/review?page=0` | Authorized correction/offered-photo queue. |
| POST | `/notes/save`, `/notes/delete` | Revision-checked note changes. |
| POST | `/photos/update`, `/photos/delete` | Revision-checked personal-photo changes. |
| POST | `/hero` | Select or clear an own-photo hero. |
| POST | `/review/correction`, `/review/promote` | Read/unread or publish a current contribution. |

Mutation DTOs carry the plant/item/revision values explicitly; there is no
dynamic wire `Action` field. The app authorizes before reading the POST body,
checks origin/content type, caps streaming JSON at 24,000 bytes even without
Content-Length, and only then invokes the generated decoder. Handlers recheck
the active owner/reviewer and species visibility. Validation, claims, limits,
revisions and storage changes remain in the same app commands used by v1.
The schema and migrations are unchanged.

Private generated requests use Hedge's no-store browser transport inside the
existing guest-session lock, consume responses before releasing it and keep
HTTP/decode/transport failures distinct until the view renders an error. The
request captures its owner key/viewer token. Existing client generations still
discard late results after navigation or session/key changes. Public catalogue
requests keep their ordinary transport.

Multipart upload and authorized media routes keep their existing URLs. Upload
consumes its response inside its own lock, returns a typed success/error result,
then loads a generated personal snapshot in a separate lock. It does not decode
the legacy upload response as an unchecked personal record or nest locks.

The main app and admin-page links use the same typed capability loader and
shared credential subscription. Applying/removing an owner key in this tab or
another tab invalidates capabilities; the admin shell no longer polls storage
every second. Its 15-second server refresh remains for grant revocation, along
with focus/visibility/session refresh. Subscriptions provide disposal. The
shared admin also clears loaded data and rejects older-key completions.

Admin registration explicitly names Plant, PlantPhoto, PlantMap, GlossaryTerm,
SourceReference, Grant and Identity descriptors. Identity supports only list/read.
This ceiling is enforced even for AdminOwner. New generated descriptors cannot
silently enter the admin. Curators still receive no catalogue CRUD rights.

### Compatibility window

The previous PascalCase JSON endpoints (`/api/plants/access`, `/personal/:id`,
and `/review`) remain for one compatibility release. They parse old inputs and
invoke the same typed commands as v2; they are not a second implementation of
contribution policy. This allows already-open/cached clients to keep working
when the new Worker and client are deployed together.

Retire the v1 JSON adapters only in a later release after v2 has been deployed,
old clients have been given a refresh window, and any remaining old clients
have been accounted for. Keep the upload and personal-media routes: they are
deliberately outside this JSON migration. Do not remove v1 in this refactor.
The private preview now runs the v2 client and Worker, version
`2c6e318d-d95b-4a1d-8500-0b9e60d1101c` (source `350cd35`). The compatibility
window starts with this deployment; v1 JSON adapters remain available for old
open tabs. No existing catalogue, grants or contributions were changed.
