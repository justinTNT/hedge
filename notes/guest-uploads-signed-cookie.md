# Card: signed guest cookie + guest comment-image uploads

**Status:** carded for later (2026-09-15). Not started.

## Problem
Uploading an image into a **comment** fails with `401` (`[hamlet-rt] Upload failed: 401`).
The rich-text 🖼 button uploads to `POST /api/blobs`, which is **admin-gated**
(`X-Admin-Key` from `localStorage.adminKey`, Router.fs). A guest commenter has no admin
key → hard 401. (The owner, signed into `/admin` on the same origin, has the key, so it
works for them — which is why it went unnoticed.) Pre-existing; surfaced testing justat.

## Intent (from the user)
The admin gate is about **abuse-resistance, not privilege**. Image posting in comments
should be allowed, just **kept within an app workflow** — not a wide-open keyless endpoint.
Guests should effectively be "granted a key," via a **different path than admin**. And: the
guest cookie, if not server-signed, **should be** (an unsigned identity cookie is forgeable).

## Plan

### Part 1 — Sign the `hedge_guest` cookie (framework)
Today it's a bare `guestId` uuid (`Router.fs:65-75`, `resolveGuest`/`guestCookieValue`),
`HttpOnly; SameSite=Lax`, server-issued on first write but **not signed**. Make it
`guestId.HMAC(secret, guestId)`:
- `resolveGuest` / `guestCookieValue` become **async** (HMAC via `Workers.hmacSha256`) and
  take a secret; verify on read, mint+sign when missing/invalid.
- Thread the secret through **~13 call sites** (all currently `resolveGuest request` /
  `guestCookieValue guest`):
  - `packages/hedge/src/Hedge/Router.fs` — identity endpoints (~247-262) + OAuth start/callback (~297-343)
  - `packages/modules/blog/src/Server/Handlers.fs:155,203` (submitComment)
  - `packages/modules/articles/src/Server/Handlers.fs:127,173` (submitComment)
  - `apps/microblog/src/Server/Handlers.fs:151,171,184,227,238,250` (activate/revert/disconnect)
  - `apps/articles/src/Server/Handlers.fs:120,140,147,188,199,211`

### Part 2 — Guest upload endpoint (framework + editor)
- New `POST /api/blobs/guest` (Router.fs, alongside the admin `/api/blobs` at ~364): **no
  admin key**; requires a valid **signed** guest cookie; image-type gated
  (`allowedImageTypes`); **size-capped (~5 MB)** — needs a `fileSize` Emit helper in
  Workers.fs; stores under `comment/<guestId>/<id>/<name>`; returns `/blobs/…`. New handler
  `handleGuestBlobUpload` in `Workers.fs` (mirror `handleBlobUpload:212`).
- Editor: comment editors set `container._hamletUploadEndpoint = "/api/blobs/guest"` (the
  hook already exists — `packages/rich-text/tiptap-editor.js:90,703`, `uploadEndpoint`
  param). Admin/authoring editors keep `/api/blobs`. The uploader's `X-Admin-Key` header
  (`:149`) is harmless on the guest path (ignored). Wire the endpoint through the shared
  `Client.RichText` comment-editor creation → blog/articles comment pages.

## Decisions pending
1. **Signing secret** — reuse per-deploy `OAUTH_SECRET` (in every `Env`; no new config, but
   may be empty on no-OAuth tenants e.g. ntaidc) vs a dedicated `GUEST_SECRET` env var
   across the estate (cleaner, config churn). **Rec: OAUTH_SECRET.**
2. **Existing (unsigned) cookies** — grace-accept legacy bare-uuid cookies and re-sign on
   next write (no one loses their anonymous identity; forgeable until they age out) vs hard
   cutover (every current guest becomes new; clean but resets anonymous identities/avatars).
   **Rec: grace-accept** (guest cookie is a low-stakes anonymous identity; size/type caps
   bound abuse during grace).

## Rollout
Framework change → redeploy the comment-bearing estate (the tenants already on
`followups-identity-og-editor`). **No DB migrations.**

## Interim / stop-gap (optional, tiny)
Until this lands, guests still see a 🖼 button that 401s. A ~3-line change in
`tiptap-editor.js` could hide/omit the image button when `localStorage.adminKey` is empty,
so guests don't hit a failing button. Not done — decide alongside the full feature.
