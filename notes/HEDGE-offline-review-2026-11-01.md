# Hedge offline implementation code review

Reviewed 1 November 2026 against `notes/HEDGE-offline.md`, covering the staged and unstaged changes relative to HEAD and the new untracked implementation files. This includes the shared offline runtime and generator, Native Plants reads, outbox, media, seed, search, server changes and mobile integration.

The implementation needs corrections before field use. The most serious issues are the cold-start offline notebook gate, identity changes during outbox work, and acknowledged writes disappearing from the saved notebook. P1 means high priority; P2 means a functional defect to fix before calling the implementation complete. No implementation fixes were made during this review.

## Findings

### 1 P1 Allow an owner to reach the private cache after an offline cold start

Location: `apps/native-plants/src/Client/ContributionsApi.fs:12–17`; `packages/hedge/lib/guest-session.js:451–460`.

The session decorator wraps `Client.Sync.privateReads`, so `withSessionRequest` must obtain a ready session before the cached transport runs. On a cold start without signal, `/api/mobile/me` fails and readiness is false. Although the new `Auth.lastKnown` restores the account for display, every notebook read is rejected with “Your session is unavailable” before consulting its saved snapshot. `Personal.Loaded` then clears Data, disabling notebook edits and uploads as well as offline reading.

Reproduced against the compiled ContributionsApi with a primed identity-scoped personal cache and the runtime's unavailable-session rejection: `personal` returns that transport error despite the saved entry. This is distinct from losing connectivity after a successful session check in the same process.

Provide an explicit offline read path for the locally confirmed owner while preserving epoch checks around delivery. Test a complete app restart with a saved bearer, identity and notebook, followed by offline reading and queueing.

### 2 P1 Bind delivery and every replay step to the item's original identity

Location: `apps/native-plants/src/Client/Sync.fs:141–147`; `packages/hedge/src/Client/Offline.fs:303–320`.

`keepDelivered` stores the upload bytes under the mutable `Client.Storage.owner`, rather than `item.Scope`. Its first `store.Get` yields, so an account switch after network delivery can make Alice's private photograph become a saved photograph in Bob's namespace. The callback is already outside the sender's completed session lock. This violates the stated isolation guarantee.

Reproduced with the compiled module: queue an upload as Alice, change owner to Bob, call `keepDelivered` for Alice's item; `keptPrivateMedia(store, "bob")` contains both Alice photo paths.

The outbox also snapshots Alice's pending list once and continues sending it after scope changes. A deferred-sender reproduction sent the second Alice item with Bob as the current credential owner. The server's viewer check can reject such writes, but they should never be replayed under the replacement identity or parked as a consequence of switching accounts.

Use `item.Scope` for retained media, pin the pass to its starting identity and stop before another send when that identity changes. Cover switching during send completion and between two queued items.

### 3 P1 Persist successful mutation snapshots before removing their pending overlay

Location: `apps/native-plants/src/Client/ContributionsApi.fs:53–56`; `apps/native-plants/src/Client/Personal.fs:98–104`; `apps/native-plants/src/Client/Sync.fs:159`.

Only GET responses update the private read cache. A successful online notebook command returns an updated Personal snapshot, but `changeOrQueue` and `Saved` only put it in the Elmish model. With no pending items, no refresh follows. Open a notebook, save a note online, then restart offline: the saved GET still contains the notebook from before that save. The acknowledged note disappears from the local view. Deletes can similarly reappear, and saved revisions remain stale.

Replay has the same durability window: the successful POST body is ignored by the delivery callback, then the outbox item is deleted. If the subsequent GET fails, its stale cached snapshot becomes authoritative and the delivered overlay is gone.

Prime the owner's personal GET from every successful mutation response before declaring the local state settled. Test online save followed immediately by offline restart, and replay followed by an unsuccessful snapshot refresh.

### 4 P2 Translate queued photo edits to the revision created by their upload

Location: `apps/native-plants/src/Client/Sync.fs:91–95, 311–316`; `apps/native-plants/src/Server/Contributions.fs:237, 319`.

The optimistic upload photo has Revision 0. Editing its caption, photographer or offered flag while offline queues a separate `PhotoDetails` command with revision 0; there is no fold or replay rewrite for Upload followed by PhotoDetails. The server creates the uploaded photograph at revision 1 and requires an exact revision for updates. Replay therefore uploads successfully and immediately parks the user's metadata as a conflict, even when nobody else touched the photograph.

Retain metadata with the pending upload or derive the dependent command's revision from its delivered result. Test upload, edit details and reconnect as one sequence, including repeated edits before delivery.

### 5 P2 Keep recovery controls visible for parked deletes and hero selections

Location: `apps/native-plants/src/Client/Personal.fs:197–205, 290–298, 357`; `apps/native-plants/src/Client/Sync.fs:306, 309–310`.

Delete overlays remove the note or photograph even when the delete is parked. The only “Send my version” and “Discard” controls are rendered by `pendingMarker` on those now-absent items. Hero selections have an empty item ID and no corresponding marker either. A stale-revision delete or rejected hero selection therefore leaves only an aggregate “1 not sent” count. Send now skips parked items, so the user cannot resolve or discard these changes through the UI.

Render each parked command in a recovery list independent of whether its target survives the optimistic overlay. Test parked note deletion, photo deletion and hero selection with accessible retry and discard actions.

### 6 P2 Do not certify a new catalogue revision using fallback copies from the old one

Location: `apps/native-plants/src/Client/App.fs:109–119`; `packages/hedge/src/Client/Offline.fs:176–195`.

When the bulk endpoint fails, the fallback warm-up runs through the cache-serving transport. `warm` counts any returned 2xx, including an old account supplied from cache after a network failure. If a new catalogue was fetched just before connectivity failed, all the old cached accounts can count as successful and `warm/catalogue` is set to the new revision. Subsequent loads skip warm-up for that revision, so descriptions and media plans can remain old until another catalogue revision or a direct visit refreshes the individual account.

Only record the new warm revision when every required account was confirmed for it. Count network-confirmed results separately from fallback availability, and retry incomplete warm-ups when connectivity returns. Test new catalogue plus failed bulk/per-account requests over an existing old cache.

### 7 P2 Route changes to pending items through the outbox before trying the network

Location: `apps/native-plants/src/Client/ContributionsApi.fs:53–59`; `apps/native-plants/src/Client/Sync.fs:176–191`.

Folding runs only after a network attempt fails. A newer edit can therefore bypass an older pending edit as soon as connectivity returns. For example, queue text B for a server note at revision 2, regain connectivity, then save text C at revision 2 before the queued flush completes. C succeeds; Saved triggers a flush; B conflicts against revision 3 and remains parked. The overlay now displays B over the successfully saved C. An edit of an unsent create can similarly create the newer text online and leave the older create parked.

Check for a pending change to the target before issuing a live command, fold the new intent into that queue and serialize it with replay. Test editing a pending note during reconnection and while a flush is in flight.

### 8 P2 Recheck the connection rule throughout automatic pack downloads

Location: `apps/native-plants/src/Client/OfflinePage.fs:55–62, 92`.

Wi-Fi-only policy is checked when selecting a job. Once started, its `proceed` callback checks only `cancelled`. Changing the setting to Wi-Fi only does not stop an active download either. A download begun on Wi-Fi can therefore continue fetching the remaining thumbnail or full-size pack over cellular data, potentially hundreds of megabytes, despite the saved restriction.

Make automatically started jobs check the current connection and policy before scheduling every subsequent file. Keep any manual override explicit. Test a Wi-Fi-to-cellular transition mid-job and enabling Wi-Fi only during a job.

### 9 P2 Resolve IndexedDB writes only after the transaction commits

Location: `packages/hedge/src/Client/Offline.fs:44–56`.

`databaseRequest` resolves from the individual request's `onsuccess`, with no transaction `oncomplete` or `onabort` handling. IndexedDB request success precedes transaction commit; a later abort can roll back a put after the store has reported success. The outbox can consequently acknowledge “Kept on this phone” without a durable item, particularly under storage pressure. Deletion and attachment cleanup have the same false-success boundary.

Capture request results but resolve only on transaction completion; reject transaction errors and aborts. Add a store-adapter test where the request succeeds and the transaction subsequently aborts. The current memory-store tests cannot exercise this behavior.

### 10 P2 Preserve the old pending write until its replacement is durable

Location: `apps/native-plants/src/Client/Sync.fs:179–189`.

Replacing a queued edit first calls `outbox.Remove` and only later calls Enqueue. A quota failure, storage error or process termination between these operations destroys the previously acknowledged offline edit without persisting the replacement. Even when the UI reports the new save as failed, the earlier saved version has already been lost.

Replace the existing stored item in place, or atomically persist the successor and retire its predecessor. Preserve ordering and attachment ownership. Test replacement with an injected failing Put and confirm that the previous pending edit remains recoverable.

## Verification and coverage

- `npm test` in `apps/native-plants`: 18 Python import checks and 144 JavaScript tests passed. These JavaScript tests exercise the existing compiled `dist` artifacts; a fresh Fable compilation was not performed during this review.
- Native Plants Client, Server and Gen projects each passed `dotnet build --no-restore` with zero warnings and errors.
- Additional small reproductions against the compiled modules confirmed the unavailable-session cache gate, the cross-owner media retention and replay continuing with a changed scope.
- Reviewed generator request builders and image collectors, generated surfaces, bulk endpoint, seed generation, description matching, preview boundary and mobile startup alongside the offline paths. No separate actionable finding was identified in those generation and server additions beyond their interactions described above.
- Existing tests cover memory storage and isolated offline helpers well. They do not establish real IndexedDB commit behavior, a cold offline notebook startup, dependent photo revisions or reconnection races. Browser/Android field checks and the repository-wide golden-model/scaffold pipeline were not run.
- `git diff --check HEAD` reports trailing blank lines in five pre-existing working-tree edits. Those are formatting observations, not counted as review findings.

Fix the identity and offline-startup boundaries first, then make successful snapshots and queue replacement durable. Extend the integration coverage around the concrete sequences above before the field check.

## Resolution (2 November 2026)

All ten findings were addressed; each has a test unless noted. `npm test`: 149 pass.

| # | Resolution | Test |
| --- | --- | --- |
| 1 | The session lock now sits inside the private read cache (`Client.Sync.privateReads` wraps `GuestSession.transport`), and the contributions client uses that transport directly for personal reads. A saved notebook answers before any session check; network reads and all writes still run under the lock. | `test/contributions-api.test.mjs`: cold start, no network, session unavailable → the owner's saved notebook; another identity gets nothing. |
| 2 | `keepDelivered` keeps bytes under `item.Scope`. An outbox pass records the identity it started under and stops before the next send when it changes. | `test/sync.test.mjs` (delivery after an account switch), `test/offline.test.mjs` (pass stops at the switch). |
| 3 | `Client.Sync.rememberSnapshot` stores every successful online command's answer as the owner's saved personal read, under the identity that made the request; `rememberDelivered` does the same for every replay delivery before the item is dropped. | `test/contributions-api.test.mjs` (online save becomes the saved notebook), `test/sync.test.mjs` (delivery answer stored). |
| 4 | `Client.Sync.prepare` sends a revision-0 photograph edit or deletion with the revision the upload created, read from the delivered snapshot. | `test/sync.test.mjs`. |
| 5 | The notebook lists parked changes whose target is not on screen (deletions, hero choices) with "Send my version" and "Discard". | `test/contributions-client.test.mjs`. |
| 6 | `Client.Offline.warm` now takes the store, scope and a plain transport, and stores and counts only what the network answered; the fallback warm-up in `App.warmAccounts` uses it, so a cached copy never certifies a new revision. | `test/offline.test.mjs`. |
| 7 | `changeOrQueue` asks `Client.Sync.supersedes` first: a change to something waiting joins the queue behind it, the queue is flushed, and the notebook re-read; the network is tried directly only otherwise. | `test/contributions-api.test.mjs`. |
| 8 | Automatic pack downloads check the current connection and the Wi-Fi rule before every file (`OfflinePage.mayContinue`); a change of the setting applies at once; a download started by hand continues on any connection; a paused job says so. | No automated test: the connection API is a device feature. Covered by the device check. |
| 9 | `databaseRequest` resolves only on the transaction's `oncomplete` and rejects on request error, transaction error or abort. | No automated test: the suite's store is in memory. A fake-IndexedDB fixture is the next step if this needs guarding. |
| 10 | `Client.Sync.queue` enqueues the successor before removing the changes it replaces; the cancel path removes both and the kept bytes. | Covered by the fold tests; ordering is by construction. |

The second review's findings map onto these: its scope-binding and IndexedDB items are 2 and 9 above; its progress-bar reset is fixed by downloading only the missing files and showing the kept count plus progress; its flush-during-flush item is handled by one further pass after the running one.

