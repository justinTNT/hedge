# Glossary and references — 24 September 2026

The app now incorporates the complete accepted-view glossary and both reference
lists from `4. NPNA 2026 REF, BIB, GLOSS, FAM LIST, ENDEM LIST, INDEX.docx` under
the local Brock archive. There are 95 definitions, 25 numbered Aboriginal plant-use
references and 120 bibliography paragraphs. All 105 account-level `Ref:` fields
remain intact. Bibliography paragraphs retain their original boundaries and text,
including combined or incomplete entries; the count is not a claim of 120 distinct
publications.

## Reading the guide

Every matched occurrence in account prose, headings, attributes, taxon aliases and
captions receives a dotted underline. Hover or keyboard focus shows its definition;
tap/click pins it until another tap, Escape, an outside click, navigation or page
scroll. Popups stay within the viewport and can scroll when a definition is long.
Navigation controls keep their usual action. Glossary popup text is not recursively
annotated into further popups.

Matching is case-insensitive, uses whole words, includes explicit noun plurals and
recorded alternatives such as lamina/alluvial/genera, and prefers longer phrases
such as “compound leaf” and “female flower”. Hyphen variants and “bipinnate” are
recognized. No arbitrary stemming, synonym inference or HTML rewriting is used.
The compiled matcher preserves all source characters; it finds 7,159 occurrences
across the current account prose. Words absent from the source glossary do not get
invented definitions. Aliases remain editable in admin using `|` separators.

`/glossary` searches terms, aliases and definitions. It includes the DOCX's dioecious
flower illustration, retaining its attribution in the definition. The labelled
flower diagram is reproduced from `COMPLETE NPNA 2022.A.pdf`, PDF page 184 / printed
page 366, because the 2026 entry refers to a diagram absent from that DOCX. Its
caption distinguishes the illustration's edition. The reviewed crop includes the
printed labels, not only the underlying image object. Source hashes and reproduction
coordinates are pinned in `data/glossary-illustrations.json`.

`/references` searches all imported source entries. A one- or two-digit search finds
that numbered usage reference. Numbered parenthetical citations in Aboriginal-use
text expose the corresponding complete citation; numbers in measurements, dates or
other sections are not treated as usage citations. Explicit short names such as
Flora NT link to their known bibliography entry; unmatched named sources remain
verbatim, without invented full citations or destinations.

## Source issues retained for editorial review

- **Reference 22 is missing** from both the 2026 DOCX/PDF list and the 2022 A proof.
  *Melaleuca leucadendra* cites it once. Its popup explains that the source entry is
  missing; numbering is preserved. Adding/publishing reference 22 through admin
  resolves the popup and removes the global gap notice.
- Bibliography paragraph 49 combines two Byrnes publications (1984 and 1985).
- Paragraph 102 leaves the McKay (2017) entry unfinished after “and”. Paragraph 104
  begins with “Lindley McKay, Darwin.” attached to the Midgley entry; McNamara occurs
  between them. These require an editorial correction rather than silently joining
  or rewriting the text.
- Account references can name additional sources absent from the main bibliography.
  They remain visible on the relevant account; their full publication metadata has
  not been guessed or backfilled from external data.

These issues are also emitted in private `data/import-report.json`. The complete
definition/citation text is in the generated local `data/catalogue-source.json` and
seed. Original source files were not modified.

## Ownership and verification

GlossaryTerm and SourceReference are app-owned domain records with generated admin
CRUD. Glossary and reference public DTOs exclude source paths/evidence. The existing
in-memory catalogue loads them in the same D1 batch as plants/photos/maps and includes
them in its revision. Owner changes invalidate the cache; unpublished/deleted entries
do not enter the public guide. Re-import uses insert-once IDs and preserves owner
edits. No framework change or new reusable module is required.

Validation: 16 importer tests and 25 compiled client/Worker tests pass; matching,
longest phrases, plurals, preserved punctuation, unresolved references, publication,
revision invalidation and private evidence exclusion are covered. All 95 live
definitions and 145 reference records were compared with the imported source, and
both served illustration assets were byte-checked. Every pre-existing plant, photo
and map row remained unchanged by the seed. Chrome verified inline definitions,
keyboard focus/Escape, tap toggling and a phone-width popup. Changes are local;
there was no remote deployment.
