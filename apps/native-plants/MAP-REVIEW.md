# Distribution-map review — 24 September 2026

The local app has **289 published maps on 289 of 531 accounts**: 271 maps from the
2022 edition and all 18 standalone 2026 maps. There are 295 allocated map records;
six older maps remain unpublished where a 2026 standalone map supersedes them.
Eight additional proof extractions remain held without a species allocation.

## Sources and extraction

`COMPLETE NPNA 2022.A.pdf` in `HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT`
under `~/Desktop/brocky` preserves larger native map objects than the compressed
single-page `PROOF Native Plants of N Australia-PDF.pdf`. We extracted 285 map
objects, excluding botanical drawings; 277 had sufficient documentary evidence for
allocation. The 2022 A proof is the map source, while the single-page proof remains
the photograph source. All originals are local, so the external volume is unnecessary
for reproduction.

PDF stencil images decode with inverted grayscale; extraction restores the printed
black marks on white. Output retains native resolution, outlines and occurrence
marks. The maps were visually reviewed in contact sheets. Source hashes, page and
image-object numbers, published names, account associations and output hashes are
recorded in `data/book-distribution-maps.json`.

The 277 allocations comprise 261 exact names, 10 previous names explicitly recorded
in the current manuscript, four species maps illustrating ranked accounts, and two
varietal maps under current species accounts. Public captions preserve the published
taxon; they do not imply a finer identification or a taxonomic conclusion.

## Standalone 2026 collection

All 18 original JPEGs are copied, unchanged, into:

`~/Desktop/brocky/Distribution maps/2026 originals/`

Display PNGs are in:

`~/Desktop/brocky/Distribution maps/2026 normalized/`

Each map uses the reviewed native crop `[573,451,2940,4512]`, preserving a margin
around the entire NT outline. Display copies use grayscale and a maximum 1600px
longest edge. No map is redrawn, georeferenced or assigned inferred coordinates.
The source/derived hashes and allocation evidence are in
`data/standalone-distribution-maps.json`.

| Account | Display choice |
| --- | --- |
| Blakella bella | Replaces the displayed 2022 map; older record retained |
| Blakella polysciada | Replaces the displayed 2022 map; older record retained |
| Brachychiton megaphyllus | Replaces the displayed 2022 map; older record retained |
| Calytrix arborescens | Adds map coverage |
| Cleome microaustralica | Replaces the displayed 2022 map; older record retained |
| Corymbia arnhemensis | Adds map coverage |
| Corymbia chartacea | Adds map coverage |
| Corymbia dunlopiana | Adds map coverage |
| Eucalyptus patellaris | Replaces the displayed 2022 map; older record retained |
| Eucalyptus tintinnans | Replaces the displayed 2022 map; older record retained |
| Flacourtia territorialis | Adds map coverage |
| Grevillea sp. Magela Creek | Adds map coverage |
| Harpullia leichhardtii | Adds map coverage |
| Helicteres macrothrix | Adds map coverage |
| Hibiscus petherickii | Adds map coverage |
| Hildegardia australiensis | Adds map coverage |
| Macropteranthes kekwickii | Adds map coverage |
| Stenostegia congesta | Adds map coverage |

Eight of these maps had previously been selected from PICK as photographs. Their
photo records are now unpublished with a classification marker, and their dedicated
PlantMap records supply the Distribution section. No plant account or photograph ID
was replaced. There remain 1,344 published photographs and all 531 accounts illustrated.

## Held proof maps

These files are extracted locally, but assigning them to a current account needs
an explicit editorial decision. Combined labels are not split by assumption.

| Published name | Printed page | Reason |
| --- | --- | --- |
| Acacia neurocarpa and Acacia holosericea | 96 | Published name or combined account needs an explicit editorial association before importing; no inferred taxonomic equivalence. |
| Bauhinia binata | 126 | Published name or combined account needs an explicit editorial association before importing; no inferred taxonomic equivalence. |
| Bauhinia cunninghamii | 126 | Published name or combined account needs an explicit editorial association before importing; no inferred taxonomic equivalence. |
| Cleome cleomoides | 150 | Published name or combined account needs an explicit editorial association before importing; no inferred taxonomic equivalence. |
| Drosera sp. | 186 | Unresolved species label; not enough evidence to identify this as the current Drosera sp. serpens account. |
| Ficus atricha and F. brachypoda | 212 | Published name or combined account needs an explicit editorial association before importing; no inferred taxonomic equivalence. |
| Hibiscus sp. (possibly H. panduriformis) | 247 | Published name or combined account needs an explicit editorial association before importing; no inferred taxonomic equivalence. |
| Myrsine pedicellata | 284 | Published name or combined account needs an explicit editorial association before importing; no inferred taxonomic equivalence. |

## Presentation and ownership

Maps sit alongside the Distribution text in a transparent, borderless div at 50%
width, with no padding or centering. They preserve their full outline with
`object-fit: contain`. They display inline without
an enlargement action. Captions retain the published name and edition/page or
source collection. These are
historical source illustrations, not live occurrence queries or exhaustive current
range claims. Missing maps do not imply an absence of the species.

PlantMap is an app-owned editable record with publication/order controls in Hedge
admin. The same catalogue snapshot/cache includes maps; publication and deletion
behave like other account content. Private paths and evidence never enter the public
API. Six superseded records can be inspected in admin and republished deliberately.

## Transparent display assets — 24 September

The importer produces black-and-transparent GIFs from all 295 allocated, reviewed
map PNGs (289 published, six superseded). Source and normalized PNGs remain intact.
No resizing or dithering occurs. The binary 2022 maps retain their ink pixels
exactly; grayscale 2026 maps use a fixed threshold of 192/255, retaining darker
pixels as black and making lighter paper/fringe pixels transparent. GIF palette
index 0 is transparent, and index 1 is black. The source hash remains in the asset
filename, with `.gif` replacing `.png`.

Seeding conditionally updates only a map whose image still has the corresponding
reviewed PNG URL. Custom owner images, captions, source evidence, sort order,
publication state and soft deletion are preserved; repeated imports are idempotent.
All 295 converted masks and dimensions were checked, and the comparison of 2022
and 2026 maps against the page background was visually reviewed. The focused suite
now has 18 importer and 27 client/Worker checks.

## Reproduction and checks

From `apps/native-plants`, with Pillow and pypdfium2:

```sh
python3 scripts/extract-book-maps.py
python3 scripts/normalize-standalone-maps.py
npm run import
npm run db:init
npm run db:seed
npm run build
npm test
```

The extraction runtime used here is
`/Users/jtnt/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/bin/python3`
(Pillow 12.3.0, pypdfium2). PNG byte encoding can differ between library builds even
with the same Pillow version: if reproduction fails its recorded hash, use this
runtime or review the changed extraction before changing the manifest. Existing
normalized files are verified, never overwritten.

Verification after refinement: all 531 live account responses checked, all 289 served
map PNGs byte-compared with reviewed assets, all 531 stored plant rows unchanged,
all 1,352 pre-existing photo IDs preserved. The focused suite has 15 importer and 20
client/Worker checks. Chrome confirmed the original map viewer, source captions, photographer
filter and small-photo behavior. No remote deployment was performed.
