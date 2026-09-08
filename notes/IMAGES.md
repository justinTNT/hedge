# Microblog images → R2 (plan)

Status: **planned, not started** (decisions locked 2026-09-08). No code yet.

## Decisions (locked)

- **Microblog images move to R2.** All image bytes a microblog *owns or shows* live in
  that tenant's `<tenant>-blobs` R2 bucket, served at `/blobs/<key>` by the worker.
- **Full backfill.** Mirror every *currently-resolving* image referenced by existing
  items (hero + inline) into R2 and rewrite the stored URL to `/blobs/<key>`. Dead
  hotlinks (already 404) are left as-is — `hideBrokenImg` already hides them.
- **Auto-mirror new content.** On submit/update, any image URL not already under
  `/blobs/` is fetched and ingested to R2 — uploads *and* pasted external URLs. No
  permanent third-party hotlinks in microblog items.
- **AWS is NOT being retired.** The `isnt.so` S3 bucket stays; mp3s and experiments
  keep living on S3. This plan only removes the microblogs' *dependency* on AWS so the
  bucket *could* be deleted later — that deletion is a separate, deferred decision.

Sizing: the entire `isnt.so` bucket is **396 objects / 77.8 MiB** across all sites, so
storage cost on R2 is negligible and the backfill is a minutes-long job. R2 also has no
egress fees, so serving from `/blobs/` is cheaper than the current S3 hotlink.

## Current state (what already exists)

hedge already has the R2 primitives — the gap is that item images don't use them.

- **Framework `Hedge.Workers`:**
  - `handleBlobUpload` — `POST /api/blobs`, multipart `file`, `allowedImageTypes`
    (jpeg/png/gif/webp/svg), key = `<newId()>/<name>`, returns `{"url":"/blobs/<key>"}`.
  - `handleBlobServe` — `GET /blobs/<key>`, reads `contentType` from R2 `httpMetadata`,
    serves with `Cache-Control: public, max-age=31536000, immutable`.
  - Router wires both; `blobs` is a reserved slug; every tenant has a `BLOBS` binding.
- **App `apps/microblog/src/Server/Handlers.fs`:**
  - `cacheAvatar` — the template for what we want: content-addressed key
    (`avatars/<hash>`), get-then-put dedup, `r2PutTyped` stores `contentType`,
    best-effort (returns the original URL on any failure). Currently avatar-only
    (https-only, avatar image types, `avatars/` prefix).
- **Client:** item `Image` and inline TipTap `<img>` render whatever URL is stored.
  No upload path is wired to `/api/blobs` for post content.

### Bugs / gaps to fix along the way

- `handleBlobUpload` calls `blobs.put(key, file)` **without** `httpMetadata.contentType`,
  so `handleBlobServe` serves those objects as `application/octet-stream`. Must store the
  content type on put (as `cacheAvatar`/`r2PutTyped` already do).
- ETL only rewrites the **hero** `image`; inline `<img>` in `teaser`/`comment` HTML keep
  `http://isnt.so/...` → mixed-content-blocked on HTTPS. The backfill's inline walker
  fixes these; note it so we don't assume inline images currently work.

## Target architecture

- **Storage:** per-tenant R2 (`<tenant>-blobs`), keyed content-addressed:
  `img/<sha1(sourceUrl)>.<ext>` (or the existing hmac hash, 32 chars). Content-addressing
  gives idempotent backfill, cross-item dedup, and keeps the immutable cache header honest.
- **Serving:** unchanged — `/blobs/<key>` via `handleBlobServe`, edge-cached, free egress,
  same origin (no CORS, no mixed content).
- **Ingestion:** one framework function turns any fetchable image URL into a `/blobs/` URL.

## Framework / app boundary

- **Framework (`Hedge`):** the *mechanism* —
  - generalise `cacheAvatar` → `ingestImage blobs url` (see A);
  - fix `handleBlobUpload` content type;
  - the admin image-picker widget (rides the queued Admin extraction).
- **App / tenant (`apps/microblog`):** the *policy* —
  - which bucket (`BLOBS` binding, already per-tenant);
  - "auto-mirror on submit" wiring in the write handlers (C);
  - the backfill invocation (B);
  - the TipTap-JSON image walker (arguably framework once a second app needs it, but start
    it in the app next to the ETL/rich-text code).

## Workstream A — `ingestImage` (framework)

Generalise `cacheAvatar` into a reusable, best-effort ingester:

- signature: `ingestImage (blobs: R2Bucket) (url: string) : Promise<string>` → returns a
  `/blobs/<key>` URL, or the **original url unchanged** on any failure (never throws).
- accept `http://` **and** `https://` (server-side fetch has no mixed-content rule; needed
  for `isnt.so` which is http-only) — but if the input is already `/blobs/...` or a
  same-origin `/blobs/` URL, return it untouched (no re-ingest).
- content-address the key: `img/<hash(url)>.<ext>`, dedup via `blobs.get key` first.
- validate `content-type` against the allowed image set; store it via `r2PutTyped`.
- **size cap** (e.g. reject > ~10 MB) so a bad URL can't blow up a worker request.
- keep `cacheAvatar` as a thin wrapper (or fold avatars into `ingestImage` with an
  `avatars/` prefix option) so sign-in behaviour is unchanged.

## Workstream B — full backfill (one-off, per tenant)

Goal: rewrite existing items so every resolving image is a `/blobs/` URL, severing the
AWS dependency for `usbase`, `mtmuse`, `nonukes`, `idealist`.

Preferred mechanism: a **guarded maintenance route** (worker-side, has `BLOBS` + `fetch`):

- `POST /api/admin/backfill-images`, gated by the `ADMIN_KEY` secret.
- for each item: `ingestImage` the hero `Image`; walk the TipTap JSON of `extract` and
  `owner_comment` and `ingestImage` each image node's `src`; `UPDATE` the row only if
  something changed.
- idempotent by construction (content-addressed keys + `/blobs/` inputs skipped), so it's
  safe to re-run; returns a report `{items, heroMirrored, inlineMirrored, skippedDead}`.
- run once per tenant with a curl carrying the admin key, then leave the route guarded (or
  remove it) — it's harmless when idempotent but should not be world-callable.

Alternative if a route feels wrong: a local node script using `wrangler r2 object put` +
`wrangler d1 execute`. The worker route is cleaner (fetch+put+update+contentType all in
one place, reuses `ingestImage`) — prefer it.

Order: run backfill **after** a tenant's normal ETL load, so there's never a broken
window (ETL's S3-rewrite keeps images working until the backfill moves them to R2).

## Workstream C — auto-mirror on submit (app)

In the microblog create/update handlers (`submitItem` / update path in
`apps/microblog/src/Server/Handlers.fs`):

- before writing, `ingestImage` the hero `Image` and every inline image `src` in the
  rich-text fields (same walker as B).
- best-effort: if a fetch fails, keep the original URL and let the post through — never
  block submission on a flaky image host.
- net effect: pasting a foreign image URL self-hosts it on save; uploads (via the widget)
  already produce `/blobs/` URLs and pass through untouched.

## Workstream D — admin upload widget (framework, batched)

- an image field in the admin editor that POSTs to `/api/blobs` and stores the returned
  `/blobs/<key>`; drop/paste in TipTap uploads the same way.
- this restores justsayno.de's upload UX on R2.
- **batch with the queued framework work** (Admin extraction + per-tenant config in
  `MONOREPO.md`) rather than one-off it in the app.

## ETL adjustment

- Keep the current `isnt.so → https S3 REST` + `http → https` rewrite as the immediate
  working state for freshly-migrated tenants.
- Document that the per-site migration recipe gains a final step: **run the image
  backfill** after loading, to move images from S3 to R2.

## Sequencing

1. **A** — `ingestImage` + fix `handleBlobUpload` content type (small, reuses `cacheAvatar`).
2. **B** — backfill route; run for `usbase`, `mtmuse`, `nonukes`, `idealist`; verify.
   *(This is the step that unblocks any future `isnt.so`/AWS teardown.)*
3. **C** — auto-mirror on submit (hero + inline walker).
4. **D** — admin upload widget, with the framework batch.

## Risks / notes

- TipTap image node shape: `{ type: "image", attrs: { src } }` — the walker must handle
  nested content arrays; reuse for both B and C.
- Same image referenced by many items → content-addressed key dedups automatically.
- Backfill fetches the **stored** URL (post-ETL that's the https S3 REST endpoint), so no
  http/mixed-content issue server-side.
- Non-image / dead URLs: `ingestImage` returns them unchanged; they stay as-is and
  `hideBrokenImg` hides the broken ones.
- Do **not** delete `isnt.so` as part of this — decoupling ≠ teardown (per decision above).
