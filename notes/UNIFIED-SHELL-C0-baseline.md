# C0 — Baseline and build-gate evidence

Branch `unified-shell-consolidation`, off `main` @ `72af14c`. Date 2026-09-16.
This records the C0 baseline; it is NOT full acceptance (per the work order, known
defects assigned to later checkpoints may remain).

## Environment
- .NET SDK **10.0.400** (no `global.json`; documented, not changed).
- node **v25.5.0**.
- Commands: `./test.sh`, `./scripts/build-all.sh`, `cd apps/microblog && npm run build:extension:all`.

## Passing baseline (all green)
- **`./test.sh`** — gen byte-stability (all apps + module surfaces), SchemaCodec round-trip,
  check-sql fresh-vs-migrated (blog + articles), client build matrix (+ extension type-check),
  host-context probe, scaffold pipeline. "All tests passed."
- **`./scripts/build-all.sh`** — every app Server+Client type-checks; extension `ext-tc`
  (dotnet), `ext-chr` and `ext-ffx` (clean Fable compile + esbuild bundle + package +
  artifact verification). "ALL GREEN."
- Both extension packages (`dist-chrome`, `dist-firefox`) build and package.

## C0 deliverables done
- Baseline captured (above).
- **Build gate extended** (commit on this branch): `build-all.sh` now runs a real extension
  Fable compile + bundle + package for Chrome and Firefox, verifies each dist has a non-empty
  `manifest.json`/`popup.js`/`popup.html`/`background.js`, and scans build output for Fable
  restore/compile errors even on exit 0. (test.sh keeps the fast `dotnet` type-check.)

## Recent-fix coverage + behavioral-fixture assignment
All four are currently **compile/build-verified** (the gates above) and were verified live in
prior sessions. A *behavioral* race-fixture (valid + obsolete completion) needs an injectable
boundary that does not exist yet; each is assigned to the checkpoint that introduces it:

| Fix | Now | Behavioral fixture lands in |
| --- | --- | --- |
| Archive-key block incl. `%2F`-encoded | live-verified (404 on real archive blob) + build | **C4** — the literal prefix becomes a pure `BlobServingPolicy`, unit-testable for old/new/encoded keys |
| Extension destination pinning | build | **C2** — typed transport + fake transport makes "site changed mid-submit still uses original destination" assertable |
| srcset with commas in CDN URLs | build | **C2** — the parser is inline JS in Popup.fs's injected `Emit`; extract to a shared testable unit with the extension adapter |
| Admin FormSeq guard | build | **C1** — `Admin.App.update` isn't importable under node (Feliz/Router/HMR browser globals); testable once update logic is DOM-free |

## Remaining shell/architecture obligations (reproduced by review, not failing gates)
Assigned to owning checkpoints:
- **C1** — DOM-only editor drafts; `articlesStaleDrop`/`blogStaleDrop` shell enumeration of child
  results; reverse-order response handling; late-write outcome retention; resource-scope
  disposal/cancellation; per-request validity with cached feeds.
- **C2** — ambient `Client.Api` dependency in generated clients; UI inferring meaning from
  arbitrary error objects; extension referencing app-level Models/empty ClientGen.
- **C3** — content modules importing `Server.Env`/`Server.Identity`; no module `Services` record
  or generated handler binding.
- **C4** — app-local snapshot handlers (belong in Blog); hard-coded `archive/` prefix in the
  framework blob route.
- **C5** — duplicate module-owned identity ownership; content drafts depending on an editor
  surviving navigation.

## Not run (needs browser / user)
- The full **browser acceptance matrix** (shell paginate→switch→draft→Back/Forward→submit-while-
  navigating; OAuth return/focus; deep links; asset MIME) and **installed-extension smoke tests**.
  Claude-in-Chrome is unavailable this session; these are the actual acceptance gates for
  C1/C4/C5 and require the user's browser (or an enabled sandboxed browser) at each checkpoint.
