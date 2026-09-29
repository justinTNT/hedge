# wt.fail — Capacitor Android POC

The mobile shell for the [Capacitor POC](../../../notes/HEDGE-CAPACITOR-microblog-BUILD-PLAN.md):
Microblog (wt.fail) as an Android app using **browser-OAuth + an opaque bearer session** (not a
native provider SDK). Personal-device proof of concept — not published.

> **Scaffold status.** These config/scripts were authored in a sandbox that cannot run `npm install`,
> the Android SDK, or a device — so the `android/` Gradle project is **not** generated here and nothing
> below has been executed on hardware. Run the commands on your machine. Pinned to **Capacitor 8**
> (current latest) so the reusable framework mobile stack is validated against the major a real app would
> ship on; the secure-storage API was checked identical across the 6→8 bump (see below). The on-device
> Capacitor-8 rebuild is yours to run — it needs the JDK 21 toolchain in Prerequisites.

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

This POC is on **Capacitor 8**, which sets the Android toolchain floor:

- **JDK 21 installed** (Capacitor 7 raised the minimum from 17 to 21; 8 keeps it). Capacitor 6 was the last
  major that ran on JDK 17 — that's the one real reason the first cut used 6. Your *global* `JAVA_HOME` need
  not be 21: `npm run add:android`/`sync` pin this project's Gradle to JDK 21 (`org.gradle.java.home`,
  resolved via `/usr/libexec/java_home -v 21`) so a machine that keeps an older JDK for other work still
  builds this app. Without a JDK 21 present, Gradle fails with `invalid source release: 21`.
- **Android Studio + Android SDK** at the versions Capacitor 8 requires (recent Android Studio, a current
  build-tools/Platform, AGP ≥ 8.x). Check the official [Capacitor upgrade guides](https://capacitorjs.com/docs/updating)
  (6→7 then 7→8) for the exact Android Studio / AGP / target-SDK minimums rather than trusting a number here.
- **Node ≥ 22** for `@capacitor/cli@8` (this repo already runs newer).
- A device with USB debugging or an emulator (AVD).
- The estate's existing **wt.fail Google OAuth web client** is reused unchanged — no new Google Cloud
  config, no Android OAuth client (browser-OAuth keeps the Worker as the OAuth client).

> **Upgrading an existing checkout from the earlier Capacitor 6 cut:** the `android/` project is
> gitignored and pinned to whatever Capacitor generated it, so `rm -rf android node_modules` and re-run
> the first-time setup below on the Capacitor-8 CLI — `cap sync` alone will not migrate a v6 project to v8.

## First-time setup

```bash
cd apps/microblog/mobile
npm install                # installs deps incl. capacitor-secure-storage-plugin
npm run build:web          # builds the web bundle into ./www
npm run add:android        # cap add android, then patch:android (deeplink + JDK-21 Gradle pin)
npm run sync               # cap sync android (re-applies the patches)
```

Use the `npm run add:android` / `npm run sync` wrappers, not raw `npx cap …`: they run `patch:android`
afterward, which registers the `wtfail://auth` deeplink and pins Gradle to JDK 21 — both edits land in the
generated, gitignored `android/`, so they must be re-applied after every scaffold (the wrappers do that).

**Secure bearer storage (#7):** the bearer is stored via `capacitor-secure-storage-plugin` (registered as
`SecureStoragePlugin`) — `guest-session.js` feature-detects it and keeps an in-memory cache so the
transport reads it synchronously. It is declared at `^0.13.0`, the release whose peer dependency is
`@capacitor/core >=8.0.0`. Its API is **identical** to the Capacitor-6-era `0.10.0` this POC first used —
same `SecureStoragePlugin` registration name and the same `get({key})→{value}` / `set({key,value})` /
`remove({key})` shape the wrapper calls — so nothing in `guest-session.js` changed across the Cap-6→8
bump. (Plugin version ↔ Capacitor major: `0.10.x`→Cap 6, `0.11/0.12`→Cap 7, `0.13`→Cap 8; bump it with
the `@capacitor/*` majors.) Without the plugin the store falls back to WebView `localStorage` (fine for a
quick spike, not for real 30-day sessions).

The deeplink scheme `wtfail://auth?...` (browser-OAuth return) is registered **automatically**:
`npm run add:android` and `npm run sync` both run `npm run deeplink`, which patches
`android/app/src/main/AndroidManifest.xml` in place (idempotently) to add this intent-filter to the main
`<activity>`:

```xml
<intent-filter>
  <action android:name="android.intent.action.VIEW" />
  <category android:name="android.intent.category.DEFAULT" />
  <category android:name="android.intent.category.BROWSABLE" />
  <data android:scheme="wtfail" android:host="auth" />
</intent-filter>
```

The manifest is generated + gitignored, so this script (`scripts/register-deeplink.mjs`) is the committed
source of truth — a rebuild always re-applies it. If it ever can't find `.MainActivity` it prints the
snippet to add by hand. (Custom scheme is fine for a personal-device POC; PKCE makes an intercepted
deeplink useless. Android App Links backed by an `assetlinks.json` on wt.fail are the hardening upgrade.)

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
