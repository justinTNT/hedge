# Archive photograph review — 23–24 September 2026

## 24 September: high-confidence archive additions

Added **520 photographs across 293 accounts** from the Crucial X10 report's
**A — strong documentary association** loose-photo shortlist. There are now
**1,864 published photographs across all 531 accounts**. The 289 published
distribution maps remain separate from the galleries. These totals supersede
the earlier counts below.

All 521 A-grade loose candidates from the missing-photo and gallery-expansion
reports were checked against their local Brock archive copies and source SHA256s.
The previously reviewed shortlist was compared with the current galleries,
including the recently imported proof images. One further fruit-cluster view of
*Aidia racemosa* (E050-10) was excluded as a repeat of an existing view; the other
520 were added. B-grade supported associations and C-grade unresolved leads remain
for later decisions. Earlier book-image additions were not imported again.

The permanent audit is `data/volume-photo-import.json`: each candidate's report
reference, source path/hash, dimensions, association evidence, selection notes,
caption, credit and inclusion/exclusion decision. Corresponding individual
allocations are in `data/editorial-decisions.json`. The original volume hierarchy
is preserved under `~/Desktop/brocky`; no external volume is required.

Six photographs retain species-level captions where their filenames omit the
account's subspecies/variety, even though the full-rank source folder supports the
association. No botanical identification or taxonomic equivalence is inferred.
206 added photographs have explicit photographer credits; the other 314 remain
silent. Credits use known filename initials, named photographer collections and
explicit metadata. The Aidia fruit-dove photograph is credited to Leigh Patterson:
its LP filename and EXIF Artist/Copyright take precedence over the Ian Morris
collection containing it. R. A. Kerrigan is retained as named in the source file.

New photos follow the existing gallery rather than displacing its hero. All 531
existing plant records, 1,352 existing photo records (including unpublished maps),
295 map records, glossary entries and references were byte-for-byte unchanged at
the record level after the insert-only seed. All 293 affected public responses
were checked for unchanged hero/order followed by their added photos, correct
captions and credits, and absence of private source evidence. All 1,040 new served
WebPs were byte-compared with generated assets; source dimensions were respected.

Validation: 43 app/importer tests and the site build passed. The additions are in
the local preview; no remote deployment occurred.

## 24 September: photographs recovered from the 2022 proof

Added **247 photographs across 175 accounts**, including **34 photographs for
all 24 previously unillustrated accounts**. All **531 accounts are now
illustrated**, with **1,352 gallery entries** (this total includes existing map
images). These figures supersede the historical coverage counts below.

The source is `HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/PROOF Native Plants
of N Australia-PDF.pdf`, the 2021 proof of the 2022 edition, copied from the
external volume to the Brock archive. Of the 185 reviewed image objects, 54 were
composites requiring individual panel crops. The 249 proposed panels were
visually reviewed; a blank panel and a small duplicate crop of the existing
Eucalyptus camaldulensis fruit photograph were excluded. No distribution maps
were added in this photograph batch.

Each retained panel has explicit page, object index, crop bounds, output hash
and account evidence in `data/book-photo-extractions.json`, plus a hash-pinned
allocation in `data/editorial-decisions.json`. The reproducible extractor is
`scripts/extract-book-photos.py`. Native pixels are saved as lossless PNG under
the archive's adjacent `PROOF Native Plants of N Australia-PDF.extracted`
directory. Some proof images and insets are small: they are neither enhanced nor
upscaled, and should be inspected at that quality.

Credit is John Brock, following the proof's copyright and photography statement;
the specifically credited exceptions are outside this selection. Species-only
proof evidence for Avicennia marina, Eucalyptus camaldulensis and Trichodesma
zeylanicum retains a species-level public caption on the more specific account.
Other allocations record full-rank account evidence from the proof. Documentary
matching is not independent botanical identification.

Existing photographs keep their order, hero choices, captions and publication
state. New photographs follow the existing gallery; previously empty galleries
start with their largest recovered panel. All 531 pre-existing plant rows and
1,105 pre-existing photo rows were verified unchanged after the insert-only seed.

Validation: all 27 app tests and the site build passed. Every extracted PNG hash
was verified; all 175 affected live account responses and all 494 image/thumbnail
URLs were checked successfully. Derivatives do not enlarge the extracted images.

## 24 September: species-level illustrations approved

The owner approved using the candidate photographs on the nine remaining
subspecies/variety accounts, with public captions at **species level**. Account
headings and descriptions keep the full subspecies/variety name. Photo captions
appear beneath the hero, each gallery image and the enlarged photograph.

Reviewed 44 candidates and selected **30 photographs across all nine accounts**,
retaining complementary flowers, fruit, seeds, leaf surfaces, habit, aerial roots
and wildlife views. Fourteen duplicate copies, alternate exports and repeated
views remain excluded with individual reasons. There is no fixed gallery cap.

| Account | Photographs | Public photo caption |
| --- | ---: | --- |
| Alstonia spectabilis subsp. ophioxyloides | 1 | Alstonia spectabilis |
| Antiaris toxicaria var. macrophylla | 3 | Antiaris toxicaria |
| Crotalaria cunninghamii subsp. cunninghamii | 5 | Crotalaria cunninghamii |
| Ficus virens var. virens | 7 | Ficus virens |
| Ilex arnhemensis subsp. arnhemensis | 3 | Ilex arnhemensis |
| Leptospermum madidum subsp. sativum | 3 | Leptospermum madidum |
| Lophostemon grandiflorus subsp. riparius | 3 | Lophostemon grandiflorus |
| Syzygium forte subsp. potamophilum | 3 | Syzygium forte |
| Trichodesma zeylanicum var. zeylanicum | 2 | Trichodesma zeylanicum |

The user's observation that these ranks may represent the locally familiar
expression of a species, particularly Alstonia spectabilis subsp. ophioxyloides,
is recorded as editorial context. The subspecies/variety shown in each image
remains unverified. The species-level caption intentionally makes the difference
from the account title visible to reviewers.

Each selected file has a hash-pinned allocation and explicit `caption` in
`data/editorial-decisions.json`. The importer preserves that caption rather than
automatically borrowing the account's more specific name. Existing photo captions,
hero choices and admin edits are preserved by the insert-only seed.

Coverage is now **507 of 531 accounts**, with **1,105 photographs**. The remaining
**24 unillustrated accounts** have empty folders referring to the 2022 edition:
23 say to use its photographs, and Cartonema parviflorum names NPNA 2022 p.145.
The Desmodium folder uses the spelling `heterocarpum` for the account's
`heterocarpon`. These findings supersede the rank-review blanks in the earlier
23 September audit below.

Validation: all 27 app tests passed, including a regression check that follows an
explicit species caption through candidate selection, generated catalogue and
SQL seed. Import, audit and site build passed. All 531 existing plant rows and
1,075 existing photo rows were unchanged after seeding. Live checks verified the
full account headings, species-only captions for all 30 photos, and all 60 image
and thumbnail URLs across the nine accounts.

## 24 September: Abrus seed photograph restored

The Abrus account had two newer flower photographs, but lacked the book's seed
view. Its curated folder and filenames say only `Abrus precatorius`, while the
manuscript account and newer photographs use `Abrus precatorius subsp. precatorius`.
The strict rank check therefore left the seed files unmatched.

Book Y, printed page 78 (PDF page 40), directly connects the seed photograph to
the full subspecies account. After comparing the proof and original files,
`1. (square) Abrus precatorius.649.b.jpg` is explicitly allocated to that account.
The other PICK file, `1.a Abrus precatorius.649.a.jpg`, is an alternate crop of the
same photograph. Two EXTRA photographs show repeated views of the same pod
cluster; their exclusions are also recorded. No general rank alias was added.

The page now retains **two flower photographs plus the book's seed photograph**,
showing both pink-to-mauve pea flowers and bright red black-spotted seeds in open
pods. Existing hero, captions and photo order are preserved. Archive-wide counts
are now **1,075 photographs across 498 of 531 accounts**.

Import, audit and site build passed. All 531 existing plant records and 1,074
existing photo records were unchanged after the local seed. The live Abrus API
returns the two original flower photographs in order followed by the seed view;
all six image/thumbnail URLs serve successfully.

Review principle: an illustrated account is not necessarily adequately
illustrated. Select complementary plant parts and life stages; check unresolved
book selections even when newer photographs have already supplied a hero.

## 24 September: Grevillea mimosoides hold resolved

The 24 March proof correction concerned an earlier layout. On inspection,
**book Y already incorporates it**: printed page 229 (PDF page 115) puts the
red-flowered Ian Morris photograph under G. longicuspis, and the two
cream-flowered photographs under G. mimosoides. The PDF metadata gives a creation
date of 25 March 2026, consistent with that sequence.

The two mimosoides photographs match `1. Grevillea mimosoides.014.a.JPG` and
`2. Grevillea mimosoides.029.a.JPG` in its curated PICK directory. William Burgess's
`Grevillea mimosoides 3279.JPG` provides a consistent additional view of the narrow
entire leaves and cream cylindrical flower racemes, with a butterfly. The
whole-account hold was our precaution and was no longer justified by the later
proof. It has been removed, retaining its history, proof path and the two source
hashes in `resolvedPhotoHolds`.

These **three photographs** bring coverage to **498 of 531 accounts** and
**1,074 photographs**, leaving **33 unillustrated accounts** (nine needing rank
review and 24 without a direct full-name filename match).

There is also a likely **description unit error**: both the manuscript and book Y
say leaf blades are `12–29cm x 6–20(30)cm`. The width probably means **mm**.
[Flora of Australia 17A, p. 375](https://www.dcceew.gov.au/sites/default/files/env/pages/9956603b-17a1-4fe2-b47a-3addcd924fc0/files/flora-australia-17a-proteaceae-2-grevillea.pdf)
describes a broader species range of 6.5–40 cm long and 6–50 mm wide. This supports
a unit correction but does not independently verify the manuscript's narrower
local range. The proposed correction is recorded in `descriptionReviewNotes`;
the manuscript and app description are unchanged pending editorial review.

The refreshed import, coverage audit and site build passed. Local database checks
confirmed all 531 existing plant rows and 1,071 existing photo rows unchanged.
The live gallery returns all three new photographs, and all six derivative URLs
serve WebP images successfully.

The rest of this note records the **23 September review**, before this resolution.

## 23 September results

The broader review recovered **161 photographs for 65 plant accounts**, including
the first photographs for **53 accounts**. The local preview has been updated.

| Measure | Before this review | After |
| --- | ---: | ---: |
| Plant accounts | 531 | 531 |
| Illustrated accounts | 444 | 497 |
| Accounts without photographs | 87 | 34 |
| Imported photographs | 910 | 1,071 |

## Scope and allocation method

Indexed 10,634 JPG, JPEG, TIFF, TIF and PNG files across the supplied `brocky`
archive. Visually inspected 265 candidates covering 80 accounts, concentrating
on unillustrated accounts and unresolved curated selections. This was not a
visual inspection of every archive image or independent botanical verification
of each identification.

The original importer recognised literal `PICK` directories. Useful selections
also occur in `SELECT`, `PICKX`, `PICK.1 (C. brachiata)` and annotated directories
such as `PICK (incl C. costatum)` and `PICK (X. umbrosus)`. Further photographs
occur in the Willie Burgess collection and newer field-trip collections.
Spelling differences also prevented some otherwise well-supported allocations.

Each reviewed file now has an explicit decision in
[data/editorial-decisions.json](data/editorial-decisions.json): original path,
SHA256, target account, inclusion/exclusion, credit, order and reason. These
decisions do not make every similarly named folder eligible for automatic import
or introduce general fuzzy taxonomic matching. The source archive is unchanged.

Selected distinct views of habit, leaves, flowers, fruit, seeds, bark and wildlife
associations where supported by source labels. There is no fixed gallery cap.
Obvious repeated views, alternate crops and duplicate copies were excluded.

| Decision for these 265 candidates | Files |
| --- | ---: |
| Selected | 161 |
| Repeated view or alternate crop | 49 |
| Species label insufficient for subspecies/variety | 25 |
| Map, rather than photograph | 10 |
| Duplicate copy | 9 |
| Name/allocation needs review | 8 |
| Source explicitly says TBC | 2 |
| Already imported copy with conflicting name | 1 |

Several maps have filenames consisting only of the species name. They were
recognised visually; checking only for `MAP` in a filename would miss them.

Photographer initials for new selections were checked against the manuscript's
front-matter contributor list. Unmarked images retain “Photographer not recorded”;
the book copyright statement does not establish the photographer of every file.
Existing credits and hero choices were preserved.

## Decisions requiring researcher attention

**Acacia alleniana / latescens:** the curated file
`1. Acacia latescens.455 .WB.JPG` is byte-identical to the already imported
`Acacia alleniana 2454.JPG` in Willie Burgess's collection. Its containing curated
folder also says alleniana. The duplicate is excluded and the existing original
allocation retained, with the conflict explicitly recorded. Needle-like foliage
is consistent with the manuscript and [WATTLE's alleniana description](https://apps.lucidcentral.org/wattle/text/entities/acacia_alleniana.htm);
[the latescens description](https://worldwidewattle.com/speciesgallery/latescens.php)
describes broader phyllodes. This supports further review; it is not independent
confirmation of the photograph's identification. The other latescens-labelled
file remains excluded.

Other held conflicts include an acutangula-labelled photograph under
Barringtonia asiatica, plicata-labelled photographs under Nervilia dallachyana,
and a breviflora-labelled photograph in a Grewia oxyphylla folder. The two files
explicitly marked `TBC` remain excluded. No species-only label was assumed to
identify a particular subspecies or variety.

At the end of this review, **Grevillea mimosoides** remained held because proof
corrections identified a G. longicuspis misallocation. That hold was subsequently
resolved on 24 September, as documented above.

Two apparent manuscript spelling errors deserve editorial review. Only explicit
photo allocations were made; account names, IDs and manuscript text are unchanged:

- **Jasminum didymum subsp. didymium:** the photo folder and
  [Kew's accepted name](https://powo.science.kew.org/taxon/urn:lsid:ipni.org:names:149607-3)
  use subsp. **didymum**. One full-rank-labelled photograph was added; species-only
  photographs remain held.
- **Leichardtia viridiflora subsp. tropica:** the folder and
  [Forster's taxonomic treatment](https://www.qld.gov.au/__data/assets/pdf_file/0023/158135/forster-gymnema-and-leichhardtia-austrobaileya-v11-1-18.pdf)
  use **Leichhardtia**. Two full-rank-labelled fruit photographs were added.

## Remaining coverage

Nine accounts have possible photographs whose labels do not establish the
required subspecies or variety:

- Alstonia spectabilis subsp. ophioxyloides
- Antiaris toxicaria var. macrophylla
- Crotalaria cunninghamii subsp. cunninghamii
- Ficus virens var. virens
- Ilex arnhemensis subsp. arnhemensis
- Leptospermum madidum subsp. sativum
- Lophostemon grandiflorus subsp. riparius
- Syzygium forte subsp. potamophilum
- Trichodesma zeylanicum var. zeylanicum

One account, **Grevillea mimosoides**, was held at this point; it is now illustrated
following the 24 September resolution above.

For the following 24 accounts, the archive scan found no direct full-name
filename match. This does **not** establish that photographs are absent:
abbreviations, synonyms, older names, mistakes or unlabelled photographs may
still provide candidates.

| Account | Account |
| --- | --- |
| Acacia lacertensis | Acacia megalantha |
| Acanthus ebracteatus | Bruguiera gymnorrhiza |
| Cartonema parviflorum | Ceriops tagal |
| Corymbia nesophila | Crateva religiosa |
| Desmodium heterocarpon var. strigosum | Eucalyptus koolpinensis |
| Evolvulus alsinoides | Grevillea dunlopii |
| Grevillea polyacida | Hibbertia dealbata |
| Hibbertia fractiflexa subsp. brachyblastis | Lumnitzera littorea |
| Melhania oblongifolia | Pityrodia lanuginosa |
| Pogostemon stellatus | Rhizophora apiculata |
| Solanum asymmetriphyllum | Terminalia arostrata |
| Xerochrysum borealis | Zornia prostrata |

For example, there is a Cartonema spicatum var. humile source folder, but its
relationship to the manuscript's C. parviflorum account has not been established.
It is not an automatic synonym allocation.

## Repeatable audit and validation

After `npm run import`, run `npm run audit:photos`. The audit writes ignored local
`data/photo-audit.json` and `data/photo-audit.md`, listing all remaining blanks,
full-name filename candidates, rank-only suggestions and reviewed decisions.
It reads the source-import baseline, not live admin state, and never imports or
publishes a candidate. The JSON retains paths for subsequent researcher review.

Validation completed for this review:

- `npm test`: 26 tests passed (11 importer, 15 client/Worker).
- `npm run build:site` and local `npm run db:seed` completed.
- Compared all 531 existing plant rows and 910 existing photo rows against the
  pre-import local database: unchanged. All 444 existing hero choices preserved.
- Checked all 2,142 generated image derivatives exist in the built site.
- Live HTTP checks verified all 65 affected account responses and all 322 new
  photograph/thumbnail URLs, plus catalogue coverage and the retained editorial hold.

These changes are in the local Native Plants checkout and preview; no remote
deployment was performed.

## 24 September: crop refinements, minimum dimensions and contributors

Reviewed and tightened 132 proof-photo crops to remove residual paper gutters and
compression fringes, including all four Koolpinensis photographs. The old extraction
PNGs remain untouched; new ` - trimmed.png` files have pinned hashes and prior-crop
evidence in `book-photo-extractions.json`. Each replacement retains its original
PlantPhoto ID. Seed updates require the previous image/thumbnail URLs and preserve
owner captions, ordering, publication and deletion fields.

Photographs with either full-image dimension below 200px remain visible inline but
have no enlargement button or “View photograph” prompt. This applies to 62 current
photos, including Koolpinensis's small bark and fruit insets. Unknown photographer
credits are silent in both inline captions and the photograph viewer.

The supplied contributor index adds these verified initials to the importer. Counts
below are selected published photographs, not all files in the archive:

| Initials | Photographer | Selected photos |
| --- | --- | ---: |
| KB | Kym Brennan | 4 |
| IC | Ian Cowie | 3 |
| DH | David Hancock | 1 |
| DL | Diane Lucas | 6 |
| AM | Anita Meadows | 2 |
| KM | Keira Meadows | 5 |
| LP | Leigh Patterson | 0 |
| JP | Julia Perdevich | 1 |
| TR | Tissa Ratnayeke | 0 |
| JRS | Jeremy Russell-Smith | 4 |
| NS | Nic Smith | 4 |
| BS | Ben Stuckey | 1 |
| AW | Aiden Webb | 7 |

35 previously unrecorded credits were filled from filename initials; three selections
from this contributor list already had explicit credits. Existing named credits were
preserved. Across the collection, 553 photos name 18 photographers; 791 still have
no established credit. Leigh Patterson and Tissa Ratnayeke are recognized mappings,
but no currently selected image carries their initials. The four existing mappings
(IM, WB, RD, GF) and explicitly recorded names remain available.

The browse page's Photographer filter matches any published, undeleted photo on an
account, including supporting images. Facet counts count distinct plant accounts;
filter URLs are shareable and combine with botanical filters. Photo credits remain
editable through PlantPhoto admin and are independent of application user identity.

Eight images previously counted as photographs are distribution maps. Their photo
records are unpublished and preserved; dedicated map records display them with the
other maps. Current totals: 1,344 published photographs, 531 illustrated accounts,
289 accounts with a distribution map. See [MAP-REVIEW.md](MAP-REVIEW.md).
