# Integration with shared identity and access-controlled admin

Native Plants is rebased onto main's identity/admin merge (`5d2ae5d`, including the
review fixes in `1954eec`). It now uses the shared admin's `Authorize` contract through
`Hedge.Admin.ownerKey`; the framework gates discovery and CRUD. The app still uses
owner-key access while personal contributions and delegated review are implemented.

The shared identity module and optional grant support are available. The next
integration slice can use core `Identity.Server.activeSubject` for ownership without
enabling grants. Scoped contribution reads, bounded review/promotion operations and
private media still need implementation and the planned second-consumer checks.

## Existing seams

- Public botanical content is app-owned `Plant` and `PlantPhoto` data.
- `AdminConfig.Authorize` binds the owner-key adapter; extend that binding with the
  app's delegated policy when bounded review operations are ready. Reuse shared grants.
- The Worker currently opts out of OAuth and guest sessions. Enable the shared
  identity module through its shared composition surface in the next integration slice.
- The public catalogue explicitly projects allowed fields. New personal records
  must not be added to that projection by default.
- A photo promotion should create/update a curated PlantPhoto through an app-owned
  operation, retaining provenance and credit, and invalidate the public catalogue.
  The shared admin hosts and authorizes that operation; it does not own botanical policy.

## App-owned contribution model to implement next

| Record | Required meaning |
| --- | --- |
| Plant note | Stable ID, species ID, owner subject, text, optional correction flag, timestamps. |
| Personal image | Stable ID, species ID, owner subject, private media reference, caption/credit, optional public flag, timestamps. |
| Review/promotion state | Correction read/unread and the link from a promoted image to its contribution, using the shared admin's operation model. |

Bind the owner to the shared identity module's authenticated-subject contract. Do
not use a browser guest ID as permanent ownership or require a curator grant for
ordinary personal contributions. Establish switching/merge behavior with that
contract before creating persisted owner references.

Personal notes/images are authenticated, owner-scoped data, fetched separately
from the shared in-memory botanical catalogue. Reviewers get only the records and
operations explicitly offered for review. Private media requires an authorized
upload/serving facility; today's public static guide photographs are not that facility.

Use the requested flags and capabilities without settling additional product
policy prematurely. Public-image visibility timing, withdrawal and per-reviewer
read-state semantics belong to the integration work. The current catalogue has no
personal contribution endpoints or nonfunctional sign-in controls.

## Species-page photograph experience

The shared selection has one hero photograph and smaller supporting photographs;
every image opens the same full-size viewer. There is no fixed five-image limit.
The selection can show flowers, nuts, leaves, juvenile/adult differences and other
useful identification views, as well as different aspects of a plant's beauty.
An owner can choose the hero by changing the curated photos' sort order.

When a user signs in, the species page must also show that user's own photographs,
including ones that have not been offered for public use or promoted. Load them
through an authenticated owner-scoped request, alongside the shared catalogue,
with a clear "Your photographs" treatment and the same image viewer. They should
appear automatically on the species page, without visiting a separate profile.
Seeing one's own image here does not publish it or make it visible to other users.

Promotion into the shared selection is an editorial outcome users can aspire to;
it preserves attribution and the contribution link. A promoted image should appear
only once on its owner's page, with its ownership/public-selection status visible.
On logout or identity switching, clear the personal images and any open personal
image viewer immediately; reject in-flight responses for the previous identity.
Personal responses and media must never enter the shared catalogue cache.

## Acceptance for contribution integration

1. An authenticated user can manage their own species notes and images.
2. A second user cannot read private material or alter the first user's records.
3. An authorized reviewer can mark corrections read/unread and promote eligible
   images through the common admin, preserving credit and contribution provenance.
4. Revocation and anonymous/session changes are enforced by the shared service.
5. Promotion updates the public catalogue under its documented refresh policy;
   personal material stays out of public catalogue responses.
6. The app imports the shared identity/admin capability without Microblog or Alerts
   implementation namespaces, and without changing the source-import pipeline.
7. A species page shows the curated hero/gallery and the signed-in user's own
   images. All open in the viewer; another user sees only their own personal images.
8. Promotion does not duplicate the photograph for its owner. Logout, account
   switching and late responses cannot leave another identity's images on screen.
