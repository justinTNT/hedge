# Native Plants: descriptive search without diluting taxonomy

Proposed 4 October 2026. Revised and implemented 1 November 2026: the matcher runs on the device over
the accounts the offline cache already holds (see "Storage, API and client boundaries" below and
[HEDGE-offline.md](HEDGE-offline.md)); the interaction design is unchanged.

Work in `/Users/jtnt/Play/hedge-native-plants` on `native-plants`. The branch was
rebased onto local `main` at `7378d8e`. Its previous tip, `673e879`, is retained as
`backup/native-plants-before-main-20261004-673e879`. Rebase verification also
requires adapting the app's OAuth registration to main's OIDC configuration;
Google and GitHub remain the offered providers, with email sign-in disabled.

## Aim and evidence

Let a person search for an observed feature, such as **colourful bark**, while
keeping scientific names, common names, genera and families the clearest route
to a known plant or taxon. Descriptive matches must never crowd name suggestions
or outrank a matching taxon because its name is mentioned in another account.

The source catalogue currently contains 531 accounts. Their descriptive fields
total about 602 KB of UTF-8 text before indexing or compression. Corymbia
foelscheana's `features` field begins “colourful bark, scaly and mostly red-grey
on lower trunk”. It is the only account containing the literal phrase
“colourful bark” in those fields. This is a source-text match, not an inferred
botanical trait or a reason to manufacture additional tags.

Current implementation:

- `apps/native-plants/src/Core/Catalogue.fs`: name scoring, suggestions and
  facets. Searches scientific/common names, recorded aliases, family and genus.
- `src/Client/App.fs`: suggestions and browse filtering run locally over
  `GetCatalogue.Plants`. The client does not currently call `SearchPlants`.
- `src/Server/CatalogueStore.fs`: an immutable published snapshot already holds
  all account sections in `ById`, with revision/invalidation handling.
- `src/Server/Handlers.fs`: `SearchPlants` currently applies the same name filter
  to the cached cards. Merely changing this endpoint would not change the UI.

## Search interaction

Keep one search box. Do not introduce a mandatory name/keyword mode switch.

### While typing: names and taxonomy only

- Preserve immediate local suggestions. Descriptive search neither delays nor
  replaces them, and no description request is made for each keystroke.
- Represent species, genus and family suggestions explicitly. A genus/family
  suggestion is one labelled choice, not seven species filling every slot.
- Rank exact scientific/common/recorded-alias names and exact taxa first, then
  name prefixes and the existing all-token name matches. Reserve visibility for
  an exact genus/family even when many species match that prefix.
- Selecting a species opens its account. Selecting a genus/family opens the
  existing browse route with that filter visible; the selected name is consumed
  as a taxon filter rather than left as a redundant keyword.
- Keep independent filters where compatible. Selecting a family clears an
  incompatible genus; selecting a genus establishes its recorded family.
- Enter opens a suggestion only when the user deliberately selected it with
  keyboard navigation. Otherwise Enter submits the text. Never auto-open the
  first species for a descriptive query.
- Keep the separate `/taxonomy` browser focused on family/genus names.

### After submission: separate name matches from descriptive matches

Use two result groups, with distinct counts and one occurrence of each species:

1. **Name and taxonomy matches**, using only the plant's own names and taxonomy.
   These always come first. Preserve their existing ranking where practical.
2. **Matches in descriptions**, for additional species. When the first group is
   nonempty, this group starts collapsed behind a clearly labelled count/action.
   When there are no name matches, show descriptive results immediately.

An exact `Corymbia` query therefore presents Corymbia plants without interleaving
other plants whose notes mention that genus. A `colourful bark` query immediately
presents Corymbia foelscheana with a supporting excerpt. A species matching both
groups appears once, in the name group.

Keep selected family/genus and other facets visible and apply them to both
groups. No automatic removal of filters to manufacture results. Back/forward,
shareable `q`/facet URLs and clear-filter behavior remain predictable. Do not
interpret `Corymbia colourful bark` as an implicit genus filter in the first
version; the explicit genus suggestion/filter provides that workflow.

Each descriptive result includes the section label and a short verbatim excerpt
with matched terms highlighted as text. Link to the account's existing section
anchor. For the example, show “Recognise this plant — colourful bark, scaly…”.
The excerpt must retain enough context for qualifications or negation to remain
visible. Search finds what an account says; it does not certify identification.

Use ordinary loading, empty and error states for the descriptive group. A
network failure must not masquerade as zero matches or erase usable local name
results. Use accessible disclosure and combobox semantics on web and Android.

## Matching and ranking

Index published species accounts, preserving field boundaries. Include recognition
features, habit, bark, leaves, phyllodes, flowers, fruit, flowering/fruiting times,
habitat, distribution text, cultivation, traditional uses and notes. Names remain
in their own matching path. Exclude bibliography, provenance, photographer
credits, glossary definitions and all personal notes, ID requests and unpromoted
contributions. Do not turn glossary definitions into claims about a species.

For the initial matcher:

- Normalize case, whitespace, punctuation and hyphens, retaining original text
  and offsets for display. Match words, so `red` does not match `coloured`.
- Support conservative spelling equivalents such as colourful/colorful and a
  small explicit set of word forms. No broad stemming or inferred botanical
  synonym expansion in this version.
- Require every meaningful query token to match within one section. This avoids
  constructing “red flowers” from “red bark” plus “white flowers” elsewhere.
  Do not silently fall back to OR when there are no results.
- Rank exact phrases ahead of nearby all-token matches. Within those tiers,
  favor recognition and morphological sections over cultivation and incidental
  mentions in notes. A phrase's frequency must not outrank a name match.
- Choose one best evidence excerpt per plant. Use scientific name and stable ID
  as deterministic tie breakers. Do not invent a percentage-confidence score.
- Retain the existing 160-character query bound and add bounded token/output
  limits. Blank or punctuation-only queries retain ordinary catalogue browsing.

Full natural-language parsing, fuzzy botanical spelling, semantic/vector search,
new curated trait fields and searches of a user's own notes are later decisions.
Do not add a generic Hedge search framework or an external search service here.

## Storage, API and client boundaries

SQLite/D1 remains the editable source and `CatalogueStore` the published snapshot. The matcher is
the pure core module `src/Core/Descriptions.fs` (normalisation, equivalents, section matching,
ranking, excerpts), compiled into both client and server.

Revised 1 November 2026. The original plan computed descriptive matches on the server, so as not to
send every full account to the phone merely to enable search. The offline work changed the premise:
the device now keeps every published account (about 0.6 MB of text) so that each page opens without
signal, refreshed with the catalogue revision. Descriptive search therefore runs on the device over
that same saved snapshot (`src/Client/Search.fs` reads the cached accounts; the browse page calls the
matcher). It works in the field with no signal, answers in milliseconds, and needs no API extension,
request-generation keys or revision reconciliation: the result is a pure function of the snapshot
the page already shows. The cost accepted is that ranking changes ship with the client, so on
Android they wait for an app update; and before the first warm-up completes the search covers only
the accounts saved so far, which the results say plainly ("Searched the N of M accounts saved on
this device so far").

Autocomplete, blank-query browsing and submitted searches are all local. `SearchPlants` keeps its
name filter; it may adopt the same core matcher later if an API consumer wants descriptive results.
A single bulk accounts endpoint (one request instead of one per account for the warm-up) is a
worthwhile follow-on but not a prerequisite.

## Delivery and verification

1. Record representative source fixtures and expected grouping/ranking first.
2. Extract/retain a dedicated name matcher; add the pure section matcher and
   snapshot-owned index without broadening autocomplete's input.
3. Wire submitted searches into the browse page over the saved accounts, with grouped evidence
   and the saved-so-far notice; no API change.
4. Add genus/family suggestion targets and result excerpts/disclosure. Check
   keyboard, tap, narrow-screen layout and section-anchor navigation.
5. Run the app build/tests and Android web build. Review in browser/on device
   before deciding to deploy or rebuild/distribute the APK.

Acceptance examples and guards:

| Query or action | Expected behavior |
| --- | --- |
| `colourful bark` | Corymbia foelscheana is the leading descriptive result; excerpt uses the recognition text. |
| `colorful bark` | Same account via the explicit spelling equivalence. |
| `Corymbia foelscheana` | Its own account is first in name results; accounts mentioning it cannot displace it. |
| `Corymbia` | Exact genus choice remains visible; results show that genus ahead of any descriptive mentions. |
| `Myrtaceae` | Exact family choice remains visible; family browsing works as before. |
| `wattle`, recorded former names, partial scientific names | Existing name discovery continues to work immediately. |
| `red flowers` against a synthetic red-bark/white-flower account | Does not produce a cross-section match. |
| `colourful bark` plus an incompatible family filter | Zero results within the filter; no silent broadening. |
| Clear query, change filters, go back, quickly submit a second query | Correct URL/view; earlier network responses are ignored. |
| Edit/unpublish a matching account | Index, counts and snippets refresh with the published snapshot; removed content does not linger. |
| Put a unique phrase in a private note or ID request | No public search result or snippet reveals it. |
| No network at all | Name and descriptive results both work from the saved accounts; coverage is stated when the warm-up is incomplete. |

Measure warm-query latency, cold snapshot/index cost and response sizes on the
actual source dataset. Add focused tests for grouping, token boundaries,
matching excerpts, filters, publication/revision handling and stale requests.
Use existing auth and contribution tests to guard the rebase integration;
search does not alter their access rules.

Related: [Native Plants app README](../apps/native-plants/README.md),
[Hedge boundary integration](NATIVE-PLANTS-hedge-boundary-refactor.md), and
[field notes and identification](NATIVE-PLANTS-field-notes-and-identification.md).
