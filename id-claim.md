# Plan: Identity Claiming

## Context

Hedge sites have anonymous guests (auto-generated cookie ID + random animal name). Identity Claiming lets users upgrade their anonymous account by linking it to an OAuth provider. The provider supplies **name** and **profile picture**. Picture is editable after claiming.

Guest sessions are untouched. Claiming just populates the existing `Guest` record with real name + picture. OAuth redirect flow for all providers, starting with Google. FedCM can be layered on later.

## Two-layer model

```
Framework layer (unchanged):  cookie(hedge_guest=uuid) → resolveGuest()
Identity layer (new):         guest_id → Identity record → { provider, name, picture }
```

## User flow

1. Anonymous guest is "Brave Falcon" with default avatar
2. Clicks "Claim your identity" → sees enabled providers (from Config.fs)
3. OAuth redirect → provider consent screen → callback
4. Guest record populated: name + picture from provider
5. Comments now show real name + picture
6. Can edit picture later (upload to R2, already have blob infrastructure)
7. On new device: "Sign in with Google" → same providerUserId → same Guest → cookie linked

## Domain model

`Domain.fs`:

Existing `Guest` type already has the right shape:
```fsharp
type Guest = {
    Id: PrimaryKey<string>
    Name: string              // ← populated from provider on claim
    Picture: string           // ← populated from provider, editable after
    SessionId: string         // ← links to hedge_guest cookie
    CreatedAt: CreateTimestamp
    DeletedAt: SoftDelete option
}
```

New type for provider link (supports multiple providers per guest):
```fsharp
type IdentityClaim = {
    Id: PrimaryKey<string>
    GuestId: ForeignKey<Guest>
    Provider: string            // "google", "facebook", "microsoft", "github"
    ProviderUserId: string      // unique ID from provider
    CreatedAt: CreateTimestamp
}
```

## API

```
GET  /api/auth/:provider/login     → redirect to provider consent screen
GET  /api/auth/:provider/callback  → exchange code, populate Guest, store claim
GET  /api/auth/me                  → current Guest if claimed (name, picture), null if anon
```

## OAuth flow (generic, same for all providers)

1. `/api/auth/google/login` → generate `state` = HMAC-SHA256(timestamp + guestId, secret), redirect to Google authorize URL
2. Google → `/api/auth/google/callback?code=xxx&state=yyy`
3. Verify state signature (unforgeable + fresh within 5 min window), exchange code for token, fetch userinfo (name + picture)
4. Find-or-create Guest by providerUserId, upsert IdentityClaim
5. Set `hedge_guest` cookie to the claimed Guest's SessionId
6. Redirect back to app

**State nonce strategy: signed state.** HMAC-sign the state parameter with a server secret (`ADMIN_KEY` or dedicated `OAUTH_SECRET`). On callback, verify the signature and check the embedded timestamp for freshness. Zero storage, no KV, no table, no cleanup. The state token just needs to be unforgeable and fresh — HMAC gives both.

## Provider config

**Config.fs** — declares enabled providers (client renders buttons):
```fsharp
type GlobalConfig = {
    SiteName: string
    Features: FeatureFlags
    IdentityProviders: string list   // e.g. ["google"; "microsoft"; "github"]
}
```

**wrangler.toml + Env.fs** — runtime secrets per provider (`GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`). Prod secrets via `wrangler secret put`.

**Local cache** — after first claim, store provider preference in localStorage. Returning visitors get that provider surfaced first.

## Provider registry

Framework `OAuth.fs` — hardcoded configs by popularity:

| Priority | Provider | authorize_url | token_url | userinfo_url |
|---|---|---|---|---|
| 1 | Google | accounts.google.com/o/oauth2/v2/auth | oauth2.googleapis.com/token | googleapis.com/oauth2/v2/userinfo |
| 2 | Facebook | facebook.com/v18.0/dialog/oauth | graph.facebook.com/v18.0/oauth/access_token | graph.facebook.com/me?fields=name,picture |
| 3 | Microsoft | login.microsoftonline.com/.../authorize | login.microsoftonline.com/.../token | graph.microsoft.com/v1.0/me |
| 4 | GitHub | github.com/login/oauth/authorize | github.com/login/oauth/access_token | api.github.com/user |

Active providers = intersection of Config.fs list and env vars present.

## Implementation order

1. **`OAuth.fs`** (framework) — provider registry, generic OAuth flow (generateAuthUrl, exchangeCode, fetchUserinfo), signed state (HMAC generate + verify)
2. **Domain model** — `IdentityClaim` type in Domain.fs, `npm run gen` → table + codecs
3. **Auth routes** — add `/api/auth/*` to `createWorker` in Router.fs
5. **Handlers** — claim logic: OAuth callback → populate Guest → store IdentityClaim
6. **Client** — "Claim identity" button, provider list from config, `GET /api/auth/me` on init
7. **Picture editing** — after claim, allow upload via existing R2 blob infrastructure
8. **Google** — register OAuth app, set env vars, test full flow

## Files to modify

**Framework (`packages/hedge/src/Hedge/`):**
- `OAuth.fs` (new) — provider registry + generic OAuth helpers
- `Router.fs` — auth routes in createWorker
- `Hedge.fsproj` — add OAuth.fs

**App (`apps/microblog/`):**
- `src/Models/Domain.fs` — `IdentityClaim` type
- `src/Models/Config.fs` — `IdentityProviders` in GlobalConfig
- `src/Server/Env.fs` — provider env vars (GOOGLE_CLIENT_ID, GOOGLE_CLIENT_SECRET)
- `wrangler.toml` — Google client ID/secret
- `src/Client/Types.fs` — claimed identity in Model
- `src/Client/Shared.fs` — claim button + identity display (name + picture)

## Verification

1. `npm run gen` — generates identity_claims table + codecs
2. `dotnet build` — server and client compile
3. `./test.sh` — golden model + scaffold pipeline pass
4. Manual: click "Sign in with Google" → consent → callback → name + picture stored
5. Manual: clear cookies → sign in again → same Guest found via providerUserId
6. Manual: anonymous user can still comment without claiming (existing behavior unchanged)
