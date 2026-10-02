# Auth — email magic-link hardening TODOs

Two deferred hardening items for the passwordless email sign-in (branch oidc-email-auth, merged to main).
Neither blocks the feature as merged, but the first **must** be handled before enabling a real email sender
on a public site. Flagged across code reviews A/B/C.

## 1. Rate-limiting on `POST /api/auth/email`  (reviewers A#4, B#3, C#3)

`POST /api/auth/email` (packages/hedge/src/Hedge/Router.fs) calls the configured sender with no throttle,
auth, or origin check — so any client can trigger outbound mail to arbitrary addresses. On a site with a
real `EMAIL_PROVIDER` that means provider cost + sender-reputation / deliverability damage.

Currently: **documented only** (comment at the endpoint). No enforcement in the worker. Safe today because
every site has `EMAIL_PROVIDER` unset or `stub` (email off).

Before flipping any site to a real sender, add:
- **Edge:** Cloudflare WAF rate-limiting by IP on `/api/auth/email` (+ optionally a Turnstile token check).
- **Server:** a per-`(email, IP)` D1 counter over a short window (fits the existing D1 storage; mirror the
  kind of lightweight table used elsewhere). Reject over the limit before contacting the provider.

## 2. Strict single-use magic-link token  (reviewer B#2; A#3/C#6 judged it an acceptable tradeoff)

The token is a stateless HMAC (packages/hedge/src/Hedge/Email.fs `generateEmailToken`/`verifyEmailToken`),
reusable within its 15-min TTL. As merged it is **bound to the requesting guest** (495f216), so only the
requesting browser can reuse it, and not after that guest changes (logout) — which removed the exploitable
cross-browser case (review B#1). The residual is same-browser replay within the TTL (standard passwordless
tradeoff).

For strict single-use, add a stored one-time record and consume it atomically **before** account mutation —
mirror `Identity.Mobile` `mobile_auth_codes` (a `DELETE … WHERE id=? … RETURNING` consume). That is a new
table + migration, which is why it was deferred given session-binding already closes the attack.

See [[auth-providers-oidc-email]] (memory) and the auth plan's deferred list.
