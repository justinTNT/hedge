# Hedge Capacitor — Microblog POC build plan (easy-mode)

Execution plan derived from [the handoff spec](HEDGE-CAPACITOR-microblog-plan.md). Where they
disagree, the handoff is the reference for *intent* and this file is the *scope + commit order* we
actually build. This is a proof of concept — not published, personal-device only.

## Baseline (recorded)

- **Branch:** `hedge-capacitor-microblog`, off `main`.
- **Baseline SHA:** `516f2d9` (includes the identity module, delegated admin, and the predeploy
  schema-check guardrail).
- **`./test.sh`:** green this session at this tree (exit 0, "All tests passed"; the one `FAIL`
  in the log is the intentional negative-control assertion).
- Working tree also carries unrelated dirty notes (`notes/guest-uploads-signed-cookie.md` +
  untracked notes) inherited from `main` — **do not touch**.

## Easy-mode scope decisions (what makes this small)

These are settled; they cut the handoff's conservative surface roughly in half.

1. **Browser-OAuth, not native-SDK.** The Worker stays the OAuth client (as web already is); the app
   never handles provider tokens. No provider JWT verification, no per-platform OAuth clients, no
   `@capgo/capacitor-social-login`. The existing wt.fail Google web OAuth client is reused **unchanged**.
2. **Android only.** No iOS, no Apple, no paid Apple Developer account, no store publication, no OTA.
3. **dev = live wt.fail.** No isolated Worker/D1/R2. `mobile_sessions` is an additive, harmless table.
   **NB:** its migration must be applied to `wt-fail-db` *before* deploying code that reads it — the
   predeploy schema-check guardrail (`check --remote`) now enforces exactly this, so a miss is blocked
   at deploy, not discovered as a 500. Do **not** use `deploy`/`deploy:all` as prototype setup
   (handoff §Stage 1).
4. **Mobile completion policy = "activate + merge, no claim."** Native login quietly reattributes the
   device's anonymous content to the verified identity. Justification: personal device ⇒ one person ⇒
   the device's anon content is theirs. Injected as a *completion policy* (per-client), reusing the
   existing `OAuthDeps` completion-policy seam (main: `IsCuratorReturn`; Native Plants:
   `ActivateOnReturn` — confirm exact shape at implementation). Web keeps its claim choice untouched.
   - **Explicit boundary accepted:** on a *shared* phone (log in as A → browse anon → log in as B),
     B inherits the in-between content. Fine for an unpublished personal-device POC; recorded, not silent.
   - Mobile sign-out is plainly "sign out" — it does **not** un-merge (no parked-anonymous state).

## What stays ugly (and therefore gets real tests)

Two areas are irreducible even under easy-mode. Everything else is plumbing.

- **Session/credential handoff** — a second auth carrier (opaque bearer) beside a signed cookie we
  can't reuse cross-origin; one-time-code (PKCE) exchange; **fail-closed** bearer resolution.
- **The merge itself must be atomic** — an in-flight request mid-login can't reinstate the old anon
  author afterward; a signed-out/revoked bearer can't come back (handoff §123). "Merge everything" is
  simpler than a claim choice, but it still cannot be racy. Plus Google subject continuity: phone login
  resolves the *same* identity as web (§119), and the merge flows into that adopted identity.

## Existing seams (verified this session)

| Seam | File | Role in this work |
| --- | --- | --- |
| OAuth callback | `packages/hedge/src/Hedge/Router.fs:339-389` | Add the native-return branch after `OnOAuthComplete`; ends today at `redirectResponseOpt … cookie` (`:387`). |
| OAuth config (injected hooks) | `Router.fs:197-203` (`OAuthConfig`, `OAuthComplete`) | Add an optional `CompleteNative` hook (mirrors `OnOAuthComplete`); `None` on browser-only hosts. |
| Guest policy | `packages/hedge/src/Hedge/GuestSession.fs` (`requireGuest :119`, `resolveOrBootstrap :152`, `adopt :166`) | Bearer path yields the same `Accepted { GuestId }`; never mints from a bearer. |
| Role policy | `packages/hedge/src/Hedge/AccessControl.fs:48-62` | Refactor so the `GuestId → ActiveSubject → HasGrant` tail (`:53-61`) is shared by cookie + bearer — role policy not duplicated. |
| Transport contract | `packages/hedge/src/Hedge/Http.fs:53` (`Transport`), `:29` (`Response`), `:70-100` | CapacitorHttp adapter; `TransportFailure` only when the request never completes. |
| Identity completion | `packages/modules/identity/src/Server/Handlers.fs:96` (`onOAuthComplete`) + its `OAuthDeps` | Reuse resolution + `AdoptGuestId`; supply the mobile completion policy. |

## Commit order

Each is a reviewable commit; regenerate + commit any gen-checked artifact and keep `./test.sh` green.

**C0 — Browser regression baseline (before any shared change).** Per handoff §142, record a manual
run of web: guest comment → login → claim → switch → disconnect, plus owner/delegated-admin
authorization. Capture as a short checklist in `notes/` with observed results. No code.

**C1 — Capacitor Android shell (Stage 1a).** Scaffold `apps/microblog/mobile` (`@capacitor/android`),
app id/name, a mobile-only Vite build to a **separate** output dir (never clobber a tenant `_site`),
tenant assets/styles per the unset-slug rule. API origin pinned to `https://wt.fail` (do **not**
inherit Vite's `darwin.news` default). Dev scripts `mobile:dev:android`, `mobile:build`.
*Exit:* feed → item → back on a physical device, live-reload for F#/CSS, bundled build runs without
the dev server, public browsing needs no auth.

**C2 — CapacitorHttp transport (Stage 1b).** Implement `Hedge.Http.Transport` over
`CapacitorHttp.request`: constrained to the wt.fail origin, auto-redirect disabled for credentialed
calls, response normalized to the text-body `Response`, failures mapped per `Http.fs`. Wire into the
mobile Blog host. Still anonymous (no bearer). *Exit:* reads flow through native HTTP; error contract
preserved; no browser CORS change needed (native requests don't preflight — handoff §105).

**C3 — Opaque mobile session + bearer resolver (Stage 2a) — UGLY, tested.**
- `mobile_sessions` table via the identity module (gen + additive migration; store only the token
  **hash**).
- Bearer resolution: `Authorization: Bearer` → hash lookup → not-expired → `Accepted { GuestId }`;
  **fail closed** on an invalid bearer (no cookie fallback, §101). Refactor `AccessControl` tail so
  cookie + bearer share `ActiveSubject → HasGrant`.
- Native **anonymous bootstrap** endpoint: mint an anon mobile session at first launch.
- Tests: valid/invalid/expired bearer; fail-closed (no silent cookie fallback); anon bearer → anon
  guest, never a role; bearer-carried request can't reach admin.

**C4 — Native OAuth return + one-time-code exchange (Stage 2b) — UGLY, tested.**
- Login route (`Router.fs:317`): accept `&challenge`, carry it into the HMAC-signed state alongside
  `guestId`/`returnTo`.
- Callback (`:339`): when the signed `returnTo` is the allowlisted native scheme (`wtfail://auth`),
  mint a short-lived **one-use** code bound to `{ resolvedSubject, challenge }` where
  `resolvedSubject = completion.AdoptGuestId |> Option.defaultValue a.GuestId`; 302 to the deeplink.
- `/api/auth/mobile/exchange`: verify `sha256(verifier) == challenge`, consume the code atomically,
  mint the bearer bound to the subject, rotate + invalidate the prior anon bearer.
- Inject the mobile completion policy (activate + merge the device anon guest; no claim).
- Tests: PKCE mismatch rejected; code one-use (replay rejected); concurrent exchange rejected; state
  tamper rejected; scheme allowlist enforced.

**C5 — App browser-OAuth flow + secure storage (Stage 2c).** `@capacitor/browser` opens
`/api/auth/google/login?returnTo=wtfail://auth&challenge=…` in the **system** browser (never the
WebView — Google blocks embedded WebViews). `@capacitor/app` `appUrlOpen` catches the deeplink →
`Browser.close()` → exchange → store the bearer in a **Keystore-backed** plugin (never Preferences/
localStorage). Custom scheme registered in `AndroidManifest.xml`. Sign-out revokes the session
server-side + clears local state; a late login result can't restore it. *Exit:* native Google login →
server-verified Hedge identity; restart persists; sign-out stays signed out.

**C6 — Semantics tests (Stage 3, trimmed) — UGLY, tested.** Seeded DB; exercise real module
endpoints (a `/me` 200 is not evidence). Trimmed table:
anonymous browse/comment → login **auto-merges** the anon content (no claim) → next comment uses the
active identity · same Google account web+Android → same identity, grants preserved · kill/relaunch →
restores only a valid session (no bootstrap loop) · sign-out during pending login → late result can't
restore, revoked bearer fails on replay · different users can't cross-activate · grant removed / active
made anonymous → role endpoints reflect it immediately, no admin via a mobile token · root vs `/st`
base (moot on wt.fail root, assert the one-prefix rule anyway). Plus an explicit **atomic-merge race**
test and a **subject-continuity** test (verified `sub` vs the existing Google user identifier).

**C7 — Docs + consuming-host example (Handoff).** Update `HEDGE-CAPACITOR-microblog-plan.md` with
selected plugin versions, commands, device results, remaining blockers. Add a short example of how a
second host (Native Plants next) supplies API address / providers / transport / identity adapter with
**no** microblog import. Re-run `./test.sh` + the C0 browser regression. Do not publish store builds.

## Risks / gotchas

- **Regen discipline** is the top failure mode — regenerate + commit every gen-checked artifact
  (schema, module surface, per-site glue incl. idealist) after the `mobile_sessions` addition.
- **Keystore plugin** choice + reinstall/backup behavior — spike and document (handoff §Stage 2).
- **Custom scheme, not App Links** for the POC — any app can claim `wtfail://`; PKCE makes an
  intercepted deeplink useless. Note App Links as the hardening upgrade.
- **Do not** merge the Native Plants uncommitted shared-auth changes; inspect `ActivateOnReturn` as a
  reference for the completion-policy shape only (handoff §22).
- **Anonymous bootstrap** must not silently mint a session on a write — bootstrap is an explicit call
  (handoff §Stage 3 row 1).
- One identity authority per host — don't leave a second `window.HedgeGuest` racing the Capacitor
  app model (handoff §107).
