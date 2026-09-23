# Archive photograph review — 23 September 2026

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

The existing **Grevillea mimosoides** hold remains: proof corrections identified
a G. longicuspis misallocation. These files have not been reinstated.

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

One account, **Grevillea mimosoides**, has the existing editorial hold above.

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
