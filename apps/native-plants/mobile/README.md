# Native Plants for Android

A Capacitor 8 shell for the existing Native Plants app. App ID: `au.nativeplants.guide`.
It bundles the interface (under 0.5 MB before the Android runtime), then uses the
private Cloudflare preview for catalogue data, photographs, identity and contributions.
An internet connection is required; this is not an offline field-guide release.

## Build and run

From `apps/native-plants`, install the app dependencies and build the server/client:

```sh
npm ci
npm run build
cd mobile
npm ci
npm run build:web
npm run add:android       # first time only
npm run build:android
npm run run:android
```

The debug APK is `android/app/build/outputs/apk/debug/app-debug.apk`.
It is suitable for direct installation/testing, not a Play Store release.
Android Studio, Android SDK 36 and Java 21 are required. On macOS the setup script
finds Java 21 and the usual SDK location; elsewhere configure `JAVA_HOME` and
`ANDROID_HOME`. `npm run sync` reapplies the callback intent, icon, backup policy
and local Java configuration. Generated `android/` and `www/` remain ignored.
Dependency versions and the npm lockfile are committed.

`build:web` writes only `mobile/www`; the website's `_site` remains independent.
It does not copy the original photographs, admin shell, local secrets or databases.
The default API is `https://native-plants-preview.justin-8ee.workers.dev`.
An explicit `API_ORIGIN=https://…` can select another compatible HTTPS deployment.
The origin must be fixed at build time; no credentials go in build variables.

## On-device development

Run `dotnet fable src/Client/Client.fsproj -o dist/client --watch` from the app
and `npm run dev:web` from `mobile` in separate terminals. Then run
`npm run dev:android` in `mobile`. `DEV_HOST` optionally overrides LAN discovery.
The phone/emulator needs access to this computer on port 3038. API traffic still
goes to the private HTTPS Worker. The live-reload script restores the committed
Capacitor config; run `npm run build:android` to rebuild the installed bundled
version afterwards. Never distribute a live-reload build.

## Preview and login

The app never asks for the preview password. The gate recognises it by a fixed
client marker header that `mobile/transport.mjs` adds to API-origin requests and
the gate strips before Hedge sees them. The marker is not a secret: handing out
the APK is what grants device access, and the password remains the web's
boundary. The APK contains neither password nor admin key. The app's
`Authorization: Bearer` remains available to identity and role checks. Browser
Basic authentication and the existing OAuth callback URLs remain unchanged.

Use Login → Google (or GitHub when configured). OAuth opens the system browser.
The login, callback and return routes sit outside the preview gate's credential
check, so the browser is never asked for the preview password. The final redirect is
`nativeplants://auth?code=…`, with a short-lived one-use PKCE code, never a bearer.
The shared session runtime exchanges it and stores the token securely. The app
retains the species page and refreshes personal content after login. If Android
kills the app during browser login, reopen it and start login again.

Notes, corrections, ID requests, uploads, personal hero selection and offers use
the same owner checks, quotas and roles as the website. Private media is fetched
with credentials and displayed using temporary object URLs, released on unmount.
Curators retain their role-specific review access; the owner admin opens in the
system browser and still requires its separate admin key. Android Back closes a
photo/account panel, returns through navigation, or minimizes at the root.
No broad media-library, location or camera permission is requested; uploads use
the existing file picker. Native bridge logging and Android backup are disabled
so credentials are not recorded/restored through those paths.

## Server rollout

Back up D1, then apply `../migrations/0004_mobile.sql` once to databases created
before this feature, including the preview. It adds identity's nullable
`superseded_by` field and the opt-in mobile session/code tables. Fresh databases
use generated `schema.sql`; do not run this migration again on a fresh schema.
Deploy the ordinary website/Worker with `npm run deploy:preview` from the app.
No catalogue reseed or OAuth-client change is required.

## Verification

The app's `npm test` covers browser behavior plus native PKCE/one-use codes,
bearer precedence, account isolation, notes/photos, limits, role revocation,
logout, private preview transport and migration compatibility. The shared client
callback tests reject another app's URL and clean up failed/cancelled login.
Run `npm run check:preview` and the repository `./test.sh` before publishing.

Manual device checks remain: first launch opens without a password, browse/search, Google login
and return, upload an actual camera/library photo, private note/ID request,
restart with the saved login, logout and a second account. This environment can
build/install the APK and inspect startup logs; completing real provider login
requires the tester's account.

Native HTTP uses Capacitor's patched fetch so multipart uploads remain supported:
https://capacitorjs.com/docs/apis/http

## Offline

The app keeps the guide on the device and lets the notebook work without signal.

- **First launch.** The bundle carries the catalogue and every account as the worker served them at
  build time (`npm run build:mobile` runs `scripts/seed-mobile.mjs` over the local import), so the guide
  opens with no network from the very first start; the first connection refreshes anything that changed.
  With no network, the guide opens from what is saved and a signed-in person stays signed in to their
  saved notebook; the server judges the sign-in when it is next reachable.
- **Reading.** Catalogue reads go through the framework's read cache (`Client.Offline`, IndexedDB
  database `native-plants`): the network is tried first, and when it gives no answer the saved copy is
  shown with an "Offline: showing the guide as saved on …" notice. After each catalogue load with a new
  revision, every plant account is fetched in one request (or one by one against an older server) and
  saved, so every page opens offline. Photographs are
  resolved from the kept copy first, and catalogue photographs that have been viewed are kept. The signed-in
  person's notebook snapshot is cached under their identity, so it opens offline too.
- **Searching.** Name search and descriptive search ("colourful bark") both run on the phone over the
  saved accounts, so they work without signal; while the first warm-up is still running, the results
  say how many accounts have been searched so far.
- **Writing.** Notes, edits, deletions, hero choices and photographs made while the network is away are kept
  on the phone and shown in the notebook with an "On this phone · will send when online" marker; the local
  limits count them. They are sent in order when the network returns, on resume, or with "Send now". A later
  edit folds into an unsent one; deleting something the server never received simply drops it. Something the
  server refuses (a full notebook, an item changed elsewhere, an expired sign-in) is marked "Not sent" with
  the server's reason, and offers "Send my version" (resent over the server's current revision) or "Discard".
  Queued changes belong to the identity that made them and wait for that identity to sign in again.
- **Photograph packs.** The "On this phone" screen (`/offline`, also linked from the offline notice)
  keeps the guide's photographs ahead of need: maps and glossary drawings always (about 3 MB), every
  photograph at screen size by default (about 105 MB), and full-size photographs as an opt-in (about
  450 MB). Packs download by themselves on Wi-Fi, or on mobile data when "Wi-Fi only" is switched off,
  resume where they stopped, and can be stopped or removed; the screen shows what is saved, the storage
  used and any changes waiting to send.
- **Your own photographs.** A photograph you take or add here stays on the phone after it is sent, and
  the photographs in your notebook are kept as you open each plant, both at screen size and full size.
  They are stored under your account and only your account can see them; the "On this phone" screen
  counts them and can remove them.
- **Camera.** On the bundled app the photo roll and the attachment picker offer "Take a photograph" beside
  "Add a photograph". It opens the system camera through the browser's capture hint, with no camera
  permission of the app's own; the system camera does not add the shot to the gallery, but the prepared
  photograph is kept on the phone until it is sent and then lives in the roll.

Design and status: `notes/HEDGE-offline.md`.
