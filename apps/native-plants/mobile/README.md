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

Enter the existing preview password on first launch (see `../PREVIEW.md` for its
local location). It is saved in Android's encrypted secure store, independently
of Hedge's revocable account token. The APK contains neither password nor admin
key. The native preview header is stripped before Hedge sees a request; its
`Authorization: Bearer` remains available to identity and role checks. Browser
Basic authentication and the existing OAuth callback URLs remain unchanged.

Use Login → Google (or GitHub when configured). OAuth opens the system browser,
which may also ask for the preview password the first time. The final redirect is
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

Manual device checks remain: enter preview password, browse/search, Google login
and return, upload an actual camera/library photo, private note/ID request,
restart with the saved login, logout and a second account. This environment can
build/install the APK and inspect startup logs; completing real provider login
requires the tester's account.

Native HTTP uses Capacitor's patched fetch so multipart uploads remain supported:
https://capacitorjs.com/docs/apis/http
