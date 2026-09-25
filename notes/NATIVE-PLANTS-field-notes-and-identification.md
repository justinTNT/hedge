# Native Plants: field notes, corrections and identification requests

Recorded: 25 September 2026. Status: agreed direction for later design; not implemented.

## User-facing model

The field-notes section can offer three separate actions, all creating the same
kind of entry: authored text, a set of associated photographs, and a purpose.

| Action | Purpose and intended audience |
| --- | --- |
| Private Note | Personal observations, visible to their author. |
| Correction | A suggested correction submitted for editorial review. |
| ID request | A request for identification help, submitted to people granted the appropriate identification capability. |

Use a common editor with the purpose selected by the entry action. Notes and
corrections can be useful without photographs. Whether an ID request must have
at least one photograph is an implementation decision for the later plan.

Photographs are linked from the owner's collection, not copied into each entry.
The same photograph can appear in an entry and in the owner's species gallery
without consuming two upload slots. Standalone gallery uploads remain useful.
An offer for public gallery inclusion is separate from sharing an entry and its
attachments with the relevant reviewers. Linking must never unintentionally
expose other private notes or photographs.

## Identification outcomes

Possible responses include:

- Confirmed as the suggested species.
- An alternative identification suggested, with a distinct visual treatment.
- Rejected as this species, without claiming to know the correct identification.
- Unable to determine / unknown.

Preserve the author's original note and attach the reviewer's response and
outcome. Do not overwrite the author's words or obscure who made the assertion.
Suggesting an alternative and simply rejecting the proposed species convey
different information. This workflow does not automatically change a public
species account, move a photograph or publish an image.

## Ownership and boundaries

This is Native Plants domain behavior: entry purpose, photo associations,
identification responses, editorial states and presentation belong to the app.
Use Hedge's identity and access-control capabilities for authenticated subjects
and grants. An identification role/capability and its bounded actions still need
to be defined; do not assume the existing curator role has these new powers.

The proposed allowance remains five notes per owner per species across all
three purposes together, and five personal photographs per owner per species.
Linking photographs does not create additional quota. Future resolve/archive/
delete actions must explicitly decide whether they release an allowance; current
correction read/unread and photo promotion do not do so.

## Still to decide when implementing

- Exact entry, attachment and reviewer-response schema and migration from the
  existing correction flag; preserve current private notes and photographs.
- Which reviewers can read a submitted entry and its attachments, and what
  happens on withdrawal, purpose changes, photo deletion or a revised submission.
- Review status, revision checks, response authorship, any follow-up conversation
  and the effect of administrative lifecycle actions on the five-item limits.
- Presentation of each purpose/outcome, including how proposed alternatives
  refer to another taxon and what happens if its identification remains uncertain.

No implementation is authorized by this note alone. The immediate gallery and
notebook visual cleanup is separate and already deployed.

## Existing implementation and related plans

- [Current contributions, privacy and limits](../apps/native-plants/CONTRIBUTIONS.md)
- [Identity and personal-contribution integration](../apps/native-plants/IDENTITY-INTEGRATION.md)
- [Hedge identity/access-control boundaries](IDENTITY-ACCESS-CONTROL-overarching-plan.md)
