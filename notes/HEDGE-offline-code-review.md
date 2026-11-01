# Comprehensive Code Review: Offline Reads, Writes and Media (HEDGE-offline)

**Date:** 1 November 2026  
**Scope:** Uncommitted implementation of `notes/HEDGE-offline.md` across framework (`packages/hedge`), app runtime (`apps/native-plants`), build tooling, mobile seeding, preview gate, and test suites.  
**Review Status:** Verified via clean compilation (`npm run build`, `npm run build:mobile`) and all 144 automated tests passing.

---

## 1. Executive Summary & Verdict

The implementation delivers the complete offline architecture specified in `notes/HEDGE-offline.md`. It provides a clean separation of concerns:
- **Framework owns protocol:** Durable Key-Value store (`IndexedDB`), transparent read-through cached transports (`network-first`, `cache-first`), FIFO scoped outbox with replay/park/retry state machines, and resumable media prefetch (`Client.Offline`).
- **App owns policy:** Endpoint routing, scoped identity bindings, snapshot folding rules, local optimistic notebook overlays, media pack definitions, connection constraints (Wi-Fi vs mobile data), and pure client-side descriptive text search.

### Verdict: Ready for Production / Preview Deployment
The code is high quality, idiomatic F# (Fable), robustly handles JS interop, adheres to strict identity scoping, and avoids service-worker pitfalls by operating beneath the HTTP transport layer. 

A few minor edge cases and optimization opportunities were identified around progress reporting during media pack downloads and scope referencing during delivery callbacks, detailed in Section 5.

---

## 2. Architecture & Design Alignment

| Architectural Requirement (`HEDGE-offline.md`) | Implementation Mechanism | Status |
|---|---|---|
| **Bypass Service Worker limitations** (CapacitorHttp native fetch bypass & `no-store` preview gate) | Custom transport decorators in `Client.Offline` over `Hedge.Http.Transport` using IndexedDB | Verified |
| **Reads are transparent** | `cachedTransport` and `cacheFirstTransport` intercept GET requests; typed clients remain unchanged | Verified |
| **Writes are explicit** | Durable `Outbox` with typed enqueuing, optimistic `overlay`, and explicit status markers | Verified |
| **Identity-bound security** | Cache keys, outbox entries, and personal media partitioned by `scope` (`provider:id`) | Verified |
| **First launch with no network** | `scripts/seed-mobile.mjs` generates 531 accounts into bundle; `Client.Seed.ensure` primes cache before initial load | Verified |
| **Bulk warm-up** | `GetAccounts` endpoint (`/api/plants/accounts`) primes per-account read cache via `primeRead` | Verified |
| **Client-side descriptive search** | Pure tokenizer and matcher in `NativePlants.Descriptions`, fed from primed accounts via `Client.Search` | Verified |
| **Zero-manifest media packs** | `Client.Packs.plan` derives required URLs dynamically from cached account responses | Verified |
| **Server-side replay idempotency** | `mutateCommand` in `Server/Contributions.fs` detects identical replayed creates and answers 200 rather than 409 | Verified |
| **Android OAuth login in preview** | `preview-gate.mjs` exempts GET handshake routes (`/login`, `/callback`, `/mobile/return`) | Verified |

---

## 3. Detailed Component Review

### 3.1 Framework Offline Runtime (`packages/hedge/src/Client/Offline.fs`)
- **`Store` abstraction:** Simple, well-typed contract supporting `Get`, `Put`, `Delete`, and `Keys` (prefix listing). `indexedDbStore` uses an explicit versioned single object store (`kv`) with `IDBKeyRange.bound($0, $0 + '\uffff')` for prefix ranges. Fallback to `memoryStore` ensures graceful degradation in non-browser/test environments.
- **`cachedTransport`:**
  - GET requests only; non-GET requests pass through immediately.
  - Generates deterministic keys: `read/<scope>/GET <path>?<query>`.
  - Seamlessly revalidates via `If-None-Match` when a cached copy carries an `ETag`; on `304 Not Modified`, updates the `storedAt` timestamp and returns the cached body with status `200` so Thoth decoders succeed.
  - Never masks `4xx` responses (auth expiry or missing records propagate directly).
  - Falls back to cached copy on `TransportFailure` or `>= 500`.
- **`createOutbox`:**
  - Chronological ordering is preserved via padded timestamps and sequence numbers: `outbox/<scope>/<padded-timestamp>-<seq>`.
  - Replay state machine cleanly distinguishes between `Delivered`, `Retry` (which sets `blocked = true` to halt further replay until network recovers), `Park` (isolates individual conflicts/quotas without blocking subsequent independent items), and `Discard`.
  - Concurrent flushes safely join via `running: JS.Promise<OutboxItem list> option ref`.
  - Announcements via `onDelivered` run *before* attachment removal, ensuring apps can migrate uploaded bytes to permanent storage.

### 3.2 Code Generation & Media Introspection (`Gen/Program.fs`)
- **`Requests` record:** Generated alongside `Client` in `ClientGen.fs`. Decouples wire request generation from transport execution, enabling `primeRead`, outbox persistence, and cache queries to share the exact URLs and query encodings as live calls.
- **`Codecs.Images`:** Computes a fixpoint over the domain type graph to detect all records carrying `Hedge.Interface.Image`. Generates recursive extractors (`identifyNoteResponse`, `plantDetail`, etc.) without requiring runtime reflection or manual lists.
- **Header forwarding:** `Client/Api.fs` updated to forward response headers as `(string * string) list`, enabling generic ETag revalidation.

### 3.3 Device Seeding & Startup Resilience (`Seed.fs`, `seed-mobile.mjs`, `entry.mjs`)
- **Build-time seed generation:** `scripts/seed-mobile.mjs` executes the compiled server worker over local SQLite data, asserting revision alignment and 100% catalogue coverage before writing `catalogue.json` and `accounts.json` (approx. 2.1 MB total).
- **Startup sequence (`App.fs`):** `init` sequences `Client.Seed.ensure ()` before dispatching `load` or `currentPlant`. This eliminates race conditions on cold launch where an uncached read might fail before the seed is primed.
- **Mobile preview unlock (`entry.mjs`):** Network failures during preview credential verification fall back to accepting the saved password, allowing offline launch without locking out the user.

### 3.4 Descriptive Search (`Core/Descriptions.fs`, `Client/Search.fs`)
- **Pure matching engine:** Operates entirely client-side on cached plant accounts (`(string * Section list) list`).
- **Linguistic precision:**
  - American-to-British spelling folding (`color` → `colour`, `gray` → `grey`, `fiber` → `fibre`).
  - Strict morphology: reduces basic trailing `-s` plurals, folds `leaves` → `leaf`.
  - Ignores stop words, limits query to 8 meaningful tokens.
  - Excludes non-descriptive sections (`References`).
  - Ranks phrase matches before scattered matches, and morphological sections (`Recognise this plant`, `Bark`, `Leaves`) before habitat/season notes.
- **UI integration (`App.fs`):**
  - Only executes on query submission (not keystrokes), avoiding layout churn.
  - Respects active category/facet filters.
  - Automatically expands description matches if no plant name matched; collapses them if name matches exist.
  - Links directly to section anchors (`#section-bark`).

### 3.5 Write Synchronization & Notebook Policy (`Client/Sync.fs`)
- **Folding rules:** Incoming changes fold into queued ones:
  - Edits replace pending creates/edits.
  - Deleting an unsent create or unsent upload cancels both and deletes stored blob bytes immediately.
  - Hero selections for the same plant replace previous selections.
- **Optimistic Overlay:** `overlay` applies pending changes on top of `PersonalSnapshot`, adjusting `NoteCapacity` and `PhotoCapacity` so device limits reflect pending writes while away.
- **Reconciliation:** `reconcile` automatically detects when a refreshed server snapshot contains items previously parked or pending, removing them from the outbox.
- **Server Idempotency (`Server/Contributions.fs`):** Replayed `SaveNote` or `SaveEntry` with identical text/purpose/photos returns `200 Ok` with fresh personal data rather than a 409 conflict.

### 3.6 Media Packs & Image Handling (`Packs.fs`, `OfflinePage.fs`, `Images.fs`)
- **Pack tiering:**
  - `Base`: maps and glossary illustrations (~3 MB, auto-downloaded).
  - `Thumbnails`: 640px photos (~105 MB, default on).
  - `FullSize`: 1600px photos (~450 MB, opt-in).
- **Dynamic planning:** `Packs.plan()` derives media lists directly from accounts stored in IndexedDB.
- **Network constraints:** Evaluates connection type (`wifi`, `cellular`, `offline`). Auto-downloads obey the `WifiOnly` preference.
- **Memory safety:** `Images.View` and `Images.measure` revoke object URLs (`URL.revokeObjectURL`) on disposal or measurement completion.
- **Hardware Camera:** Uses standard `<input type="file" accept="image/*" capture="environment">` for zero-permission system camera capture.

---

## 4. Key Strengths

1. **Architectural Purity:** Decoupling protocol in Hedge from policy in Native Plants ensures that other Hedge applications (e.g., Grassophy) can adopt offline support by simply configuring endpoints and folding rules.
2. **Transparent vs Explicit Separation:** Transparent caching for reads eliminates UI boilerplate, while explicit outbox queuing prevents silent data loss and ambiguity for writes.
3. **No Service Worker Dependency:** By embedding the cache within the HTTP transport decorator, the system operates reliably across standard browsers and native Capacitor WebViews (where CapacitorHttp bypasses Service Workers).
4. **Deterministic Client Search:** Descriptive search is fast (< 10 ms for 531 accounts), deterministic, and fully functional without network.
5. **Zero Storage Leaking on Cancelled Uploads:** When an unsent upload is deleted, `Sync.fs` immediately deletes the queued attachment blobs from IndexedDB.

---

## 5. Findings & Recommendations

### Finding 1 (Minor UX / Visual Glitch): Progress Bar Counter Reset on Pack Resume
- **Location:** `apps/native-plants/src/Client/OfflinePage.fs` (`start`) and `packages/hedge/src/Client/Offline.fs` (`fetchMediaWith`)
- **Detail:** When starting a pack download where items are already cached:
  1. `OfflinePage.fs` initializes UI progress to `keptOf model.Kept (urls plan pack)` (e.g. 450/500).
  2. `fetchMediaWith` initializes its internal progress counter to `{ Total = urls.Length; Kept = 0; Failed = 0 }`.
  3. As `fetchMediaWith` checks existing items in IndexedDB, `onProgress` emits callbacks with `Kept = 1, 2, 3...`.
  4. The UI progress bar flashes down from 450 to 1, then rapidly counts back up to 450 before fetching the remaining 50 items over the network.
- **Recommendation:**
  In `fetchMediaWith`, initialize `Kept` with the count of already present URLs, or filter `urls` in `Packs.download` to only the missing URLs, or pass `initialKept` to `fetchMediaWith`.

### Finding 2 (Defensive Robustness): Scope Binding in `keepDelivered`
- **Location:** `apps/native-plants/src/Client/Sync.fs`, line 147
- **Code:**
  ```fsharp
  let! blob = store.Get key
  match blob with
  | Some blob -> do! keepPrivateMedia store Client.Storage.owner url blob
  | None -> ()
  ```
- **Detail:** `keepDelivered` uses the mutable global `Client.Storage.owner` instead of `item.Scope`. While `Client.Storage.owner` typically matches during a normal session, if an upload completes just as a session transition occurs, writing to `Client.Storage.owner` could store the blob under `""` or an incorrect scope.
- **Recommendation:**
  Change line 147 to use `item.Scope`:
  ```fsharp
  do! keepPrivateMedia store item.Scope url blob
  ```

### Finding 3 (Concurrency Edge Case): Outbox Enqueue During Active Flush
- **Location:** `packages/hedge/src/Client/Offline.fs` (`createOutbox`)
- **Detail:** If an item is enqueued while `flush()` is actively awaiting network responses, the caller to `flush()` receives the in-flight `pass()` promise. Because `pass()` takes a snapshot of `pending` at the start of its execution, the newly enqueued item will not be processed during this pass.
- **Impact:** Low. The item remains safely in IndexedDB and will be flushed on the next reconnect, resume, or user action.
- **Recommendation (Optional Enhancement):**
  Add a `rerunRequested` flag in `createOutbox`: if `flush()` is invoked while `running.Value.IsSome`, flag `rerunRequested <- true`, and in the `finally` block of `pass()`, trigger another pass if flagged.

### Finding 4 (IndexedDB Abort Handling): Transaction Error Handlers
- **Location:** `packages/hedge/src/Client/Offline.fs`, line 44
- **Code:**
  ```fsharp
  [<Emit("new Promise((resolve, reject) => { const transaction = $0.transaction('kv', $1); const request = $2(transaction.objectStore('kv')); request.onsuccess = () => resolve(request.result); request.onerror = () => reject(request.error); })")>]
  let private databaseRequest (database: obj) (mode: string) (operation: obj -> obj) : JS.Promise<obj> = jsNative
  ```
- **Detail:** In IndexedDB, if a transaction aborts due to quota limits (`QuotaExceededError`) or OS storage reclaim, `transaction.onabort` or `transaction.onerror` may fire without `request.onerror` firing. In such an event, the promise would neither resolve nor reject.
- **Recommendation:**
  Add transaction-level handlers to ensure the promise rejects cleanly:
  ```javascript
  transaction.onerror = () => reject(transaction.error);
  transaction.onabort = () => reject(transaction.error || new Error('Transaction aborted'));
  ```

---

## 6. Verification Records

### Automated Test Suite
All 144 unit and integration tests executed cleanly:
- `test/offline.test.mjs`: Read cache revalidation, ETag 304 handling, outbox queueing/ordering/parking, media prefetch.
- `test/sync.test.mjs`: Outbox command typing, folding, notebook overlay, reconcile, retry.
- `test/packs.test.mjs`: Pack planning, size estimation, connection constraints.
- `test/seed.test.mjs`: Seed verification and cache priming.
- `test/descriptions.test.mjs`: Descriptive search matching, tokens, fragments, section weighting.
- `test/accounts.test.mjs`: Bulk warm-up priming.
- `test/contributions.test.mjs`: Server idempotent replayed create handling.
- `test/preview-gate.test.mjs`: OAuth handshake exemption on GET routes.

### Build Verification
1. `npm run gen`: Generates clean F# request and image codecs.
2. `npm run build`: Compiles `Client.fsproj`, `Admin.fsproj`, `Server.fsproj`, and bundles Vite site (`_site/`).
3. `npm run build:mobile`: Compiles native bundle and generates 531 accounts into `mobile/www/seed/` (2,098 KB).

---

## 7. Next Actions

1. Apply the two defensive improvements (Finding 1 for progress bar consistency, Finding 2 for `item.Scope` binding).
2. Deploy the preview worker (`npm run deploy:preview`) to make `/api/plants/accounts` and idempotent note creates live.
3. Perform physical Android device checks with the built APK (Google sign-in roundtrip, camera capture, offline field notes, pack download).
4. Proceed to Grassophy rebase and adoption as outlined in `HEDGE-offline.md`.

## Resolution (2 November 2026)

- Finding 1: `Client.Packs.download` receives only the missing files; the screen shows the kept count plus progress over the whole pack, so the bar no longer resets.
- Finding 2: `keepDelivered` keeps bytes under `item.Scope`.
- Finding 3: a flush requested during a pass runs one more pass when it ends.
- Finding 4: the store's transaction wrapper resolves on commit and rejects on error or abort.

See `HEDGE-offline-review-2026-11-01.md` for the full resolution table, which these overlap.

