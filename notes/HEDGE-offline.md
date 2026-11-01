# Hedge: offline reads, writes and media at the framework level

Decided 1 November 2026. Gen groundwork, the offline runtime, the Native Plants read and write wiring and the media packs with their offline screen are done.

Native Plants and Grassophy are field tools with the same requirement: the whole
catalogue usable with no signal, photographs where the device can afford them, and
notes and photos taken in the field that reach the server when connectivity returns.
Two apps with one requirement is the threshold for a framework answer, so the design
lives in Hedge and the first implementation is proved in Native Plants, then adopted
by Grassophy when its branch takes the newer framework.

## Facts that fix the layer

- Every generated client runs over `Hedge.Http.Transport`, a function from request to
  promised response, and the framework already decorates it (browser or Capacitor
  adapter, then the guest-session epoch wrapper). Offline is two more decorators and a
  store; generated clients and the server do not change.
- The Android app routes `fetch` natively through CapacitorHttp, which bypasses any
  service worker, and the private preview gate marks every response `no-store`. A
  service worker or HTTP-cache design would work on the public website only. An
  explicit store under the transport works on both.
- `Hedge.Http.Request` is JSON-serialisable by construction: its body is already
  serialised text. An outbox can persist it as-is and replay it through the same
  decorated transport, so identity and the preview credential ride along.
- Reads and writes are already structural: `Get`, `GetBy`, `GetQuery` versus `Post`.
  No attribute is needed to tell a cacheable read from a queued write.
- Blobs are served `immutable` and catalogue media names are content-hashed, so a
  manifest diff downloads only changed files.

## Principle: reads are transparent, writes are explicit

A read decorator serves GET requests from the store on network failure or by policy
and refreshes the store on success; callers cannot tell the difference. A queued
write is neither failed nor completed, the typed client cannot return "done later",
and the UI must know at submit time that an item is pending. So writes go through an
outbox the app calls deliberately. The outbox replays through the transport chain.

| Framework owns (protocol) | App owns (policy) |
| --- | --- |
| Store abstraction: IndexedDB for small state, Capacitor Filesystem for media; persistence request; quota estimate | Which endpoints to snapshot and which media tiers to download |
| Read decorator: network-first, cache-first, cache-only; keyed by request and identity | Default download size, WiFi-only default, the offline settings screen |
| Outbox: durable queue, identity binding, replay on online/resume/success, backoff, retry on network and 5xx | Meaning of each 4xx: quota parks, revision conflict becomes a draft, 401 waits for login |
| Media prefetch: manifest diff by hash, per-file resumable download, progress | Ordering such as photo before dependent note; local quota counting |
| Identity binding from the session layer | Local descriptive search over the snapshot |

## Gen groundwork (done 1 November 2026)

- `ClientGen` emits a `Requests` record alongside `Client`: the wire request each
  endpoint sends, before any transport. `createClient` is now defined in terms of it,
  so a cache key or an outbox item is built from the same path, query and body the
  live client uses. Every app and module surface was regenerated.
- `Codecs` emits an `Images` module when any response or view type carries an
  `Hedge.Interface.Image`: one collector per image-bearing record, walking nested
  records, options, lists and arrays, named like the codecs. A field opts into media
  discovery by its type alone. Surfaces without `Image` fields are unchanged apart
  from the new `img` unwrap helper beside `lk`.
- Both client transports now forward response headers, so a caching decorator can use
  ETag and Cache-Control generically. `Http.sendDecode` ignores them.
- Native Plants adopted the `Image` wrapper for catalogue photos, thumbnails,
  distribution maps, glossary illustrations and personal photos. Wire JSON is
  unchanged because the codec serialises wrappers as bare strings. The legacy v1
  personal JSON, which is a raw `JSON.stringify`, unwraps `Image` through a replacer
  that tests the holder's original value, since `stringify` calls Fable's union
  `toJSON` before the replacer runs.

## Offline runtime (done 1 November 2026)

`packages/hedge/src/Client/Offline.fs` (module `Client.Offline`), compiled into any app's client
like the other framework client files, and into `Hedge.Client.fsproj`:

- `Store`: durable key-value of strings and Blobs with prefix listing. `indexedDbStore` for the
  browser and WebView, `memoryStore` for tests and blocked storage, `openStore` picks; `persist`
  asks the browser not to evict. The Capacitor Filesystem backend for very large media packs
  is a later swap behind this same record.
- `cachedTransport` (network first, saved copy on no response or 5xx, ETag revalidation, 4xx never
  masked) and `cacheFirstTransport`, both keyed by a caller-supplied scope so private reads are
  bound to an identity; `onServed` tells the app whether a read came FromNetwork or FromCache.
  `warm` fills the store through a transport with bounded concurrency.
- `createOutbox`: durable, scope-bound queue of `Hedge.Http.Request`s with optional attachment
  keys; ordered replay that stops behind a Retry, parks on the app's say-so, joins concurrent
  flushes and removes attachments with their items. `defaultDecision` reads outcomes by HTTP alone.
- Media: `mediaBlob`, `keepMedia`, `forgetMedia`, `keptMedia`, `fetchMedia` (skip present, count
  failures, resumable by construction), `objectUrl`; `onReconnect` for online/visibility.
- Tests: `apps/native-plants/test/offline.test.mjs` drives the compiled module with a memory store.

Native Plants wiring, reads:

- Public catalogue reads go through `cachedTransport` with scope "" (`Client.Storage.store`,
  IndexedDB `native-plants`). Private reads keep their uncached session-locked transport.
- After a catalogue load whose revision has not been warmed, every plant account is read through
  the cache four at a time and the revision recorded, so every page opens offline.
- On the bundled app, a photograph is resolved kept-copy first; catalogue media that was fetched
  is kept, so what has been seen stays available. Private personal media is not kept yet.
- A revision poll that gets no response keeps the saved copy on screen; an "Offline: showing the
  guide as saved on …" notice appears whenever a read was answered from the cache.

Native Plants wiring, writes (`src/Client/Sync.fs`, the app's policy over the framework outbox):

- A notebook command or upload is tried on the network first; when the request never completed and
  the person is signed in, it is queued under their identity (`provider:id`) with the viewer token it
  would have carried. The session lock's own refusal is told apart from the network being away.
- A later change folds into an earlier queued one: edits replace an unsent create, deleting an
  unsent create or upload cancels both (and forgets the kept bytes), the latest hero choice wins.
- The notebook shows the last snapshot with queued changes overlaid, with local limits moved
  accordingly; pending items carry a marker, parked ones the server's reason plus "Send my version"
  (resent over the server's current revision) and "Discard". Pending photographs are shown from the
  kept bytes under `outbox/<id>/…` paths.
- Replay: 2xx delivered; no response or 5xx retried; any 4xx parked with the server's text. After a
  fresh snapshot, parked changes the server evidently has (a create that landed, an upload, a delete)
  are finished rather than left as conflicts. The server answers a replayed identical create as the
  success it was instead of a conflict.
- Flush on reconnect/visibility, on a completed sign-in, after any delivered write, and on demand.
- Private reads are cached under the identity scope so the notebook opens offline for its owner.
- Camera: the bundled app offers "Take a photograph" through the file input's `capture` hint; no
  permission, no plugin; the system camera does not save to the gallery.

Native Plants media packs and the offline screen (`src/Client/Packs.fs`, `src/Client/OfflinePage.fs`,
route `/offline`, bundled app only):

- A pack is derived from the accounts the read cache holds (`Client.Offline.cachedResponses` over the
  saved `GetPlant` responses plus the saved catalogue), so it is exactly the guide as saved and needs
  no server manifest. Three packs: Base (distribution maps and glossary drawings, kept automatically
  whenever online, about 3 MB), Thumbnails (every photograph at 640 px, on by default, about 105 MB),
  FullSize (every photograph at 1600 px, opt in, about 450 MB). Sizes are estimates from measured
  averages; the screen shows the browser's storage estimate as the true total.
- Downloads start by themselves after the account warm-up, on reconnect and when the screen opens,
  under the settings and a connection rule: Wi-Fi always, mobile data or an unknown network only when
  "Wi-Fi only" is off (the Network Information API gives the type on Android). A pack the person
  stopped, or that kept failing, is not restarted by itself in the session; "Download now" overrides.
  `fetchMedia` is resumable by construction and takes a `proceed` check for stopping.
- The screen shows accounts saved of total and the last refresh, storage used, the connection, pending
  changes, each pack's kept count with progress, Stop, Download now, Remove, and "Remove all
  photographs" (everything kept except the base pack and queued uploads). Settings persist in the store.
- The offline notice links to the screen; the footer carries "On this phone" on the bundled app.

A person's own photographs, kept under their identity:

- The framework gained identity-bound media (`privateMediaBlob`, `keepPrivateMedia`,
  `keptPrivateMedia`, `forgetPrivateMedia`, `fetchPrivateMedia`), stored under `private/<scope>/…`
  and invisible to the device-wide media helpers, and the outbox now announces a delivery before
  removing the item's attachments, so an app can keep what it needs.
- Native Plants: a delivered upload's prepared bytes move to the owner's private media under the
  paths the server now shows it at, so a photograph taken in the field stays on the phone after it
  is sent. A personal photograph fetched for display is kept the same way, and each notebook load
  prefetches that plant's own photographs in both sizes. Lookup uses the signed-in identity
  (`Client.Storage.owner`), so another account on the device finds nothing. The offline screen shows
  how many are kept and can remove them; "Remove all photographs" leaves them alone.

Descriptive search runs on the device (1 November 2026): `src/Core/Descriptions.fs` is the pure
matcher the keyword-search note asked for, and `src/Client/Search.fs` feeds it the saved accounts
from the read cache. The browse page shows "N matches in descriptions" under the name results,
collapsed when names matched and open when none did, each with the section label and a verbatim
excerpt with the query words marked, linking to the section anchor. Coverage is stated while the
warm-up is incomplete. The keyword-search note records the revision.

Bulk warm-up (1 November 2026): `GetAccounts` (`/api/plants/accounts`) returns every published
account with the catalogue revision, straight from the `CatalogueStore` snapshot. The warm-up tries
it first through the plain transport and primes the per-account read cache from it with the
framework's `primeRead`, so pages, search and packs keep reading the same per-account entries; when
the server does not offer it yet, or the request fails, the per-account warm-up runs as before. The
endpoint reaches the preview only with a server deploy (`npm run deploy:preview`); the APK works
either way.

First launch with no network (1 November 2026): `scripts/seed-mobile.mjs` runs the compiled worker
over the local import at build time and writes the catalogue and bulk accounts responses into the
bundle (`mobile/www/seed/`, about 2 MB); `Client.Seed.ensure` primes the read cache from them once,
on a device with no saved catalogue, before the first load, and records the seed's revision as the
warm-up's. A stale seed is harmless: the first connection's revision poll replaces it. Two gates in
the start-up path also had to tolerate the network being away: the preview unlock now treats a
failed check of the saved password as "open with the saved password" (only the server's refusal
asks again), and the sign-in model keeps the identity last confirmed for the stored bearer when the
session read cannot reach the server on the bundled app, so the saved notebook and queued writes
stay with their owner; the server judges the bearer when it is next reachable.

Still to do: the Capacitor Filesystem backend only if IndexedDB proves too small for the full-size
pack on real devices; Grassophy (rebase its branch first, adopt `Image`, then the same pieces).

Rejected for now: erasing the wrapper unions in Fable (would change the runtime shape
the codec and admin rely on), and adding a `Deferred` case to `ApiError` so a
transparent decorator could report queuing (touches every consumer's error handling).

Reviewed 1 November 2026 (`HEDGE-offline-review-2026-11-01.md`, `HEDGE-offline-code-review.md`);
all findings resolved 2 November 2026, with the resolution tables in those notes. The ones that
changed the design: the session lock sits inside the private read cache; an outbox pass is bound to
the identity it started under; every acknowledged notebook answer, online or replayed, becomes the
owner's saved snapshot; a change to something already queued joins the queue instead of overtaking
it; the fallback warm-up confirms from the network only; downloads keep to the Wi-Fi rule as they go.

## Next steps

Done on 1 November 2026: the `Hedge.Offline` runtime; Native Plants reads, writes (outbox), camera
capture, media packs with the offline screen, own photographs per identity, local descriptive
search and the bulk warm-up, all recorded above. Thumbnails download by default and full-size
photographs are the explicit option, as decided.

Remaining:

1. Grassophy: rebase `grassophy-species` onto main, give its species photos and recordings the
   `Image` type, then adopt the same pieces (cached reads, warm-up, packs, outbox as needed).
2. The Capacitor Filesystem backend behind the same `Store` record, only if IndexedDB proves too
   small or too slow for the full-size pack on real devices. A device test decides this.
3. Operational: deploy the preview worker so the bulk endpoint and the idempotent replayed create
   are live (the APK works against the current server through its fallbacks); install the APK and
   run the field checks; commit and force-push the rebased branch.
