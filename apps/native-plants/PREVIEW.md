# Private Cloudflare preview

Stable URL: https://native-plants-preview.justin-8ee.workers.dev

The preview uses a separate Worker (`native-plants-preview`), D1 database
(`native-plants-preview-db`, `231a3148-92aa-4709-90cd-463fb04c6f2b`) and R2 bucket
(`native-plants-preview-media`). Local development on port 8794 is unchanged.

## Access and privacy

Open the URL and enter username **preview** with the password from
`.local/preview-access.txt`. That ignored, mode-0600 local file also contains the
separate admin key for `/admin` and `/review`. Do not commit or paste those values
into notes. `.local/preview-secrets.json` holds the independent remote secrets.

Cloudflare Access is not enabled on this account. Instead, the deployment-only
`worker-preview.js` wraps Hedge with HTTPS Basic authentication. Every request,
including assets, media, API routes, OPTIONS and OAuth callbacks, passes through
the gate. `assets.run_worker_first=true` is essential: asset-first routing would
expose the static catalogue media. A missing/short gate secret fails closed.
Responses carry `private, no-store`, `noindex` and `no-referrer` headers. Version
preview URLs are disabled. R2 has no public r2.dev URL or custom domain.

This is a shared-password preview, not an invitation system or per-person access
log. Anyone given the preview password can browse the catalogue. Notes and photo
features require a verified Hedge login inside that boundary; the preview password
does not grant those capabilities. Hedge roles also remain independent.
The preview password is removed from requests before they reach Hedge. Hedge
logout clears its session, not the browser's cached Basic credentials. Use a
private browser window and close it to finish a preview session; rotate
`PREVIEW_PASSWORD` to revoke everyone's outer access.

Remote cookies are Secure, HttpOnly and SameSite=Lax (`ENVIRONMENT=preview`).
Notes and photo features now require verified sign-in
(`CONTRIBUTIONS_REQUIRE_LOGIN=true`). Anonymous pages show no notebook, upload or
personal-hero controls, and the server enforces the same requirement. Older
anonymous contributions remain available to claim on login. Notes/photos are
not public merely because their author can enter the preview. Google-hosted fonts
remain part of the existing page; the no-referrer policy suppresses the page URL
on outgoing requests.

## Google and GitHub

The owner has configured Google and confirmed successful live sign-in. GitHub
remains optional and unconfigured. Provider buttons appear only after both client
ID and secret exist. OAuth clients for this preview use:

- Google callback: `https://native-plants-preview.justin-8ee.workers.dev/api/auth/google/callback`
- GitHub callback: `https://native-plants-preview.justin-8ee.workers.dev/api/auth/github/callback`
- Site / authorized origin: `https://native-plants-preview.justin-8ee.workers.dev`

Use the provider's restricted/test audience where available and permit the intended
test accounts. Supply the four secrets with Wrangler's interactive prompts:

```sh
npx wrangler secret put GOOGLE_CLIENT_ID --env preview
npx wrangler secret put GOOGLE_CLIENT_SECRET --env preview
npx wrangler secret put GITHUB_CLIENT_ID --env preview
npx wrangler secret put GITHUB_CLIENT_SECRET --env preview
```

Do not exempt callbacks from the preview gate. Both providers return through the
browser to this same HTTPS origin; its Basic credentials and Lax Hedge cookie
can accompany that navigation. Google live sign-in has been confirmed by the
owner. Legacy anonymous-content adoption and both providers' callbacks also have
simulated-provider integration coverage; GitHub needs a live round trip if enabled.

## Data and deployments

The initial snapshot contains 531 plants, 1,872 photo records, 295 maps, 95 glossary
terms and 145 references. Only the five curated catalogue tables were copied;
identity, grant and personal-content tables started empty. Original media is in
Worker assets; new contributed media uses private R2. Remote admin edits and user
content are independent from local edits.

From `apps/native-plants`, update code/static assets with:

```sh
npm run deploy:preview
```

This builds, checks media references and the privacy boundary, and deploys only
the `preview` environment. It does **not** reseed D1 or overwrite remote records.
Future schema changes need explicit migrations against `native-plants-preview-db`
with `--env preview --remote`; do not rerun old migrations against this fresh full
schema. Never use the default local-placeholder database configuration remotely.

The initial seed is retained in ignored `.local/preview-initial.sql`, with counts
in `.local/preview-catalogue-counts.json`. `scripts/prepare-preview-seed.py` takes
an explicit SQLite path, reads a consistent snapshot, copies only catalogue
tables and refuses to overwrite an existing seed. Tables are created before
data, with parent inserts first for D1. `scripts/check-preview.py` validates this
seed, all referenced media, asset sizes and the preview configuration.

The initial setup used:

```sh
npx wrangler d1 execute native-plants-preview-db --env preview --remote --file .local/preview-initial.sql --yes
npx wrangler deploy --env preview
npx wrangler secret bulk .local/preview-secrets.json --env preview
```

The Worker stays closed until its gate secret is uploaded. Existing remote
secrets survive normal deployments. Keep `ADMIN_KEY`, `GUEST_SECRET`,
`OAUTH_SECRET` and `PREVIEW_PASSWORD` independent. Changing the guest signing
secret without a compatible keyring would invalidate anonymous sessions.

## Verification — 25 September 2026

- Full app build; 18 importer tests and 65 Node tests passed. Preview checks cover
  denied routes/methods, missing/malformed credentials, redirect/cookie preservation
  and the deployment configuration. `git diff --check` passed.
- Cloudflare dry-run confirmed isolated DB/R2 bindings. Initial live requests
  returned 503 before the gate secret existed. After secret installation, requests
  without the password returned 401 for pages, images, scripts, APIs, OPTIONS and
  OAuth callbacks. Authorized catalogue requests returned all 531 plants.
- Cloudflare API confirmed `previews_enabled=false`; bucket checks confirmed
  r2.dev disabled and no custom domains. No public callback exception was added.
- Live tests with two fresh signed guest sessions verified Secure/HttpOnly/Lax
  cookies, private note isolation, same-origin write checks, owner correction
  review, private R2 upload/read isolation, personal hero selection, photo offers,
  owner-only admin and logout independent from the preview password.
- Upload/storage verification used the existing JPEG marker test fixture through
  the API, then deleted it; browser file selection and image decoding remain a
  manual check. No test photograph was promoted to the catalogue. Temporary
  content was removed through app endpoints; soft-deleted test rows may remain.
- At initial deployment, provider discovery returned no providers until credentials
  were installed. The owner subsequently configured Google and confirmed live
  sign-in. Notes and photo features now require verified sign-in; earlier
  anonymous-mode verification above is retained as deployment history.

Sanitized live-check records are in `.local/preview-smoke-report.json` and
`.local/preview-cloudflare-settings.json`. The initial deployed code version was
`682e45f6-44f9-4edd-ad85-33de9ab828ec`; installing secrets creates a subsequent
Worker version automatically.


## Per-species limits deployed — 25 September 2026

Version `62ee0a20-6196-4526-877b-17c48db23a85` limits each owner to five active
notes and five active personal photos for each species. Server-reported counts
drive the notebook controls; atomic insertion guards include in-flight uploads.
Deletion releases a slot. Read/unread corrections and offered/promoted photos
continue to count while their personal items remain; future admin lifecycle
effects are documented in [CONTRIBUTIONS.md](CONTRIBUTIONS.md).

Build, 76 Node tests, 18 importer tests and preview checks passed. Coverage includes
simultaneous additions, failed cleanup, completed-upload retry, over-limit legacy
adoption and UI capacity changes. A live read-only check confirmed the new client
bundle, catalogue availability, private gate, anonymous contribution denials and
the Google login redirect. No schema migration or remote content deletion was needed.


## Owner and curator navigation — 25 September 2026

Version `e229828a-5503-4157-8f59-1f0ebea2a4ae` adds read-only identity lookup to
the owner admin for assigning curator grants. Catalogue links require a validated
owner key; review links and the review interface require owner or curator access.
Direct anonymous navigation to `/review` shows an access-required page. The
protected APIs continue to check authorization on every request. A saved owner
key remains independent of Google sign-in/logout.

Chrome checks covered anonymous and owner navigation on both the app and admin
pages. Live read-only checks confirmed anonymous/invalid-key denials, owner
capabilities, review/admin API enforcement and the outer preview gate. No remote
records were changed and no migration was needed.

Before committing the branch, the full repository `./test.sh`, app production
build, 18 importer tests, all 85 app Node tests and preview validation passed.
Preview validation checked 4,041 media references and 4,609 deploy assets.


## Boundary refactor prepared locally — not deployed

The `native-plants-boundaries` integration branch combines the shared Hedge
boundary refactor with Native Plants' typed contribution API, credential events
and explicit admin descriptors. No schema migration, reseeding, changed OAuth
configuration or new grant is required. The preview's current version and remote
content are unchanged by this work.

A later `npm run deploy:preview` publishes the v2 client and Worker together.
The Worker retains the previous PascalCase JSON endpoints for old open tabs;
retire those only after the compatibility window described in
[CONTRIBUTIONS.md](CONTRIBUTIONS.md). Multipart uploads and private media keep
their existing paths. The outer preview gate, signed cookies and independent
owner key retain their current behavior.

Use the normal app build, tests and `check:preview` before deployment. The new
Worker/SQLite tests exercise real generated clients, v1/v2 interoperability,
bounded streaming bodies, malformed DTOs, ownership, role revocation, quotas,
photo publication and explicit admin registration. Credential lifecycle tests
cover both documents, delayed responses, logout independence and disposal.


Local integration verification: the full repository gate passed; the production
app build, 18 importer tests, 95 Node tests and preview checks passed. The preview
check verified 4,041 media references and 4,610 assets. Cookie-renewal tests cover
both successful reads and malformed requests; unexpected storage errors remain
503 with private/no-store headers. No schema/migration files changed.

Chrome on an isolated local Worker verified anonymous review denial, owner-key
application, immediate admin navigation updates, read-only Identity and the
generated review queue. Verified contributor/curator operations and revocation
were exercised through the compiled Worker with SQLite and simulated providers;
this refactor did not repeat live Google/GitHub sign-in or browser file selection.
