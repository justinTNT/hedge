# Native Plants of Northern Australia

An independent Hedge app for John Brock's plant collection. It lives in
`apps/native-plants`; it does not import Grassophy's species module or data model.

This is a working prototype with a private Cloudflare preview. The accepted-view manuscript supplies 531 plant
accounts in 102 source family labels and 286 genera. There are 504 exact joins to
the attributes table. All matched, unheld photographs from the curated `PICK`
folders, plus explicitly reviewed supplemental selections, are imported with
one hero and a supporting gallery per account.
The source collection still needs comparison with the final proof
and editorial review of photo identity, credits and unresolved names before release.

## Run locally

Requires the repository's .NET/Fable toolchain, Node (22.13+ for the SQLite tests),
Python 3 with Pillow, and macOS `textutil` for the RTF table.

```sh
cd apps/native-plants
npm ci
npm run import
npm run build
npm run db:init
npm run db:seed
npm run preview
```

The app is at `http://localhost:8794`; the Hedge owner admin is at `/admin`.
For a fresh checkout, copy `.dev.vars.example` to `.dev.vars`, set your local
`ADMIN_KEY`, and generate independent random `GUEST_SECRET` and `OAUTH_SECRET`
values of at least 32 bytes each. Keep that file untracked. Existing checkouts
should preserve their configured secrets. The separate private Cloudflare preview is at
https://native-plants-preview.justin-8ee.workers.dev. See [PREVIEW.md](PREVIEW.md)
for its password file, independent admin key, OAuth callbacks and deployment commands.

The default archive location is `~/Desktop/brocky`. To use another location:

```sh
python3 scripts/import-source.py --source /path/to/brocky
```

`db:init` creates missing tables without dropping existing data. `db:seed` uses
`INSERT OR IGNORE` for source records. Explicit reviewed media corrections additionally
use conditional updates: crop replacements require the previous asset URLs, map
reclassification/supersession is marked once, transparent GIF map replacements require
the reviewed PNG URL, and new credits fill only the unrecorded
placeholder. These preserve captions, ordering, soft deletion and unrelated owner
edits; later owner publication choices survive another import. Renaming a plant in admin preserves its ID and existing links. The
source-derived ID represents the original account, not its current display name.

When source files change, the importer writes `data/source-change-report.json` and
stops. Review changed names and account identities before accepting a new source
baseline with `--accept-source-change`. Existing records still require a deliberate
editorial update; accepting a source version does not overwrite saved records.
For a reviewed source-name change, an `accountIds` mapping in
`data/editorial-decisions.json` can associate the new source name with the existing
account ID. Do not create a second account merely because its name changed.

## What works

- Keyboard-operated typeahead over scientific, common and explicitly recorded
  former names. Search runs locally over the published name/card catalogue.
- Family and genus drilldown, shareable URLs and botanical account pages with a
  hero photograph and smaller supporting images. One or two supporting photos sit
  beneath the title and facts beside the hero; larger sets use a full-width gallery.
  On narrow screens, supporting photos follow the hero. Photos with both dimensions at least
  200px open the viewer; smaller insets remain inline.
- Filters for growth form, sun, water, garden features, wildlife associations,
  recorded NT endemism, available photographs and photographer. The photographer
  filter includes supporting images and combines with the botanical filters.
  Unknown attributes remain unknown.
- SQLite/D1-backed Plant, PlantPhoto and PlantMap editing through Hedge admin. `Published`
  and soft deletion control inclusion in the public catalogue.
- Source glossary definitions on hover, keyboard focus or tap throughout account
  text, with complete searchable `/glossary` and `/references` pages. Numbered
  Aboriginal-use citations expose their full entries; unresolved citations stay
  explicit. GlossaryTerm and SourceReference are editable through Hedge admin.
- Generated schemas, codecs, API clients and route bindings. Botanical query and
  presentation code is app-owned.

Fields containing several facet values use `|` separators in storage. These become
ordinary typed lists in the catalogue. Photos have explicit credit, caption, order
and publication fields. `SourceEvidence` is private editorial metadata and is never
included in public API responses.
The first published photograph by sort order becomes the hero; change `SortOrder`
in PlantPhoto admin to choose it. Galleries have no fixed five-image limit.

## Persistence and in-memory queries

`Models.Domain` defines editable storage. `Models.Api` defines public records.
`Core/Catalogue.fs` supplies pure ranking/filtering and taxonomy functions.
`Server/CatalogueStore.fs` constructs the immutable catalogue and ID map from one
transactional D1 batch. Public detail and query endpoints use that catalogue;
there is no botanical SQL query per filter, result or keystroke.

Each database binding has its own disposable cache. It reloads on demand after
15 seconds. A successful owner-admin mutation invalidates that isolate immediately.
An in-flight old load cannot overwrite a later invalidation. Expired loads that fail
return an error rather than continuing to serve an obsolete publication state.

The browser checks a small content revision every 15 seconds while visible and
when returning to the tab. It downloads the card catalogue/current account only
when the content changes. Revisions are hashes of the complete public accounts,
so body-only changes are detected too. Under successful online requests, changes
normally appear within 30 seconds across active instances/tabs, plus request time.
Background tabs refresh on return. Client request IDs reject superseded responses.

## Source and media discipline

The importer reads DOCX XML using the accepted revision view: inserted text is
included, deleted/moved-from text is excluded. RTF attributes are extracted from
real table cells via `textutil`. Only exact name joins are applied; unmatched rows
and unknown codes appear in `data/import-report.json`.

Photos are selected from the curated plant-description `PICK` folders, outside
`EXTRA/EXTRAS`, with account/folder/filename agreement after normalizing spaces,
periods and underscores consistently. Rank words and epithets remain significant:
subspecies are not automatically matched to species-only labels. Unmatched folders
and conflicting filenames are reported, including the affected paths.
Every eligible photograph is imported;
the source ordering determines the initial hero and supporting order. Re-importing
preserves saved ordering, captions and publication choices. Editorial holds and their resolutions live in
`data/editorial-decisions.json`. The Grevillea mimosoides hold was resolved against
the corrected book Y layout; see [PHOTO-REVIEW.md](PHOTO-REVIEW.md).
Those checks establish documentary evidence, not botanical verification.

`photoNameAliases` in that same decisions file records reviewed archive spellings
and their evidence without changing the manuscript name, account ID or public
taxonomic aliases. For example, the Abelmoschus photo folder's `tuberosa` spelling
is explicitly allocated to the manuscript's `tuberosus` account.
`photoAllocations` records individual supplemental files (including selected
`EXTRA` or photographer-collection images), their account, credit, SHA256 and
selection/exclusion reason, and an optional public `caption`. A changed file stops import for review. These entries
do not enable wholesale import of another archive folder. The Abelmoschus
selection includes fruit/seeds and Willie Burgess photographs; an alternate crop
and a near-duplicate flower view remain excluded with reasons recorded.

Reviewed photographs from the 2022 edition's proof are reproducible from
`data/book-photo-extractions.json`. It records the source PDF hash, page/image
object, native-pixel crop, output hash and account evidence for every selection.
The source is the single-page proof in `HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL
DRAFT` beneath the Brock archive. With Pillow and pypdfium2 installed, run
`python3 scripts/extract-book-photos.py` before `npm run import` to recreate the
lossless PNG files in the adjacent `.extracted` directory. Existing extraction
files are hash-checked, never overwritten. Composite book images are split into
individual photographs; their native resolution is retained, including small
insets. The normal importer produces WebP derivatives without enlargement.

Species-level photographs may illustrate a subspecies/variety account when
explicitly approved in `photoAllocations`. Their `caption` keeps the species
binomial; the account name and description keep the full taxon. The nine accounts
approved on 24 September are listed in `speciesLevelPhotoPolicy`. This records
permission to illustrate, without asserting the finer identification. Captions
appear beneath the hero, gallery photographs and enlarged photograph, and remain
editable through the photo admin. An allocation without `caption` retains the
existing default of the account's full name.

The September media review also covers selected `SELECT`, `PICKX`, annotated
`PICK (...)` folders and newer field-trip collections. Those folders are not
automatically equivalent to `PICK`: each addition is an explicit reviewed file.
See [PHOTO-REVIEW.md](PHOTO-REVIEW.md) for scope, results and outstanding decisions.

Run `npm run audit:photos` after importing to scan the whole supplied archive for
full-name and rank-only filename candidates. It writes private
`data/photo-audit.json` and `data/photo-audit.md`, including every unillustrated
account. This is an inventory of the source import, not live admin state; it never
imports candidates. Maps, repeated exports, uncertain labels and unverified
subspecies remain editorial decisions. Absence of a filename match does not prove
that a photograph is absent from the archive.

The importer makes 640px and 1600px WebP derivatives, retaining binary hashes and
allocation evidence locally. Photographer initials are expanded only where the
source notes or the supplied contributor index identify them; unknown credits remain
unrecorded in storage and are omitted from public captions. Original source files are never modified. Reviewed extractions are written beside
the local proofs, and normalized maps are collected under `Distribution maps`. Source text exports, SQL seeds, reports and media derivatives are ignored
by Git; keep the original archive independently backed up.

Imported guide photographs are public static assets. Removing an allocation from
the catalogue does not revoke a known static URL. This storage is unsuitable for
private personal uploads; see the integration note below.

## Distribution maps and photographer credits

See [MAP-REVIEW.md](MAP-REVIEW.md) for sources, normalization, allocated/held maps
and reproduction commands. The current local collection has 289 published maps:
271 from the 2022 proof and 18 standalone 2026 maps. Six replaced 2022 maps remain
unpublished in admin. Maps appear inline with the distribution text and source
caption, as transparent GIFs at half width without padding or enlargement. Their
reviewed source PNGs remain intact. Maps are independent of photo heroes and
photographer filters.

Photographer names remain editable on PlantPhoto. The immutable catalogue derives
each plant's distinct photographers from its published, undeleted photos; facet
counts count plants, not photo rows. Filtering runs through the same pure app-owned
catalogue functions in the client and Worker. [PHOTO-REVIEW.md](PHOTO-REVIEW.md)
records the contributor initials and current coverage. No identity/auth linkage is
inferred from a photo credit.

## Glossary and references

The accepted-view backmatter DOCX supplies all 95 glossary definitions, 25 numbered
plant-use references and all 120 bibliography paragraphs. Their full text is retained;
see [GLOSSARY-REFERENCES.md](GLOSSARY-REFERENCES.md) for source gaps and matching rules.
The 105 account-level `Ref:` fields remain intact. Glossary and reference records
join the same bulk catalogue snapshot and content revision, so owner edits,
publication and deletion update the public guide through the existing refresh path.

`scripts/extract-glossary-illustrations.py` reproduces the labelled flower diagram
from the local 2022 proof; the other glossary illustration comes directly from the
backmatter DOCX. Run this extractor before the normal importer when recreating an
archive from originals. Its reviewed crop and hashes are in
`data/glossary-illustrations.json`. All inputs remain local after volume disconnection.

## Identity and contributions

Native Plants composes Hedge's shared identity and optional curator grants. Species
pages support private notes, correction flags, photo uploads, personal heroes and
photo offers. `/review` lets the owner or granted curators mark corrections
read/unread and select offered photos for public species galleries.

Notes and photo features require a verified Google/GitHub sign-in. Anonymous
visitors browse the guide and see the top-right Login control; species pages do
not load or display a personal notebook or photo controls. Both local and preview
configuration use `CONTRIBUTIONS_REQUIRE_LOGIN=true`, enforced on personal reads,
writes and private media. Older anonymous contributions are retained and claimed
on login. Each user may keep five notes and five personal photos per species;
attempting to add a sixth explains the limit and asks for an existing item to be
deleted. A camera-plus gallery tile handles uploads; field notes have compact
edit/delete icons and subtly inset text.
Reviewing a correction or promoting a photo does not release a personal slot.
See [contribution implementation and checks](CONTRIBUTIONS.md).

Fresh databases: `npm run db:init`. Existing catalogue-only databases first apply
`npm run db:migrate:identity`; identity-enabled databases then apply
`npm run db:migrate:contributions` once. Both migrations are additive. The local
preview has both migrations and preserves all original botanical records.

### Configure sign-in

Copy the missing settings from `.dev.vars.example` into your untracked `.dev.vars`.
The current checkout already has independent randomly generated `GUEST_SECRET` and
`OAUTH_SECRET` values and `ENVIRONMENT=development`; preserve those values. Local
provider credentials are separate from the Cloudflare preview, where the owner
has configured and successfully tested Google sign-in. GitHub remains optional.

| Provider | Variables | Local callback |
| --- | --- | --- |
| Google | `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET` | `http://localhost:8794/api/auth/google/callback` |
| GitHub | `GITHUB_CLIENT_ID`, `GITHUB_CLIENT_SECRET` | `http://localhost:8794/api/auth/github/callback` |

Configure those callback addresses in the respective OAuth clients, then restart
`npm run preview`. `/api/auth/providers` only offers fully configured providers;
with none, Login explains that sign-in is not available yet. No fake provider or
development authentication bypass is shipped. Provider cancellation can be retried
by returning to the guide and choosing Login again.

Use `http://localhost:8794` for OAuth testing. A different port/host requires its
own matching callback configuration. A deployed app needs the equivalent HTTPS
callbacks and separately provisioned secrets; it must not use the development
environment flag. `GUEST_KEY_ID`/`GUEST_KEYRING` support the framework's existing
signing-key rotation. Do not reuse the admin key as a signing secret.

Browser logout clears the signed cookie and presentation state without disconnecting
the provider account on other devices. The shared runtime serializes session reads
and logout, using Web Locks across tabs when available, and invalidates late reads.
It does not implement server-side revocation of a copied cookie. Contribution APIs
use the centralized app ownership policy described in [CONTRIBUTIONS.md](CONTRIBUTIONS.md);
`AuthConfig.subject` remains verified-only. Private writes carry an opaque viewer
token so a draft cannot silently be saved under a changed account.

## Checks

```sh
npm test
```

The focused checks exercise the compiled Worker over real in-memory SQLite:
owner authorization, edit/unpublish/delete behavior, private metadata exclusion,
photo/parent visibility, facet semantics, aliases, bulk-loading and cache isolation,
expiry failures, racing invalidation and preservation of edits on re-import.
Importer fixtures cover tracked revisions, inline labels, unknown codes, rank
punctuation, reviewed aliases, unmatched folders, conflicting photograph labels
and hash-pinned supplemental selections. Client checks cover keyboard selection, shareable
filter URLs, the minimum-size enlargement rule, silent unknown credits and rejection
of late responses after navigation or refresh failure. Identity checks exercise both
OAuth callback flows with simulated provider responses, same-account adoption,
anonymous/deleted-identity filtering, same-origin logout, safe return navigation,
private-prefix protection and disabled anonymous uploads. Real provider sign-in
was confirmed by the owner for Google on the private preview. GitHub still needs
credentials and a live round trip. Map checks cover source labels,
publication, deletion and exclusion from photo heroes; photographer checks include
non-hero matches and distinct-plant counts. Conditional correction fixtures preserve
owner captions, image replacements and later publication decisions on re-import.

An isolated, password-protected Cloudflare preview was deployed on 25 September
2026. It has its own D1 database, private R2 bucket and signing/admin secrets.
See [PREVIEW.md](PREVIEW.md) for access, privacy and live verification. Public
release still requires editorial review of source/proof differences, selected
content, credits and unresolved names.
