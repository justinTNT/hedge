# Crucial X10 — photographs for the 24 empty plant accounts

**All 24 have an authored photograph in the book proofs.** This closes the question of whether a pictured source exists, but not the search for full-resolution originals: most recoverable proof images are about 566–569 pixels across, sometimes with smaller panels inside a composite.

Three accounts have supported loose-photo options: **Cartonema parviflorum**, **Hibbertia fractiflexa subsp. brachyblastis**, and **Terminalia arostrata**. Four more have plausible but unresolved loose leads: **Eucalyptus koolpinensis**, **Hibbertia dealbata**, **Melhania oblongifolia**, and **Solanum asymmetriphyllum**. For the other 17, the book remains the best supported recovery route found here. The Xerochrysum photograph labelled Fraser Island is explicitly excluded from that conclusion.

Suggested later sequence: select the three supported loose-photo subjects; decide whether the small book photographs are acceptable temporary fallbacks; ask the photographer/researcher about the four named uncertain sequences. Do not silently allocate the latter by folder name. The book and loose views can be complementary—for example Terminalia foliage plus its loose dry-nut photograph.

Companion: [missing-photos.csv](missing-photos.csv). Expansion is reported separately in [gallery-expansion.md](gallery-expansion.md).


## Scope and how to read this report

Read-only audit of `/Volumes/Crucial X10`, 24 September 2026. Baseline: the live Native Plants preview catalogue captured at 11:04 ACST (531 accounts; 1,105 gallery entries), checkout `hedge-native-plants`, commit `71ae033`. No photographs were imported and no species, captions, gallery allocations, source files or databases were changed. Preview images were generated only in temporary working storage.

The two targets are separate: 24 accounts with zero gallery entries, and 489 accounts with 1–4 entries. The 18 accounts already showing five or more were outside the expansion target. Five is a review threshold, not a proposed gallery limit. A book composite is one source object and may contain several photographs; neither its panels nor repeated exports are counted as independent recovered originals.

Confidence measures **documentary association with the account**, not a botanical identification probability. No percentage is warranted. Visual inspection checks the visible subject, usefulness and duplication; a botanical specialist still resolves doubtful names. Resolution is reported separately, so a strongly associated book photograph can be a poor full-screen image.

| Grade | Meaning | Appropriate treatment |
| --- | --- | --- |
| **A — strong association** | Explicit species/full-rank filename, or an authored book layout pairing the photograph with the account; no known competing label. | Strong candidate for a later editorial selection, subject to quality and provenance. |
| **B — supported association** | Clear species-folder context, historical-name/spelling trail, or species-level label for a finer-rank account. | Preserve the source name; confirm provenance/rank where needed. |
| **C — unresolved lead** | TBC/probable labels, mixed shoots, contradictory names, another named rank, or an insufficient informal `sp.` label. | Identification work first; not an approved allocation. |

Candidate order is A, then B, then C; within a grade loose photographs precede book fallbacks and the visual-review preference is retained. This means a small A-grade book image can precede a large but uncertain loose original. Binomial-only photographs may illustrate a subspecies/variety account under the previously agreed **species-level caption**; they do not verify that rank. A photograph explicitly labelled as another subspecies is not treated as an unqualified species photograph.

The CSV has one row per account, `species` first, followed by numbered path columns in confidence order. The correspondingly numbered confidence, locator, resolution, use and basis columns explain each path. Empty slots mean no shortlisted candidate, not a missing CSV value. PDF paths deliberately repeat when they contain different page images. PDF pages are **one-based file pages**; printed page numbers are given separately. Page-object indices are zero-based PDFium page-object indices, not global PDF object/xref numbers; use the page and subject when browsing by hand.

## What was inspected

- Indexed 113,514 data files, including 109,901 image-like files across HD, SD, WILDLIFE and the other data roots. This includes 37,403 camera RAW files (RW2/ARW/NEF). Unrelated music artwork was excluded from photographic analysis.
- Read/hash/thumbnail pass over 72,133 non-music rendered-image files, plus all 1,105 currently published gallery files. SHA-256 identified exact copies; perceptual comparison suggested crops, exports and repeat views. Similarity is a screening aid, not a species classifier.
- Searched current names, source aliases, historical combinations, spelling variants, species folders, photographer collections, habitat/wildlife labels, dated mixed shoots and previous-edition instructions. The same image under conflicting names was explicitly checked rather than counted twice.
- Visually compared 5,101 loose expansion representatives (up to 12 per account) against their current galleries, 180 unique loose candidates for empty accounts, and book candidates/layouts. This is broad archival coverage, **not** a claim that every one of the 72,133 files was individually botanically identified. Named collections listed below contain additional exposures beyond the inspected representative set.
- Examined five principal 2021/2022/2026 PDF editions, plus the N/R/V proof-image metadata and relevant embedded images; verified all 24 missing accounts in both older and 2026 layouts. Older single-page proof images are often larger than the newer spread-proof copies. Checked 487 DOCX packages for embedded media (20 readable packages contained 47 images; two package/read errors were temporary or unreadable files). Relevant book-page photographs and reference screenshots were distinguished from original photographs.
- Camera RAW files were inventoried but not developed. JPEG/RAW pairs and repeated HD/SD copies are opportunities for source quality, not extra gallery subjects. Unlabelled shoots may still contain useful plants requiring human identification; a negative finding here is not proof of absence from every frame.

Protected Spotlight metadata was inaccessible; the plant-data trees were enumerated. Two rendered TIFF copies could not be decoded, both `Trichosanthes cucumerina var. cucumerina.894.tif` in the `RAW/TIF` branches of the two `SD/# AA NEW SPECIES` / `SD/# A A COMPLETED` Trichosanthes folders. They remain source-quality leads rather than visually assessed additions. Credit initials and contributor names in paths are retained as provenance clues; their expansion or permission is not assumed. Keep the original credit trail when selecting images later.


## Account index

| Species | Best available route | Loose shortlisted / book objects |
| --- | --- | --- |
| [Acacia lacertensis](#plant-dc5e0d2eb7808b0b) | Book fallback; no supported loose original found | 0 / 1 |
| [Acacia megalantha](#plant-b31c2b6ac29a5a05) | Book fallback; no supported loose original found | 0 / 1 |
| [Acanthus ebracteatus](#plant-db13293ed2ddb3df) | Book fallback; no supported loose original found | 0 / 1 |
| [Bruguiera gymnorrhiza](#plant-08731e5bbf119c1e) | Book fallback; no supported loose original found | 0 / 2 |
| [Cartonema parviflorum](#plant-f55b36118b76cfaf) | Supported loose photographs + book fallback | 1 / 1 |
| [Ceriops tagal](#plant-8e6345f09f6f2690) | Book fallback; no supported loose original found | 0 / 1 |
| [Corymbia nesophila](#plant-b49e1a7aab099221) | Book fallback; no supported loose original found | 0 / 2 |
| [Crateva religiosa](#plant-34d5ff12c99acb7b) | Book fallback; no supported loose original found | 0 / 1 |
| [Desmodium heterocarpon var. strigosum](#plant-9df61a66f0761593) | Book fallback; no supported loose original found | 0 / 1 |
| [Eucalyptus koolpinensis](#plant-d58ebbdd4f2aa1ae) | Book fallback + uncertain loose leads | 3 / 2 |
| [Evolvulus alsinoides](#plant-094037a6a72d9c11) | Book fallback; no supported loose original found | 0 / 1 |
| [Grevillea dunlopii](#plant-bb59625835b378e8) | Book fallback; no supported loose original found | 0 / 1 |
| [Grevillea polyacida](#plant-7478d7ccc9fdb334) | Book fallback; no supported loose original found | 0 / 1 |
| [Hibbertia dealbata](#plant-dc373e820f15679e) | Book fallback + uncertain loose leads | 2 / 1 |
| [Hibbertia fractiflexa subsp. brachyblastis](#plant-daee0f7a8f6dba15) | Supported loose photographs + book fallback | 1 / 1 |
| [Lumnitzera littorea](#plant-4b758bc02531a17f) | Book fallback; no supported loose original found | 0 / 1 |
| [Melhania oblongifolia](#plant-08e9119a23671c38) | Book fallback + uncertain loose leads | 1 / 1 |
| [Pityrodia lanuginosa](#plant-1adbdbfe7aa98e77) | Book fallback; no supported loose original found | 0 / 1 |
| [Pogostemon stellatus](#plant-23afe8e812e1e977) | Book fallback; no supported loose original found | 0 / 1 |
| [Rhizophora apiculata](#plant-bd2fbf83a83b5d60) | Book fallback; no supported loose original found | 0 / 1 |
| [Solanum asymmetriphyllum](#plant-22b9ddafbe2abd1c) | Book fallback + uncertain loose leads | 3 / 1 |
| [Terminalia arostrata](#plant-3439591df5e65edc) | Supported loose photographs + book fallback | 1 / 1 |
| [Xerochrysum borealis](#plant-11757202d24dd423) | Book fallback; no supported loose original found | 0 / 1 |
| [Zornia prostrata](#plant-5a696f854b4f436f) | Book fallback; no supported loose original found | 0 / 1 |

## Individual accounts

<a id="plant-dc5e0d2eb7808b0b"></a>

### Acacia lacertensis

**Book fallback; no supported loose original found.** Current gallery: empty.

No confidently associated loose original was located in the named collections or the wider filename/image comparison. The proof preserves a photograph with this species account.

| Order / inspection reference | Confidence | Source / resolution | Exact candidate path and locator |
| --- | --- | --- | --- |
| 1 / B-p91-o121 | **A** | book image; 566 × 573 px; low-resolution fallback: Composite: flowering foliage, habit and pods | `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/PROOF Native Plants of N Australia-PDF.pdf` — PDF page 91; printed page 90; page-object index 121 (zero-based) |

Association evidence:

- **1:** Photograph positioned with the authored species account in the book proof.

Previous-edition instructions preserved on this volume:

- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/AA/Acacia lacertensis (use 2022 flwr & fruit shots)` — empty folder.
- `/Volumes/Crucial X10/SD/# A A A A COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/AA/Acacia lacertensis (use 2022 flwr & fruit shots)` — empty folder.

Other proof locations (corroborating layout/copies, not additional independent photographs):

- `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/COMPLETE NPNA 2022.A.pdf` — PDF page 46.
- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A A MANUSCRIPT/5. FULL DRAFT & EDITS JAN-MAR 2026/69c3d6be8a3f9_Native Plants of Northern Australia - book Y.pdf` — PDF page 44.

<a id="plant-b31c2b6ac29a5a05"></a>

### Acacia megalantha

**Book fallback; no supported loose original found.** Current gallery: empty.

No confidently associated loose original was located in the named collections or the wider filename/image comparison. The proof preserves a photograph with this species account.

| Order / inspection reference | Confidence | Source / resolution | Exact candidate path and locator |
| --- | --- | --- | --- |
| 1 / B-p94-o1 | **A** | book image; 566 × 574 px; low-resolution fallback: Composite: flowering foliage and pods | `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/PROOF Native Plants of N Australia-PDF.pdf` — PDF page 94; printed page 93; page-object index 1 (zero-based) |

Association evidence:

- **1:** Photograph positioned with the authored species account in the book proof.

Previous-edition instructions preserved on this volume:

- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/AA/Acacia megalantha (use 2022 flwr & fruit shots)` — empty folder.
- `/Volumes/Crucial X10/SD/# A A A A COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/AA/Acacia megalantha (use 2022 flwr & fruit shots)` — empty folder.

Other proof locations (corroborating layout/copies, not additional independent photographs):

- `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/COMPLETE NPNA 2022.A.pdf` — PDF page 47.
- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A A MANUSCRIPT/5. FULL DRAFT & EDITS JAN-MAR 2026/69c3d6be8a3f9_Native Plants of Northern Australia - book Y.pdf` — PDF page 46.

<a id="plant-db13293ed2ddb3df"></a>

### Acanthus ebracteatus

**Book fallback; no supported loose original found.** Current gallery: empty.

No confidently associated loose original was located in the named collections or the wider filename/image comparison. The proof preserves a photograph with this species account.

| Order / inspection reference | Confidence | Source / resolution | Exact candidate path and locator |
| --- | --- | --- | --- |
| 1 / B-p105-o1 | **A** | book image; 566 × 386 px; low-resolution fallback: Flowers and leaves | `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/PROOF Native Plants of N Australia-PDF.pdf` — PDF page 105; printed page 104; page-object index 1 (zero-based) |

Association evidence:

- **1:** Photograph positioned with the authored species account in the book proof.

Previous-edition instructions preserved on this volume:

- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/AA/Acanthus ebracteatus (use 2022 pic & map)` — empty folder.
- `/Volumes/Crucial X10/SD/# A A A A COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/AA/Acanthus ebracteatus (use 2022 pic & map)` — empty folder.

Other proof locations (corroborating layout/copies, not additional independent photographs):

- `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/COMPLETE NPNA 2022.A.pdf` — PDF page 53.
- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A A MANUSCRIPT/5. FULL DRAFT & EDITS JAN-MAR 2026/69c3d6be8a3f9_Native Plants of Northern Australia - book Y.pdf` — PDF page 51.

<a id="plant-08731e5bbf119c1e"></a>

### Bruguiera gymnorrhiza

**Book fallback; no supported loose original found.** Current gallery: empty.

No confidently associated loose original was located in the named collections or the wider filename/image comparison. The proof preserves a photograph with this species account.

| Order / inspection reference | Confidence | Source / resolution | Exact candidate path and locator |
| --- | --- | --- | --- |
| 1 / B-p134-o1 | **A** | book image; 390 × 569 px; low-resolution fallback: Foliage and flowers | `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/PROOF Native Plants of N Australia-PDF.pdf` — PDF page 134; printed page 133; page-object index 1 (zero-based) |
| 2 / B-p134-o2 | **A** | book image; 566 × 565 px; low-resolution fallback: Mangrove stand/habit | `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/PROOF Native Plants of N Australia-PDF.pdf` — PDF page 134; printed page 133; page-object index 2 (zero-based) |

Association evidence:

- **1:** Photograph positioned with the authored species account in the book proof.
- **2:** Photograph positioned with the authored species account in the book proof.

Other proof locations (corroborating layout/copies, not additional independent photographs):

- `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/COMPLETE NPNA 2022.A.pdf` — PDF page 67.
- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A A MANUSCRIPT/5. FULL DRAFT & EDITS JAN-MAR 2026/69c3d6be8a3f9_Native Plants of Northern Australia - book Y.pdf` — PDF page 67.

<a id="plant-f55b36118b76cfaf"></a>

### Cartonema parviflorum

**Supported loose photographs + book fallback.** Current gallery: empty.

Two curated HD PICK files show essentially the same yellow-flowered plant/view. Prefer 624.a as one new gallery photograph; 627.a is a close alternate, not a second distinct subject. The book offers a different whole-plant photograph.

| Order / inspection reference | Confidence | Source / resolution | Exact candidate path and locator |
| --- | --- | --- | --- |
| 1 / L001 | **A** | loose photograph; 3651 × 2738 px; gallery option | `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/CC/Cartonema parviflorum/PICK/1. Cartonema parviflorum.624.a.JPG` |
| 2 / B-p146-o90 | **A** | book image; 569 × 583 px; low-resolution fallback: Whole flowering plant, different from the loose PICK photograph | `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/PROOF Native Plants of N Australia-PDF.pdf` — PDF page 146; printed page 145; page-object index 90 (zero-based) |

Association evidence:

- **1:** Species named in the filename.
- **2:** Photograph positioned with the authored species account in the book proof.

Close alternatives, not extra recommended subjects:

- L002: `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/CC/Cartonema parviflorum/PICK/1.a Cartonema parviflorum.627.a.JPG` — 2942 × 2206 px.

Other proof locations (corroborating layout/copies, not additional independent photographs):

- `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/COMPLETE NPNA 2022.A.pdf` — PDF page 73.
- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A A MANUSCRIPT/5. FULL DRAFT & EDITS JAN-MAR 2026/69c3d6be8a3f9_Native Plants of Northern Australia - book Y.pdf` — PDF page 73.

<details>
<summary>All filename/folder leads for this account, including duplicates and uncertain material</summary>

Counts are files, not distinct photographs. A folder association is a search lead, not an approved allocation.

- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/CC/Cartonema parviflorum/PICK` — 2 rendered files; 0 RAW files.

</details>

<a id="plant-8e6345f09f6f2690"></a>

### Ceriops tagal

**Book fallback; no supported loose original found.** Current gallery: empty.

No confidently associated loose original was located in the named collections or the wider filename/image comparison. The proof preserves a photograph with this species account.

| Order / inspection reference | Confidence | Source / resolution | Exact candidate path and locator |
| --- | --- | --- | --- |
| 1 / B-p150-o2 | **A** | book image; 567 × 567 px; low-resolution fallback: Foliage and propagules | `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/PROOF Native Plants of N Australia-PDF.pdf` — PDF page 150; printed page 149; page-object index 2 (zero-based) |

Association evidence:

- **1:** Photograph positioned with the authored species account in the book proof.

Previous-edition instructions preserved on this volume:

- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/CC/Ceriops tagal (plse use 2022 photo)` — empty folder.
- `/Volumes/Crucial X10/SD/# A A A A COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/CC/Ceriops tagal (plse use 2022 photo)` — empty folder.

Other proof locations (corroborating layout/copies, not additional independent photographs):

- `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/COMPLETE NPNA 2022.A.pdf` — PDF page 75.
- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A A MANUSCRIPT/5. FULL DRAFT & EDITS JAN-MAR 2026/69c3d6be8a3f9_Native Plants of Northern Australia - book Y.pdf` — PDF page 75.

<a id="plant-b49e1a7aab099221"></a>

### Corymbia nesophila

**Book fallback; no supported loose original found.** Current gallery: empty.

No confidently associated loose original was located in the named collections or the wider filename/image comparison. The proof preserves a photograph with this species account.

| Order / inspection reference | Confidence | Source / resolution | Exact candidate path and locator |
| --- | --- | --- | --- |
| 1 / B-p165-o1 | **A** | book image; 566 × 566 px; low-resolution fallback: Flowers/buds | `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/PROOF Native Plants of N Australia-PDF.pdf` — PDF page 165; printed page 164; page-object index 1 (zero-based) |
| 2 / B-p165-o2 | **A** | book image; 567 × 566 px; low-resolution fallback: Habit and bark | `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/PROOF Native Plants of N Australia-PDF.pdf` — PDF page 165; printed page 164; page-object index 2 (zero-based) |

Association evidence:

- **1:** Photograph positioned with the authored species account in the book proof.
- **2:** Photograph positioned with the authored species account in the book proof.

Other proof locations (corroborating layout/copies, not additional independent photographs):

- `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/COMPLETE NPNA 2022.A.pdf` — PDF page 83.
- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A A MANUSCRIPT/5. FULL DRAFT & EDITS JAN-MAR 2026/69c3d6be8a3f9_Native Plants of Northern Australia - book Y.pdf` — PDF page 82.

<a id="plant-34d5ff12c99acb7b"></a>

### Crateva religiosa

**Book fallback; no supported loose original found.** Current gallery: empty.

No confidently associated loose original was located in the named collections or the wider filename/image comparison. The proof preserves a photograph with this species account.

| Order / inspection reference | Confidence | Source / resolution | Exact candidate path and locator |
| --- | --- | --- | --- |
| 1 / B-p170-o1 | **A** | book image; 566 × 394 px; low-resolution fallback: Flowers | `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/PROOF Native Plants of N Australia-PDF.pdf` — PDF page 170; printed page 169; page-object index 1 (zero-based) |

Association evidence:

- **1:** Photograph positioned with the authored species account in the book proof.

Previous-edition instructions preserved on this volume:

- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/CC/Crateva religiosa (use 2022 photo)` — empty folder.
- `/Volumes/Crucial X10/SD/# A A A A COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/CC/Crateva religiosa (use 2022 photo)` — empty folder.

Other proof locations (corroborating layout/copies, not additional independent photographs):

- `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/COMPLETE NPNA 2022.A.pdf` — PDF page 85.
- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A A MANUSCRIPT/5. FULL DRAFT & EDITS JAN-MAR 2026/69c3d6be8a3f9_Native Plants of Northern Australia - book Y.pdf` — PDF page 84.

<a id="plant-9df61a66f0761593"></a>

### Desmodium heterocarpon var. strigosum

**Book fallback; no supported loose original found.** Current gallery: empty.

No confidently associated loose original was located in the named collections or the wider filename/image comparison. The proof preserves a photograph with this species account.

| Order / inspection reference | Confidence | Source / resolution | Exact candidate path and locator |
| --- | --- | --- | --- |
| 1 / B-p182-o2 | **A** | book image; 566 × 564 px; low-resolution fallback: Flowering stem | `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/PROOF Native Plants of N Australia-PDF.pdf` — PDF page 182; printed page 181; page-object index 2 (zero-based) |

Association evidence:

- **1:** Photograph positioned with the authored species account in the book proof.

Other proof locations (corroborating layout/copies, not additional independent photographs):

- `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/COMPLETE NPNA 2022.A.pdf` — PDF page 91.
- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A A MANUSCRIPT/5. FULL DRAFT & EDITS JAN-MAR 2026/69c3d6be8a3f9_Native Plants of Northern Australia - book Y.pdf` — PDF page 90.

<a id="plant-d58ebbdd4f2aa1ae"></a>

### Eucalyptus koolpinensis

**Book fallback + uncertain loose leads.** Current gallery: empty.

Reviewed every JPEG in the mixed 30–31 May 2026 shoot. P1130794–P1130798 show an eucalypt branch with leaves and woody fruit; P1130799–P1130801 show bark. P1130795, P1130794 and P1130799 are the useful representative leads. Most other frames and every PICK/SELECT export show Corypha palms; some show Sesbania flowers or Cycas. The folder's Euc koolpinensis label supports a lead, but does not establish the species of each frame. Ask the photographer to confirm this short sequence before allocation.

| Order / inspection reference | Confidence | Source / resolution | Exact candidate path and locator |
| --- | --- | --- | --- |
| 1 / B-p202-o1 | **A** | book image; 567 × 572 px; low-resolution fallback: Flowering foliage | `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/PROOF Native Plants of N Australia-PDF.pdf` — PDF page 202; printed page 201; page-object index 1 (zero-based) |
| 2 / B-p202-o2 | **A** | book image; 567 × 569 px; low-resolution fallback: Composite: habit, bark and fruit | `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/PROOF Native Plants of N Australia-PDF.pdf` — PDF page 202; printed page 201; page-object index 2 (zero-based) |
| 3 / L064 | **C** | loose photograph; 4864 × 3648 px; identification-review lead | `/Volumes/Crucial X10/SD/# A A A A NPNA 2026 NEW PHOTOS/A CORYPHA UTAN, Sesbania formosa, Euc koolpinensis, Cycas armstr 30-31.5.2026/P1130795.JPG` |
| 4 / L063 | **C** | loose photograph; 4864 × 3648 px; identification-review lead | `/Volumes/Crucial X10/SD/# A A A A NPNA 2026 NEW PHOTOS/A CORYPHA UTAN, Sesbania formosa, Euc koolpinensis, Cycas armstr 30-31.5.2026/P1130794.JPG` |
| 5 / L068 | **C** | loose photograph; 4864 × 3648 px; identification-review lead | `/Volumes/Crucial X10/SD/# A A A A NPNA 2026 NEW PHOTOS/A CORYPHA UTAN, Sesbania formosa, Euc koolpinensis, Cycas armstr 30-31.5.2026/P1130799.JPG` |

Association evidence:

- **1:** Photograph positioned with the authored species account in the book proof.
- **2:** Photograph positioned with the authored species account in the book proof.
- **3:** Association through the named source folder or historical-name trail; Name spelling, abbreviation or historical combination needs retaining in provenance; Source explicitly uncertain or a mixed/incorrectly labelled collection.
- **4:** Association through the named source folder or historical-name trail; Name spelling, abbreviation or historical combination needs retaining in provenance; Source explicitly uncertain or a mixed/incorrectly labelled collection.
- **5:** Association through the named source folder or historical-name trail; Name spelling, abbreviation or historical combination needs retaining in provenance; Source explicitly uncertain or a mixed/incorrectly labelled collection.

Other proof locations (corroborating layout/copies, not additional independent photographs):

- `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/COMPLETE NPNA 2022.A.pdf` — PDF page 101.
- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A A MANUSCRIPT/5. FULL DRAFT & EDITS JAN-MAR 2026/69c3d6be8a3f9_Native Plants of Northern Australia - book Y.pdf` — PDF page 101.

<details>
<summary>All filename/folder leads for this account, including duplicates and uncertain material</summary>

Counts are files, not distinct photographs. A folder association is a search lead, not an approved allocation.

- `/Volumes/Crucial X10/SD/# A A A A NPNA 2026 NEW PHOTOS/A CORYPHA UTAN, Sesbania formosa, Euc koolpinensis, Cycas armstr 30-31.5.2026` — 121 rendered files; 117 RAW files.
- `/Volumes/Crucial X10/SD/# A A A A NPNA 2026 NEW PHOTOS/A CORYPHA UTAN, Sesbania formosa, Euc koolpinensis, Cycas armstr 30-31.5.2026/SELECT` — 7 rendered files; 1 RAW files.
- `/Volumes/Crucial X10/SD/# A A A A NPNA 2026 NEW PHOTOS/A CORYPHA UTAN, Sesbania formosa, Euc koolpinensis, Cycas armstr 30-31.5.2026/SELECT/FOR GEORGE ABC` — 5 rendered files; 0 RAW files.
- `/Volumes/Crucial X10/SD/# A A A A NPNA 2026 NEW PHOTOS/A CORYPHA UTAN, Sesbania formosa, Euc koolpinensis, Cycas armstr 30-31.5.2026/SELECT/PICK` — 7 rendered files; 0 RAW files.

</details>

<a id="plant-094037a6a72d9c11"></a>

### Evolvulus alsinoides

**Book fallback; no supported loose original found.** Current gallery: empty.

No confidently associated loose original was located in the named collections or the wider filename/image comparison. The proof preserves a photograph with this species account.

| Order / inspection reference | Confidence | Source / resolution | Exact candidate path and locator |
| --- | --- | --- | --- |
| 1 / B-p210-o1 | **A** | book image; 566 × 570 px; low-resolution fallback: Flowering herb | `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/PROOF Native Plants of N Australia-PDF.pdf` — PDF page 210; printed page 209; page-object index 1 (zero-based) |

Association evidence:

- **1:** Photograph positioned with the authored species account in the book proof.

Previous-edition instructions preserved on this volume:

- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/EE/Evolvulus alsinoides (use 2022 photo)` — empty folder.
- `/Volumes/Crucial X10/SD/# A A A A COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/EE/Evolvulus alsinoides (use 2022 photo)` — empty folder.

Other proof locations (corroborating layout/copies, not additional independent photographs):

- `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/COMPLETE NPNA 2022.A.pdf` — PDF page 105.
- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A A MANUSCRIPT/5. FULL DRAFT & EDITS JAN-MAR 2026/69c3d6be8a3f9_Native Plants of Northern Australia - book Y.pdf` — PDF page 105.

<a id="plant-bb59625835b378e8"></a>

### Grevillea dunlopii

**Book fallback; no supported loose original found.** Current gallery: empty.

No confidently associated loose original was located in the named collections or the wider filename/image comparison. The proof preserves a photograph with this species account.

| Order / inspection reference | Confidence | Source / resolution | Exact candidate path and locator |
| --- | --- | --- | --- |
| 1 / B-p227-o2 | **A** | book image; 566 × 566 px; low-resolution fallback: Flowering branch | `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/PROOF Native Plants of N Australia-PDF.pdf` — PDF page 227; printed page 226; page-object index 2 (zero-based) |

Association evidence:

- **1:** Photograph positioned with the authored species account in the book proof.

Previous-edition instructions preserved on this volume:

- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/GG/Grevillea dunlopii (use 2022 photo)` — empty folder.
- `/Volumes/Crucial X10/SD/# A A A A COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/GG/Grevillea dunlopii (use 2022 photo)` — empty folder.

Other proof locations (corroborating layout/copies, not additional independent photographs):

- `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/COMPLETE NPNA 2022.A.pdf` — PDF page 114.
- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A A MANUSCRIPT/5. FULL DRAFT & EDITS JAN-MAR 2026/69c3d6be8a3f9_Native Plants of Northern Australia - book Y.pdf` — PDF page 114.

<a id="plant-7478d7ccc9fdb334"></a>

### Grevillea polyacida

**Book fallback; no supported loose original found.** Current gallery: empty.

No confidently associated loose original was located in the named collections or the wider filename/image comparison. The proof preserves a photograph with this species account.

| Order / inspection reference | Confidence | Source / resolution | Exact candidate path and locator |
| --- | --- | --- | --- |
| 1 / B-p231-o1 | **A** | book image; 566 × 566 px; low-resolution fallback: Flowers and leaves | `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/PROOF Native Plants of N Australia-PDF.pdf` — PDF page 231; printed page 230; page-object index 1 (zero-based) |

Association evidence:

- **1:** Photograph positioned with the authored species account in the book proof.

Previous-edition instructions preserved on this volume:

- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/GG/Grevillea polyacida (use 2022 photo)` — empty folder.
- `/Volumes/Crucial X10/SD/# A A A A COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/GG/Grevillea polyacida (use 2022 photo)` — empty folder.

Other proof locations (corroborating layout/copies, not additional independent photographs):

- `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/COMPLETE NPNA 2022.A.pdf` — PDF page 116.
- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A A MANUSCRIPT/5. FULL DRAFT & EDITS JAN-MAR 2026/69c3d6be8a3f9_Native Plants of Northern Australia - book Y.pdf` — PDF page 116.

<a id="plant-dc373e820f15679e"></a>

### Hibbertia dealbata

**Book fallback + uncertain loose leads.** Current gallery: empty.

Yellow flowers, narrow leaves and whole low shrub are visible, but both directories explicitly say Hibbertia cistifolia TBC, check H. dealbata. Retain as a competing-identification lead. Flower close-up P1210843 and habit P1210847 are complementary; do not resolve species from the similar flower colour.

| Order / inspection reference | Confidence | Source / resolution | Exact candidate path and locator |
| --- | --- | --- | --- |
| 1 / B-p244-o2 | **A** | book image; 566 × 566 px; low-resolution fallback: Flowers and leaves | `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/PROOF Native Plants of N Australia-PDF.pdf` — PDF page 244; printed page 243; page-object index 2 (zero-based) |
| 2 / L154 | **C** | loose photograph; 4000 × 3000 px; identification-review lead | `/Volumes/Crucial X10/SD/H H H/Hibbertia cistifolia TBC, check H. dealbata LNP 9.12.24 & 18.12.24/P1210843.JPG` |
| 3 / L158 | **C** | loose photograph; 4000 × 3000 px; identification-review lead | `/Volumes/Crucial X10/SD/H H H/Hibbertia cistifolia TBC, check H. dealbata LNP 9.12.24 & 18.12.24/P1210847.JPG` |

Association evidence:

- **1:** Photograph positioned with the authored species account in the book proof.
- **2:** Association through the named source folder or historical-name trail; Name spelling, abbreviation or historical combination needs retaining in provenance; Source explicitly uncertain or a mixed/incorrectly labelled collection.
- **3:** Association through the named source folder or historical-name trail; Name spelling, abbreviation or historical combination needs retaining in provenance; Source explicitly uncertain or a mixed/incorrectly labelled collection.

Previous-edition instructions preserved on this volume:

- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/HH/Hibbertia dealbata (use 2022 photo)` — empty folder.
- `/Volumes/Crucial X10/SD/# A A A A COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/HH/Hibbertia dealbata (use 2022 photo)` — empty folder.

Other proof locations (corroborating layout/copies, not additional independent photographs):

- `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/COMPLETE NPNA 2022.A.pdf` — PDF page 122.
- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A A MANUSCRIPT/5. FULL DRAFT & EDITS JAN-MAR 2026/69c3d6be8a3f9_Native Plants of Northern Australia - book Y.pdf` — PDF page 125.

<details>
<summary>All filename/folder leads for this account, including duplicates and uncertain material</summary>

Counts are files, not distinct photographs. A folder association is a search lead, not an approved allocation.

- `/Volumes/Crucial X10/SD/H H H/Hibbertia cistifolia TBC, check H. dealbata LNP 9,12,24` — 7 rendered files; 0 RAW files.
- `/Volumes/Crucial X10/SD/H H H/Hibbertia cistifolia TBC, check H. dealbata LNP 9.12.24 & 18.12.24` — 18 rendered files; 2 RAW files.

</details>

<a id="plant-daee0f7a8f6dba15"></a>

### Hibbertia fractiflexa subsp. brachyblastis

**Supported loose photographs + book fallback.** Current gallery: empty.

Exact full-rank filename names R A Kerrigan. A flower and hairy leaves are visible and this is a different view from the printed photograph. Preserve that credit and verify the source/permission before use; possession of this file does not establish the app's reuse rights.

| Order / inspection reference | Confidence | Source / resolution | Exact candidate path and locator |
| --- | --- | --- | --- |
| 1 / L161 | **A** | loose photograph; 500 × 451 px; gallery option | `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FOTOS/Hibbertia fractiflexa subsp. brachyblastis endemic R A Kerrigan.jpg` |
| 2 / B-p245-o1 | **A** | book image; 567 × 568 px; low-resolution fallback: Flower and leaves | `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/PROOF Native Plants of N Australia-PDF.pdf` — PDF page 245; printed page 244; page-object index 1 (zero-based) |

Association evidence:

- **1:** Species named in the filename.
- **2:** Photograph positioned with the authored species account in the book proof.

Previous-edition instructions preserved on this volume:

- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/HH/Hibbertia fractiflexa subsp. brachyblastis (use 2022 photo)` — empty folder.
- `/Volumes/Crucial X10/SD/# A A A A COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/HH/Hibbertia fractiflexa subsp. brachyblastis (use 2022 photo)` — empty folder.

Other proof locations (corroborating layout/copies, not additional independent photographs):

- `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/COMPLETE NPNA 2022.A.pdf` — PDF page 123.
- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A A MANUSCRIPT/5. FULL DRAFT & EDITS JAN-MAR 2026/69c3d6be8a3f9_Native Plants of Northern Australia - book Y.pdf` — PDF page 125.

<details>
<summary>All filename/folder leads for this account, including duplicates and uncertain material</summary>

Counts are files, not distinct photographs. A folder association is a search lead, not an approved allocation.

- `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FOTOS` — 1 rendered files; 0 RAW files.

</details>

<a id="plant-4b758bc02531a17f"></a>

### Lumnitzera littorea

**Book fallback; no supported loose original found.** Current gallery: empty.

No confidently associated loose original was located in the named collections or the wider filename/image comparison. The proof preserves a photograph with this species account.

| Order / inspection reference | Confidence | Source / resolution | Exact candidate path and locator |
| --- | --- | --- | --- |
| 1 / B-p269-o2 | **A** | book image; 567 × 380 px; low-resolution fallback: Red flowers | `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/PROOF Native Plants of N Australia-PDF.pdf` — PDF page 269; printed page 268; page-object index 2 (zero-based) |

Association evidence:

- **1:** Photograph positioned with the authored species account in the book proof.

Previous-edition instructions preserved on this volume:

- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/LL/Lumnitzera littorea (use 2022 flwr photo)` — empty folder.
- `/Volumes/Crucial X10/SD/# A A A A COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/LL/Lumnitzera littorea (use 2022 flwr photo)` — empty folder.

Other proof locations (corroborating layout/copies, not additional independent photographs):

- `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/COMPLETE NPNA 2022.A.pdf` — PDF page 135.
- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A A MANUSCRIPT/5. FULL DRAFT & EDITS JAN-MAR 2026/69c3d6be8a3f9_Native Plants of Northern Australia - book Y.pdf` — PDF page 138.

<a id="plant-08e9119a23671c38"></a>

### Melhania oblongifolia

**Book fallback + uncertain loose leads.** Current gallery: empty.

Two distinct JPEG exposures are copied into two nearly identical directories, with RAW companions. Both show leafy stems and small buds/fruit, rather than the conspicuous open flowers in the book. Folder says sp TBC poss oblongifolia. Identification remains unresolved.

| Order / inspection reference | Confidence | Source / resolution | Exact candidate path and locator |
| --- | --- | --- | --- |
| 1 / B-p280-o2 | **A** | book image; 566 × 570 px; low-resolution fallback: Flowers and leaves | `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/PROOF Native Plants of N Australia-PDF.pdf` — PDF page 280; printed page 279; page-object index 2 (zero-based) |
| 2 / L162 | **C** | loose photograph; 4000 × 3000 px; identification-review lead | `/Volumes/Crucial X10/SD/M M M/Melhania sp TBC poss oblongifolia Hwd Spr 25.2.25/_1270614.JPG` |

Association evidence:

- **1:** Photograph positioned with the authored species account in the book proof.
- **2:** Association through the named source folder or historical-name trail; Source explicitly uncertain or a mixed/incorrectly labelled collection.

Close alternatives, not extra recommended subjects:

- L163: `/Volumes/Crucial X10/SD/M M M/Melhania sp TBC poss oblongifolia Hwd Spr 25.2.25/_1270615.JPG` — 4000 × 3000 px.

Previous-edition instructions preserved on this volume:

- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/MM/Melhania oblongifolia (use 2022 photo)` — empty folder.
- `/Volumes/Crucial X10/SD/# A A A A COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/MM/Melhania oblongifolia (use 2022 photo)` — empty folder.

Other proof locations (corroborating layout/copies, not additional independent photographs):

- `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/COMPLETE NPNA 2022.A.pdf` — PDF page 140.
- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A A MANUSCRIPT/5. FULL DRAFT & EDITS JAN-MAR 2026/69c3d6be8a3f9_Native Plants of Northern Australia - book Y.pdf` — PDF page 144.

<details>
<summary>All filename/folder leads for this account, including duplicates and uncertain material</summary>

Counts are files, not distinct photographs. A folder association is a search lead, not an approved allocation.

- `/Volumes/Crucial X10/SD/M M M/(Melhania sp TBC poss oblongifolia Hwd Spr 25.2.25)` — 2 rendered files; 2 RAW files.
- `/Volumes/Crucial X10/SD/M M M/Melhania sp TBC poss oblongifolia Hwd Spr 25.2.25` — 2 rendered files; 2 RAW files.

</details>

<a id="plant-1adbdbfe7aa98e77"></a>

### Pityrodia lanuginosa

**Book fallback; no supported loose original found.** Current gallery: empty.

No confidently associated loose original was located in the named collections or the wider filename/image comparison. The proof preserves a photograph with this species account.

| Order / inspection reference | Confidence | Source / resolution | Exact candidate path and locator |
| --- | --- | --- | --- |
| 1 / B-p304-o2 | **A** | book image; 567 × 564 px; low-resolution fallback: Flowers and leaves | `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/PROOF Native Plants of N Australia-PDF.pdf` — PDF page 304; printed page 303; page-object index 2 (zero-based) |

Association evidence:

- **1:** Photograph positioned with the authored species account in the book proof.

Previous-edition instructions preserved on this volume:

- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/PP/Pityrodia lanuginosa (use 2022 photo)` — empty folder.
- `/Volumes/Crucial X10/SD/# A A A A COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/PP/Pityrodia lanuginosa (use 2022 photo)` — empty folder.

Other proof locations (corroborating layout/copies, not additional independent photographs):

- `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/COMPLETE NPNA 2022.A.pdf` — PDF page 152.
- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A A MANUSCRIPT/5. FULL DRAFT & EDITS JAN-MAR 2026/69c3d6be8a3f9_Native Plants of Northern Australia - book Y.pdf` — PDF page 158.

<a id="plant-23afe8e812e1e977"></a>

### Pogostemon stellatus

**Book fallback; no supported loose original found.** Current gallery: empty.

No confidently associated loose original was located in the named collections or the wider filename/image comparison. The proof preserves a photograph with this species account.

| Order / inspection reference | Confidence | Source / resolution | Exact candidate path and locator |
| --- | --- | --- | --- |
| 1 / B-p307-o1 | **A** | book image; 567 × 569 px; low-resolution fallback: Whole flowering herb | `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/PROOF Native Plants of N Australia-PDF.pdf` — PDF page 307; printed page 306; page-object index 1 (zero-based) |

Association evidence:

- **1:** Photograph positioned with the authored species account in the book proof.

Previous-edition instructions preserved on this volume:

- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/PP/Pogostemon stellatus (use 2022 photo)` — empty folder.
- `/Volumes/Crucial X10/SD/# A A A A COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/PP/Pogostemon stellatus (use 2022 photo)` — empty folder.

Other proof locations (corroborating layout/copies, not additional independent photographs):

- `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/COMPLETE NPNA 2022.A.pdf` — PDF page 154.
- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A A MANUSCRIPT/5. FULL DRAFT & EDITS JAN-MAR 2026/69c3d6be8a3f9_Native Plants of Northern Australia - book Y.pdf` — PDF page 160.

<a id="plant-bd2fbf83a83b5d60"></a>

### Rhizophora apiculata

**Book fallback; no supported loose original found.** Current gallery: empty.

No confidently associated loose original was located in the named collections or the wider filename/image comparison. The proof preserves a photograph with this species account.

| Order / inspection reference | Confidence | Source / resolution | Exact candidate path and locator |
| --- | --- | --- | --- |
| 1 / B-p312-o5 | **A** | book image; 567 × 383 px; low-resolution fallback: Flowers and leaves | `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/PROOF Native Plants of N Australia-PDF.pdf` — PDF page 312; printed page 311; page-object index 5 (zero-based) |

Association evidence:

- **1:** Photograph positioned with the authored species account in the book proof.

Previous-edition instructions preserved on this volume:

- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/RR/Rhizophora apiculata (use 2022 photo)` — empty folder.
- `/Volumes/Crucial X10/SD/# A A A A COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/RR/Rhizophora apiculata (use 2022 photo)` — empty folder.

Other proof locations (corroborating layout/copies, not additional independent photographs):

- `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/COMPLETE NPNA 2022.A.pdf` — PDF page 156.
- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A A MANUSCRIPT/5. FULL DRAFT & EDITS JAN-MAR 2026/69c3d6be8a3f9_Native Plants of Northern Australia - book Y.pdf` — PDF page 164.

<a id="plant-22b9ddafbe2abd1c"></a>

### Solanum asymmetriphyllum

**Book fallback + uncertain loose leads.** Current gallery: empty.

Twelve JPEGs with RAW partners in a folder explicitly labelled Solanum sp echinatum TBC (poss asymmetriphyllum). Flower/leaf detail, prickly fruit/calyx and foliage views are useful for an identification review. Do not allocate until the competing name is resolved. Most flower exposures repeat the same view.

| Order / inspection reference | Confidence | Source / resolution | Exact candidate path and locator |
| --- | --- | --- | --- |
| 1 / B-p321-o2 | **A** | book image; 566 × 382 px; low-resolution fallback: Flower, fruit and foliage | `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/PROOF Native Plants of N Australia-PDF.pdf` — PDF page 321; printed page 320; page-object index 2 (zero-based) |
| 2 / L166 | **C** | loose photograph; 4000 × 3000 px; identification-review lead | `/Volumes/Crucial X10/SD/EXOTIC OR ID TBC/Solanum sp echinatum TBC (poss asymmetriphyllum), VRD 25.4.25/_1290768.JPG` |
| 3 / L172 | **C** | loose photograph; 4000 × 3000 px; identification-review lead | `/Volumes/Crucial X10/SD/EXOTIC OR ID TBC/Solanum sp echinatum TBC (poss asymmetriphyllum), VRD 25.4.25/_1290774.JPG` |
| 4 / L175 | **C** | loose photograph; 4000 × 3000 px; identification-review lead | `/Volumes/Crucial X10/SD/EXOTIC OR ID TBC/Solanum sp echinatum TBC (poss asymmetriphyllum), VRD 25.4.25/_1290777.JPG` |

Association evidence:

- **1:** Photograph positioned with the authored species account in the book proof.
- **2:** Association through the named source folder or historical-name trail; Source explicitly uncertain or a mixed/incorrectly labelled collection.
- **3:** Association through the named source folder or historical-name trail; Source explicitly uncertain or a mixed/incorrectly labelled collection.
- **4:** Association through the named source folder or historical-name trail; Source explicitly uncertain or a mixed/incorrectly labelled collection.

Previous-edition instructions preserved on this volume:

- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/SS/Solanum asymmetriphyllum (use 2022 photo. DELETE MAP)` — empty folder.
- `/Volumes/Crucial X10/SD/# A A A A COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/SS/Solanum asymmetriphyllum (use 2022 photo. DELETE MAP)` — empty folder.

Other proof locations (corroborating layout/copies, not additional independent photographs):

- `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/COMPLETE NPNA 2022.A.pdf` — PDF page 161.
- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A A MANUSCRIPT/5. FULL DRAFT & EDITS JAN-MAR 2026/69c3d6be8a3f9_Native Plants of Northern Australia - book Y.pdf` — PDF page 169.

<details>
<summary>All filename/folder leads for this account, including duplicates and uncertain material</summary>

Counts are files, not distinct photographs. A folder association is a search lead, not an approved allocation.

- `/Volumes/Crucial X10/SD/EXOTIC OR ID TBC/Solanum sp echinatum TBC (poss asymmetriphyllum), VRD 25.4.25` — 12 rendered files; 0 RAW files.
- `/Volumes/Crucial X10/SD/EXOTIC OR ID TBC/Solanum sp echinatum TBC (poss asymmetriphyllum), VRD 25.4.25/RAW` — 0 rendered files; 12 RAW files.

</details>

<a id="plant-3439591df5e65edc"></a>

### Terminalia arostrata

**Supported loose photographs + book fallback.** Current gallery: empty.

Four JPEGs and four RAW files show the same arranged group of dry nuts on the ground. Strong species-folder association but no attached leaf/branch voucher visible. Select one, preferably _1310277, to complement the book's foliage image; do not count four as four different gallery subjects.

| Order / inspection reference | Confidence | Source / resolution | Exact candidate path and locator |
| --- | --- | --- | --- |
| 1 / B-p334-o2 | **A** | book image; 390 × 567 px; low-resolution fallback: Foliage | `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/PROOF Native Plants of N Australia-PDF.pdf` — PDF page 334; printed page 333; page-object index 2 (zero-based) |
| 2 / L178 | **B** | loose photograph; 4000 × 3000 px; gallery option | `/Volumes/Crucial X10/SD/T T T/Terminalia arostrata/Terminalia arostrata (nuts) 26.6.25/_1310277.JPG` |

Association evidence:

- **1:** Photograph positioned with the authored species account in the book proof.
- **2:** Association through the named source folder or historical-name trail.

Close alternatives, not extra recommended subjects:

- L176: `/Volumes/Crucial X10/SD/T T T/Terminalia arostrata/Terminalia arostrata (nuts) 26.6.25/_1310275.JPG` — 4000 × 3000 px.
- L177: `/Volumes/Crucial X10/SD/T T T/Terminalia arostrata/Terminalia arostrata (nuts) 26.6.25/_1310276.JPG` — 4000 × 3000 px.
- L179: `/Volumes/Crucial X10/SD/T T T/Terminalia arostrata/Terminalia arostrata (nuts) 26.6.25/_1310278.JPG` — 4000 × 3000 px.

Previous-edition instructions preserved on this volume:

- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/TT/Terminalia arostrata (use 2022 photo)` — empty folder.
- `/Volumes/Crucial X10/SD/# A A A A COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/TT/Terminalia arostrata (use 2022 photo)` — empty folder.

Other proof locations (corroborating layout/copies, not additional independent photographs):

- `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/COMPLETE NPNA 2022.A.pdf` — PDF page 167.
- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A A MANUSCRIPT/5. FULL DRAFT & EDITS JAN-MAR 2026/69c3d6be8a3f9_Native Plants of Northern Australia - book Y.pdf` — PDF page 178.

<details>
<summary>All filename/folder leads for this account, including duplicates and uncertain material</summary>

Counts are files, not distinct photographs. A folder association is a search lead, not an approved allocation.

- `/Volumes/Crucial X10/SD/T T T/Terminalia arostrata/Terminalia arostrata (nuts) 26.6.25` — 4 rendered files; 4 RAW files.

</details>

<a id="plant-11757202d24dd423"></a>

### Xerochrysum borealis

**Book fallback; no supported loose original found.** Current gallery: empty.

One photograph copied twice, labelled Xerochrysum sp 3 Fraser Is IM; its folder explicitly says TBC prob not in NT. The broader parent uses boreale / former Helichrysum bracteatum, but that does not establish this photograph's taxon. Keep as a rejected-for-now lead, not an available account image; the book photograph is the defensible recovery option.

| Order / inspection reference | Confidence | Source / resolution | Exact candidate path and locator |
| --- | --- | --- | --- |
| 1 / B-p358-o1 | **A** | book image; 566 × 573 px; low-resolution fallback: Flowering herb | `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/PROOF Native Plants of N Australia-PDF.pdf` — PDF page 358; printed page 357; page-object index 1 (zero-based) |

Association evidence:

- **1:** Photograph positioned with the authored species account in the book proof.

Rejected for this account: `/Volumes/Crucial X10/SD/X X X/Xerochrysum boreale (prev Helichrysum bracteatum) WA, NT/Xerochrysum sp TBC prob not in NT/Xerochrysum sp 3 Fraser Is IM.jpg` (L180). The competing locality/name is unresolved.

Previous-edition instructions preserved on this volume:

- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/XX/Xerochrysum borealis (use 2022 photo)` — empty folder.
- `/Volumes/Crucial X10/SD/# A A A A COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/XX/Xerochrysum borealis (use 2022 photo)` — empty folder.

Other proof locations (corroborating layout/copies, not additional independent photographs):

- `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/COMPLETE NPNA 2022.A.pdf` — PDF page 179.
- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A A MANUSCRIPT/5. FULL DRAFT & EDITS JAN-MAR 2026/69c3d6be8a3f9_Native Plants of Northern Australia - book Y.pdf` — PDF page 193.

<details>
<summary>All filename/folder leads for this account, including duplicates and uncertain material</summary>

Counts are files, not distinct photographs. A folder association is a search lead, not an approved allocation.

- `/Volumes/Crucial X10/SD/X X X/Xerochrysum boreale (prev Helichrysum bracteatum) WA, NT/Xerochrysum sp TBC prob not in NT` — 2 rendered files; 0 RAW files.

</details>

<a id="plant-5a696f854b4f436f"></a>

### Zornia prostrata

**Book fallback; no supported loose original found.** Current gallery: empty.

No confidently associated loose original was located in the named collections or the wider filename/image comparison. The proof preserves a photograph with this species account.

| Order / inspection reference | Confidence | Source / resolution | Exact candidate path and locator |
| --- | --- | --- | --- |
| 1 / B-p360-o1 | **A** | book image; 566 × 566 px; low-resolution fallback: Prostrate flowering plant | `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/PROOF Native Plants of N Australia-PDF.pdf` — PDF page 360; printed page 359; page-object index 1 (zero-based) |

Association evidence:

- **1:** Photograph positioned with the authored species account in the book proof.

Previous-edition instructions preserved on this volume:

- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/ZZ/Zornia prostrata (use 2022 photo)` — empty folder.
- `/Volumes/Crucial X10/SD/# A A A A COMPLETE NPNA NEW ED 2026/A. A. PHOTOS AND IMAGES/D. PLANT DESCRIPTIONS GENERA A-Z/ZZ/Zornia prostrata (use 2022 photo)` — empty folder.

Other proof locations (corroborating layout/copies, not additional independent photographs):

- `/Volumes/Crucial X10/HD/AA NATIVE PLANTS OF NA 2021/FINAL FULL DRAFT/COMPLETE NPNA 2022.A.pdf` — PDF page 180.
- `/Volumes/Crucial X10/HD/# COMPLETE NPNA NEW ED 2026/A A MANUSCRIPT/5. FULL DRAFT & EDITS JAN-MAR 2026/69c3d6be8a3f9_Native Plants of Northern Australia - book Y.pdf` — PDF page 194.

## Recovery limits

The highest-resolution proof selected here is generally the older single-page `PROOF Native Plants of N Australia-PDF.pdf`. The other editions mostly contain resampled versions, often around 284 pixels wide. Full-size raster rendering of a PDF page would not restore missing photographic detail. Composite images should be separated only with deliberate crops; do not present a tiny inset as a full-resolution original.

A perceptual comparison against the volume’s rendered images did not recover a high-resolution loose master for the remaining book-only photographs. It found repeated proof images and some superficial false similarities, which were rejected after inspection. Changed crops, edits, unlabelled photographs and undeveloped RAWs mean this remains a bounded negative finding.
