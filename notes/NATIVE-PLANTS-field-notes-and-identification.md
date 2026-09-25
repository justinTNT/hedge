# Native Plants: field notes, corrections and identification requests

Recorded 25 September 2026. Implemented 26 September 2026 following the owner's
instruction and clarification of photo requirements.

## Entry model

| Action | Photos | Audience |
| --- | --- | --- |
| Private Note | None; text only. | Author only. |
| Correction | Optional, from the author's species photo roll. | Author and curators. |
| ID request | At least one, from the author's species photo roll. | Author and identifiers. |

One editor serves all three actions. It selects existing photographs or uploads
and selects a new one if the roll has room. Uploading keeps the text draft;
cancelling the entry leaves the uploaded photo in the roll. Text is required,
up to 6,000 characters. Standalone gallery uploads remain available.

All purposes share five notes per owner per species. Photos share the existing
five-photo allowance. Linking an upload to multiple entries never consumes more
photo slots. Offering a photo for publication is separate from attaching it.

## Review and lifecycle

- The existing **curator** grant reads corrections, marks their current revision
  read/unread, and selects explicitly offered photos for publication.
- A separate **identifier** grant reads ID requests and selected attachments,
  and responds to a current revision. Neither role implies the other. The owner
  key can do both; it does not expose private notes or unrelated personal photos.
- Outcomes are **confirmed**, **alternative suggested**, **not this species**,
  or **unable to determine**. An alternative selects a different, published
  catalogue species. It never moves a photo or changes the public species page.
- Responses store the verified reviewer subject, display name, timestamp,
  submitted revision and original request text. Authors see attribution, not
  private provider-account IDs. Owner-key responses say “Site owner”.
- There is one immutable response per submitted revision. An author's edit
  reopens review. Earlier responses stay visible to the author, labelled as
  earlier versions, with the original request text. No conversation thread or
  response-editing UI is introduced.
- Withdrawal converts the entry into a text-only private note and removes its
  attachments. It leaves photos in the roll and response history with the author.
  Converting a former ID request to a correction does not share that conversation.
- A linked photo cannot be deleted while an active entry references it. Unlink
  it, withdraw the entry, or delete the entry first. This prevents deleting the
  last photo out of a live ID request.
- Deleting an entry/photo releases its slot. Read/unread, responses, withdrawal,
  offers and publication do not. Public image copies remain independent.

Queues/media check current grants on every request. A role change clears the
loaded queue even when another review role remains. Private records stay out
of the public in-memory catalogue.

## Storage and boundaries

All domain behavior remains in Native Plants. Hedge supplies verified identity,
grants, session locking, generated codecs/clients/routes, and owner-only admin.

Migration **0003_field_notes.sql** adds nullable purpose and photo_ids fields to
plant_notes, plus identification_responses. Null purpose derives from the old
correction flag, preserving existing notes without rewriting them. Photo IDs are
a bounded JSON list of at most five references, replaced in the same conditional
SQL statement as text/purpose/revision. SQL checks readiness, species and owner;
linking/deletion races cannot leave invalid attachments.

Responses have a unique (note_id, note_revision) index. Conditional inserts reject
changed/deleted/withdrawn submissions and invalid alternatives. Private tables
remain excluded from generic admin CRUD.

Generated endpoints add POST /api/plants/v2/notes/entry and
POST /api/plants/v2/review/identify. Old checkbox-based save routes remain during
the compatibility window, but cannot erase attachments or rewrite ID history.

## Verification and release

Tests cover purpose/photo requirements, owner/species isolation, shared quotas,
separate reviewer/media access, withdrawal/revocation, attribution/history,
stale-write races, old-client safety, preservation migration, upload drafts and
client role changes. The repository gate also passes.

Chrome on an isolated in-memory fixture verified an ID submission using an
existing photo, the review queue, an attributed response and its return to the
author's notebook. Browser file selection was denied by the browser tool; a real
browser upload remains a manual check. API upload and client draft tests pass.

See [PREVIEW.md](../apps/native-plants/PREVIEW.md) for deployment status.

## Later decisions

Follow-up conversation, editing a reviewer's response, alternatives outside the
catalogue, reviewer assignment/notifications and archival lifecycle changes stay
separate. None is required for these entry purposes.

## Related notes

- [Contributions, privacy and limits](../apps/native-plants/CONTRIBUTIONS.md)
- [Identity integration](../apps/native-plants/IDENTITY-INTEGRATION.md)
- [Shared identity/access-control boundaries](IDENTITY-ACCESS-CONTROL-overarching-plan.md)
