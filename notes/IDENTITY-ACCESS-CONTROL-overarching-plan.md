# Identity and access-controlled admin: implementation handoff

Date: 23 September 2026. Audience: the alerts/access-control implementation agent.
Status: overarching implementation plan following the owner's discussion; this handoff makes no code changes.

## Outcome and sequence

Extract the existing identity feature into a reusable module, retain the new general role-checking
primitive, and extend the shared admin to support bounded delegated work. Use Microblog/Idealist as
the first working consumer throughout. Validate the second use case with a small Native Plants
fixture; building the botanical app is not a prerequisite.

The intended sequence is **finish and merge the existing curator feature → branch from main →
identity extraction → shared admin authorization → integration into Native Plants**. Alerts already
provides the first editorial workflow. Microblog, Justat and NDCT already provide enough real host
variation to drive identity extraction.

Success means a new app can compose identity and delegated admin access without copying Microblog's
identity schema, SQL, handlers, login plumbing or grant lookup. The app author supplies its resources,
composition and domain permissions. The deployment author supplies secrets and presentation choices.

This is a bounded consolidation of existing behavior plus an admin capability with two known uses.
Do not turn it into a general permissions language or a new application/plugin architecture.

## Existing plans and the change in direction

Read these as supporting material:

- [Identity investigation](IDENTITY-MODULE-investigation.md): ownership, lifecycle and compatibility.
- [Identity consuming-host example](IDENTITY-MODULE-before-after.md): extraction scope, attribution,
  generator implications and the previously included key-rotation work (Slice G).
- [Curator plan](ACCESS-CONTROL-curator-plan.md) and [its review feedback](../plan-feedback-22-sept.md):
  role semantics, state-guarded alerts operations and deployment considerations.
- [Signed guest-cookie plan](guest-uploads-signed-cookie-v2.md): the established credential/upload
  boundary; preserve it during extraction.
- [Native Plants plan](NATIVE-PLANTS-investigation-plan.md), especially “Personal notes, contributed
  images and correction review”: the second consumer's requirements.
- [Module ownership](MODULES.md) and [unified shell](UNIFIED-SHELL.md): hosted components, one identity
  authority per document, and app-owned composition.

After the baseline merge in Phase 0, this plan advances identity from investigation to implementation
and supersedes the curator plan's deferral of role-aware common admin. Finish the current curator
branch against its existing scope first. The owner-key experience remains supported; delegated
access is an explicit opt-in. The dedicated curator page need not be removed to deliver the shared
capability.

Keep the original decisions on provider-account grants, per-request revocation, injected module
authorization and approved-entry freezing. A shared admin must preserve those domain constraints.

The older notes are design artifacts, not exact descriptions of today's code. In particular, the
implemented Grant timestamp is `CreatedAt`, and `UniqueTogether` is table metadata: do not invent a
`FieldAttr`/`SchemaCodec` change unless the actual representation requires it.

## Starting point: preserve work already done

The architectural review covered `6a2d72c..f33bf9d` on `access-control-curator`. While preparing this
handoff, HEAD advanced to `f14637e`, which addresses the reported save race, curator image upload and
current-identity decoding bugs. Inspect the current branch tip before working; verify the fixes
rather than assuming either that they are still missing or that every adjacent case is covered.

At inspection, `main` is `466edd6` (the merge of `alerts-module`), `alerts-module` has no commits outside
main, and `access-control-curator` is 11 commits ahead of main with no main-only commits. Thus only
the curator branch remains to land. Recheck these facts when performing the merge.

Keep these existing pieces:

- `Hedge.AccessControl`: accepted session → active account subject → enabled role grant.
- The `(provider, provider_user_id, role)` unique grant key, `Enabled` revocation and cookie renewal
  carried through both successful and denied authorization outcomes.
- `Alerts.Services.AuthorizeCurator`, evaluated when called with a request; cron construction stays
  request-free and does not require guest-signing configuration.
- Alerts-owned queue/API/SQL, targeted state-guarded mutations and the existing publication path.
- `ModuleServices.idealist` as the app composition point joining authorization, alerts and blog.
- The generated alerts client and codecs, which exist but are bypassed by the current curator UI.

Current shared admin authorization is still `CheckKey : request -> env -> bool`. It supplies neither
an authenticated subject nor a resource/action decision. Changing that boundary is real framework
work; passing `hasRole "curator"` into the existing all-or-nothing gate would grant too much access.

## Responsibility boundaries

| Owner | Responsibility |
| --- | --- |
| Hedge framework | Schema/code generation, HTTP contracts, generic admin enforcement, signed-cookie/session mechanisms, OAuth protocol mechanisms and the reusable role guard. |
| Identity module | Shared identity schema and persistence, account lifecycle, active-subject resolution, identity API/client state, default controls/styles and identity-specific OAuth completion behavior. |
| Optional grant support alongside identity | Shared Grant model, persistence and role-service binding. Sites choose whether to enable it; apps do not copy grant SQL. It can be a component of the identity package without becoming a separate plugin system. |
| Content/domain modules | Their own records, attribution statements, permitted state transitions, validation, targeted operations and reusable feature UI. Alerts owns what approving a post means. |
| App/host | Resources, provider configuration, module selection, attribution composition, role-to-operation policy, navigation, identity placement and wiring of admin contributions. |
| Shared UI libraries | Editor and common controls, with explicit upload/lifecycle dependencies. |
| Deployment | Secrets, environment configuration and tenant styling through documented hooks. |

Identity lives for the document's lifetime. Do not force it through content-page activation and
deactivation. The host owns its instance and signals; the module owns its implementation.

Keep identity independent of Blog/Articles table names. Content modules supply their attribution
operations through host composition. Preserve existing D1 statement batching when moving that code.

The framework must not contain checks for role names such as `curator`, or knowledge of species,
corrections, alerts fields or publication states. Those choices belong in app/domain policy.

## Phase 0 — finish and merge the current feature

Establish the working alerts/curator feature on main before starting identity extraction. This gives
the new work a known behavioral baseline, keeps its review separate from feature delivery, and makes
later regressions easier to identify.

1. Reconcile the latest curator commits with the functional review. Reproduce the reported cases and
   adjacent draft-lifetime cases; finish any correctness fixes needed for the existing feature.
2. Regenerate the affected artifacts, run the current repository gate and targeted curator checks,
   and verify the owner/curator login, authorization, upload and editorial flows. Record any live
   integration checks that remain unperformed; do not infer them from unit tests.
3. Review and merge `access-control-curator` into current `main`. `alerts-module` is already included
   at this inspection; merge it separately only if it has acquired genuinely unmerged work. Preserve
   unrelated working-tree changes and do not bundle them into the merge.
4. Record the resulting main commit and start a fresh branch, for example `identity-access-admin`,
   from it. A separate checkout/worktree is useful if the current checkout has unrelated work.

Keep this merge's scope bounded. Identity extraction, role-aware common admin and the broader client
ownership cleanup belong to the new branch. Do not make those architectural improvements a condition
for landing a functioning implementation of the original curator feature. Correctness fixes needed
for that feature do belong before the merge.

Treat deployment and migration as explicit rollout steps distinct from the Git merge. Carry forward
the original curator rollout requirements, including applying grants migrations before code that
queries them on each affected deployment.

**Exit:** main contains the verified alerts/curator implementation, the baseline SHA is recorded, and
the identity/admin work starts from that baseline on its own branch.

## Phase 1 — extract identity with existing hosts

Start by updating the consuming-host example against the current branch, including grant binding
and the standalone curator login. Establish a small integration surface before relocating files;
the existing design is a starting point, not a requirement to preserve its illustrative signatures.

Implement the shared package, provisionally `packages/modules/identity`, using explicit dependencies
and module-owned generated artifacts where appropriate. Reuse the existing shared identity client
and CSS. Resolve the generator's app/root identity slice with the smallest compatible extension;
verify schema output rather than assuming a package move is schema-neutral.

Migrate Microblog first, then the Articles app's Justat and NDCT compositions. Include an identity-only
host fixture with no attributed content. Preserve provider-free anonymous use by existing content
hosts; “no OAuth providers” does not mean “no identity feature”.

Expose authenticated-subject resolution separately from role checking. An ordinary authenticated
user must be able to own contributions without first receiving a grant. Keep role checks layered on
that identity result, and retain the subject for ownership checks and future attribution by callers.

Move grant persistence/binding into shared optional support while retaining Microblog's existing
`grants` data and contract. Adding identity to another app should not silently enable delegated
admin. Document which schema slice is selected when grants are enabled.

Consolidate login/return integration. Replace accumulating page-name tests such as the `/curator`
suffix with an explicit supported return/activation policy, preserving the existing blog claim flow
and curator document navigation. Continue validating OAuth state and allowed return destinations.

Compatibility requirements:

- Preserve existing `guests`, `identities`, and enabled `grants` storage, keys and data; preserve
  `IdentityRef` joins and existing external API/admin identifiers or provide deliberate adapters.
- Preserve cookie names, signing/audience policy, session behavior and the signed upload path.
- Preserve activation, switching, adoption, merge/fresh, disconnect and deletion semantics. Grant
  keys remain provider-account pairs and survive identity-row merges.
- Preserve each host's attribution participants and transaction boundaries. Do not execute separate
  per-module callbacks where one D1 batch currently owns the writes.
- Preserve the owner-key admin path and keep role support opt-in for other apps.

The prior identity plan explicitly includes graceful key rotation (Slice G). Carry it forward as a
separately reviewable part of this phase: shared keyring configuration, re-signing a valid retiring-key
cookie on use, retirement enforcement and single-secret compatibility. Check what has already landed
before implementing it. Packaging itself must not force a production secret rotation or session reset.

**Exit:** Microblog, Justat and NDCT use one identity implementation; an isolated host can consume it
without app-namespace shims; schema/session/attribution checks pass. Document any remaining intentional
host policy. The goal is removing copied behavior, not merely moving file names.

## Phase 2 — bounded authorization in the shared admin

Extend the generic admin's contract so the server can make an asynchronous decision for the requested
resource and operation, with the authenticated subject and cookie renewal available. Keep an adapter
for existing owner-key-only configurations. Authentication, permission and domain validation remain
distinct decisions.

Support the demonstrated requirements, choosing the smallest typed contract that does so:

- Owner access continues to work with `ADMIN_KEY`.
- Delegated users see and invoke only explicitly permitted admin resources and operations. Unlisted
  permissions are denied. Owning a role does not implicitly enable generic CRUD across the database.
- Resource listing, type/schema discovery, individual reads and writes all follow authorization.
  Hiding a table/button in the client is not enforcement; direct API calls must receive the same result.
- Grant management and sensitive identity-management operations remain owner-only unless explicitly
  delegated by a future policy. A curator must not be able to grant themselves more powers.
- Limited editorial actions can use scoped reads and targeted writes. Do not expose whole-row updates
  merely because a user may edit one field or perform one transition.
- Module/domain handlers retain state checks. The admin must not offer an alternate CRUD route that
  bypasses the approved-entry freeze or another module's validation.
- Preserve 401/403 distinctions and renewal cookies. Revocation is checked on the next protected
  request; a cached menu or previously loaded page grants no authority.

Allow modules/apps to contribute bounded operations to the shared admin. Decide whether a small
action descriptor or a hosted feature component is the smaller implementation after comparing the
two consumers. The generic shell owns authorization-aware navigation and common presentation;
the domain retains the action handler. Do not hardcode an alerts workflow in `Hedge.Admin`.

For example, an alerts curator can read the undecided queue, edit title/snippet/owner comment,
approve and dismiss. That does not imply permission to delete arbitrary alert records, change feed
configuration, edit grants, or update every column of an approved entry.

Use a short permission matrix as implementation evidence: owner, anonymous, authenticated with no
grant, enabled curator, revoked curator; allowed resources, reads and mutations for each. A compact
record of policy functions or explicit mappings is sufficient. No role hierarchy or policy language
is needed.

**Exit:** shared admin authorization works in the real Idealist composition, with server enforcement
and matching UI. Existing owner-only apps still work without adopting roles.

## Phase 3 — finish the alerts integration and client boundaries

Drive alerts' delegated admin operations through its existing module-owned API/handlers, sharing the
same domain operations if an additional adapter is needed. Approval still means approval for later
cron publication; approved entries remain frozen. Preserve atomic feed insertion/promotion recording.

Use `Alerts.ClientGen` and generated codecs. Remove duplicate wire records, handwritten endpoint
construction and JSON parsing where the generated contract already provides them. The current local
raw-fetch workaround identifies a shared transport obligation: completed 401/403/409 responses must
retain their status instead of becoming network failures. Verify/fix that shared boundary first.

Keep the app responsible for the document entry, navigation, identity placement and branding. Any
queue/editor behavior reused by the dedicated page and admin belongs with alerts, with its baseline
styles. Do not build a large reusable curator component solely to discard it immediately: choose one
implementation for the retained surfaces. The existing page may remain as a compatibility surface.

Consume the shared identity client/contract rather than reimplementing provider/session decoding.
Configure the shared rich-text editor through its supported upload capability. Preserve working
guest uploads; a delegated editor must not need the owner's admin key.

Reproduce the cases behind the review and cover the current implementation:

- Save A, edit B, then receive A's result: B's draft survives.
- Save A, then edit/reopen A before its result: newer edits are not silently closed as “saved”. Use
  operation/draft identity or deliberately prevent the conflicting interaction.
- Identity and queue responses can arrive in either order; the signed-in name remains correct.
- A curator without an admin key can use the supported image-upload control under root and subpaths.

**Exit:** Idealist demonstrates the complete shared identity + delegated admin path. There is one
implementation of alerts' editorial rules, with generated wire contracts and bounded UI ownership.

## Phase 4 — prove the Native Plants requirement without building the app

Native Plants is a separate app. Grassophy is prior proof of feasibility, not a requirement to share
species models, product UI or workflow code.

Use a small fixture or integration host with representative species, note and image records:

| Actor/capability | What the shared contracts must accommodate |
| --- | --- |
| Authenticated user | Own species notes and images without an editorial grant. |
| Contribution owner | Mark a note as a correction or an image as public through app-defined operations. |
| Authorized reviewer | Review corrections and mark them read/unread through the shared admin. |
| Authorized reviewer | Promote eligible contributed images onto a species page through a domain action. |

Exercise user A, user B, a reviewer and the owner. Verify that A cannot edit B's personal records;
review access is scoped to the contributions intended for review; private notes do not become broadly
readable through an admin list/get route. Preserve ownership, species association and image credit.
Reading a correction does not itself edit the source species account.

These are application rules exercised through shared identity/admin contracts. Keep contribution
ownership distinct from grants and from a browser guest ID. Record the ownership subject chosen by the
fixture and how switching/merging identities affects it; do not assume the grant key automatically
settles ownership for every domain.

Include the media boundary in this check: possession of an upload credential does not itself provide
private storage or authorized reads. Do not treat today's guest-comment upload URL as proof of private
personal-image support. Demonstrate protection for any fixture media declared private; if production
upload/serving support needs an extension, identify a concrete follow-on with an explicit interface
and mark that capability incomplete until implemented.

Do not settle public-image visibility timing, per-reviewer read tracking, withdrawal policy or the
full publication/storage design here. Let fixture policy supply eligibility while proving that the
framework can enforce it. Do not import the botanical archive or build its catalogue/search pipeline.

**Exit:** a second consumer uses the same identity and admin authorization surfaces without importing
Microblog or Alerts namespaces. Record exactly what the fixture proves and what remains app work.

## Verification and rollout

Use targeted behavioral checks alongside the repository's generated/build gates. Extend existing
fixtures where possible. Add the new module's generation/schema/host checks to the maintained gate;
do not rely on a one-off successful build that omits its generated artifacts.

Required evidence:

- Identity lifecycle, zero/single/multiple attribution participants, preserved batch boundaries,
  stable grant lookup across merges, and deletion/disconnect/anonymous-active denials.
- Enabled/revoked grants, unknown roles, owner compatibility, cookie renewal on denials, and safe
  construction of request-free cron services.
- Direct admin API attempts against unauthorized lists, schemas, records, fields and actions, including
  grant self-escalation and attempts to bypass alerts' state constraints.
- Generated schema and migration equivalence for each affected composition; stable `IdentityRef`
  targets; no accidental schema/feature leakage into non-opted-in deployments.
- Fresh builds and `./test.sh`, including default Microblog, Idealist, Justat and NDCT outputs. Regenerate
  module-owned and site-specific artifacts using the commands applicable to the new arrangement.
- Browser flows at root and `/st`: owner login, delegated login/return, no-grant/revoked states,
  identity switching, supported uploads, draft races and constrained editorial operations.
- Key-rotation fixtures plus the controlled integration check required by the existing Slice G plan.
  Report live OAuth/rotation checks separately from fixtures; do not claim them from mocked results.

Prefer commits that separate compatibility extraction, key rotation, admin contracts and consumer
integration. Keep the working applications usable between stages. Inspect the working tree and branch
tip before editing; unrelated notes and other agents' fixes may be present.

For rollout, distinguish package relocation from actual schema changes. Preserve deployed table names
where possible. If a composition newly enables grants or otherwise gains schema, generate/review its
migration and explicitly apply it before code that queries it. `migrate:remote` generates a migration;
it is not the apply step. Enumerate the affected deployments instead of assuming Idealist is the only
one. Keep existing secrets valid through extraction and document any optional keyring transition.

## Completion and scope limits

The handoff is complete when the agent can demonstrate:

1. Microblog, Justat and NDCT consume shared identity without copied identity behavior.
2. Grant storage/binding is reusable and retains the established role/session semantics.
3. The shared admin supports bounded delegated work, preserves owner-key use, and enforces the same
   permissions on direct API requests as in its interface.
4. Alerts uses that path without weakening its domain rules or duplicating its wire contract.
5. The Native Plants fixture proves ownership plus editorial delegation using the same contracts;
   any remaining media capability is explicitly accounted for.
6. A short host/deployment guide shows what a new app supplies, and verification results identify
   real checks, outstanding integration work and any rollout obligations.

Defer role inheritance, groups/organizations, arbitrary policy expressions, a general moderation
engine, alternative identity backends, a new module lifecycle system and a comprehensive audit UI.
Retain subjects/provenance where needed without building those systems in anticipation.

The deliverable is a smaller, reliable consuming surface for the apps we have and the next app we
have described. Assess each proposed abstraction against that outcome.
