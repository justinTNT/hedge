# Native Plants of Northern Australia

An independent Hedge app for John Brock's plant collection. It lives in
`apps/native-plants`; it does not import Grassophy's species module or data model.

This is a local working prototype. The accepted-view manuscript supplies 531 plant
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
The local `.dev.vars` created for this checkout uses `ADMIN_KEY=native-plants-local`.
This is a development credential only. Remote resources and deployment are not configured.

The default archive location is `~/Desktop/brocky`. To use another location:

```sh
python3 scripts/import-source.py --source /path/to/brocky
```

`db:init` creates missing tables without dropping existing data. `db:seed` uses
`INSERT OR IGNORE`; repeat imports preserve owner edits, publication choices and
soft deletion. Renaming a plant in admin preserves its ID and existing links. The
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
  hero photograph and smaller supporting images. Every photograph opens the viewer.
- Filters for growth form, sun, water, garden features, wildlife associations,
  recorded NT endemism and available photographs. Unknown attributes remain unknown.
- SQLite/D1-backed Plant and PlantPhoto editing through Hedge admin. `Published`
  and soft deletion control inclusion in the public catalogue.
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
source notes identify them; other credits are marked unrecorded. The archive is
read-only. Source text exports, SQL seeds, reports and media derivatives are ignored
by Git; keep the original archive independently backed up.

Imported guide photographs are public static assets. Removing an allocation from
the catalogue does not revoke a known static URL. This storage is unsuitable for
private personal uploads; see the integration note below.

## Identity and contributions

See [IDENTITY-INTEGRATION.md](IDENTITY-INTEGRATION.md). The public catalogue and
owner editing are usable independently. Personal notes/images and delegated
review will build on the shared identity and admin authorization now on main;
this app does not carry a copied login or grants implementation.

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
filter URLs and rejection of late responses after navigation or refresh failure.

No production worker, D1 database, R2 bucket or domain has been created. Before a
review deployment, reconcile source/proof differences, confirm selected content and
credits, configure dedicated bindings/secrets, and review the deployment scope.
