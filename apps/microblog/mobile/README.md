# wt.fail — Capacitor Android POC

The mobile shell for the [Capacitor POC](../../../notes/HEDGE-CAPACITOR-microblog-BUILD-PLAN.md):
Microblog (wt.fail) as an Android app using **browser-OAuth + an opaque bearer session** (not a
native provider SDK). Personal-device proof of concept — not published.

> **Scaffold status.** These config/scripts were authored in a sandbox that cannot run `npm install`,
> the Android SDK, or a device — so the `android/` Gradle project is **not** generated here and nothing
> below has been executed on hardware. Run the commands on your machine. Versions are a starting point;
> `npm outdated` and bump the Capacitor major if a newer one is current.

## What already works (server side, on branch `hedge-capacitor-microblog`)

- `mobile_sessions` table + `Identity.Mobile` storage (hash-only, 30-day expiry) — C3a/C4a.
- `Hedge.MobileSession` bearer resolver, fail-closed, sharing one role policy with cookies — C3b.
- Bearer-aware comment writes on wt.fail; `POST /api/mobile/bootstrap` (anon session) and
  `GET /api/mobile/me` (bearer → identity) — C4a.

## What's still code, not device (do these next, they're not in this folder)

- **C2 — CapacitorHttp transport (F#):** a `Hedge.Http.Transport` over `CapacitorHttp.request`,
  constrained to `https://wt.fail`, attaching `Authorization: Bearer`. Until this exists the bundled
  app (origin `capacitor://localhost` / `https://localhost`) **cannot reach the API** — the web build
  resolves API calls relative to its own origin. `build:web` passes `API_ORIGIN=https://wt.fail MOBILE=1`
  for that transport to read.
- **C4b — login:** the OAuth native-return + PKCE one-time-code exchange + anon→verified merge.
- **C5 — app login flow:** open the system browser to the login URL, catch the `wtfail://auth`
  deeplink, exchange the code, store the bearer in secure storage.

## Prerequisites (your machine)

- Android Studio + Android SDK (Platform 34+), a JDK (17), and either a device with USB debugging or
  an emulator (AVD).
- The estate's existing **wt.fail Google OAuth web client** is reused unchanged — no new Google Cloud
  config, no Android OAuth client (browser-OAuth keeps the Worker as the OAuth client).

## First-time setup

```bash
cd apps/microblog/mobile
npm install
npm run build:web          # builds the web bundle into ./www (needs C2 to actually call the API)
npx cap add android        # generates ./android (Gradle project) — network + SDK required
```

Then register the deeplink scheme so `wtfail://auth?...` returns to the app. In
`android/app/src/main/AndroidManifest.xml`, inside the main `<activity>`:

```xml
<intent-filter>
  <action android:name="android.intent.action.VIEW" />
  <category android:name="android.intent.category.DEFAULT" />
  <category android:name="android.intent.category.BROWSABLE" />
  <data android:scheme="wtfail" android:host="auth" />
</intent-filter>
```

(Custom scheme is fine for a personal-device POC; PKCE makes an intercepted deeplink useless. Android
App Links backed by an `assetlinks.json` on wt.fail are the hardening upgrade.)

## Loops

- **Bundled build** (real POC origin — exercises the bearer path):
  `npm run build:web && npm run sync && npm run run:android`
- **Live reload** (fast F#/CSS iteration): `npm run dev:android` — points the WebView at the LAN Vite
  dev server on :3030. **Never ship this**; keep the dev URL and any cleartext allowance out of release
  config (it's not in `capacitor.config.json`).
- **Fastest "app on a phone" smoke** (no C2 needed): temporarily set `server.url` in
  `capacitor.config.json` to `https://wt.fail` so the WebView loads the live site directly (origin =
  wt.fail, so cookies work and the bearer path is bypassed). Good for a first visual; revert it to
  exercise the real bundled/bearer flow.

Generated dirs (`android/`, `node_modules/`, `www/`) are gitignored.
