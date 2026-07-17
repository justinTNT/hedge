# Plan: Progressive Identification

## Context

Hedge sites have anonymous guests (auto-generated cookie ID + random emoji/color name). Progressive Identification lets users optionally upgrade by linking an OAuth provider. The key insight: **Identity is the unit of attribution, not Guest.** Guest is a session container; Identity is what comments are attributed to.

Each identity transition — claim or revert — gives the user a choice: **merge** (bring your comments with you) or **abandon** (leave them behind under the old identity). This enables true progressive identification without forcing users to commit.

OAuth redirect flow for all providers, starting with Google. FedCM can be layered on later.

## Protocol vs Policy

The framework and app have distinct responsibilities:

**Framework (Protocol)** — handles the OAuth dance, which is universally tedious and risky to get wrong:
- HMAC state generation/verification
- Authorization URL construction
- Code-for-token exchange
- Provider userinfo fetching and normalization
- Route interception (`/api/auth/:provider/login`, `/api/auth/:provider/callback`)
- Handoff: "I verified this user is `user@gmail.com` with this name and picture. Do whatever you want with that."

**Application (Policy)** — decides what a verified identity *means*:
- The Identity data model and `identities` table
- Guest ↔ Identity relationships
- Merge/abandon logic (reassigning comments is a domain concern — a different app would reassign different things)
- Identity resolution (how to look up the active identity for a guest)
- The claim/revert UI

This separation ensures Hedge stays lean. A whiteboard app would reassign EditHistory; a polling app would reassign Votes. The framework doesn't need to know.

## Three-layer model

```
Session layer:    cookie(hedge_guest=uuid) → resolveGuest() → Guest
Identity layer:   Guest → active Identity → { provider, name, picture, email }
Attribution:      Comment → Identity (not Guest)
```

## Data model (app-level — `apps/microblog/src/Models/Domain.fs`)

### Guest (slimmed — auth/session only)

```fsharp
type Guest = {
    Id: PrimaryKey<string>
    SessionId: string             // links to hedge_guest cookie
    CreatedAt: CreateTimestamp
    DeletedAt: SoftDelete option
}
```

No more `Name` or `Picture` on Guest. Display info lives on Identity. No back-reference to Identity — dependency is one-way.

### Identity (new — app domain type)

```fsharp
type Identity = {
    Id: PrimaryKey<string>
    GuestId: ForeignKey<Guest>
    Provider: string              // "anonymous", "google", "facebook", "microsoft", "github"
    ProviderUserId: string        // unique ID from provider; empty for anonymous
    Name: string                  // display name
    Picture: string               // R2 URL (downloaded from provider on claim, or generated SVG data URL)
    Email: string option          // captured from provider
    ActivatedAt: int option       // epoch seconds; most recent wins. null = never active
    CreatedAt: CreateTimestamp
}
```

Active identity = `SELECT * FROM identities WHERE guest_id = ? AND activated_at IS NOT NULL ORDER BY activated_at DESC LIMIT 1`. Switching identity just sets `ActivatedAt` to now. Natural activation history for free.

This is an **app domain type**, not a framework type. The framework doesn't own the schema — it hands off verified provider data and lets the app decide how to store it. Different Hedge apps can model identity differently (or not at all, if OAuth is disabled).

### ItemComment (migrated — references Identity, not Guest)

```fsharp
type ItemComment = {
    ...
    IdentityId: ForeignKey<Identity>   // was: GuestId: ForeignKey<Guest>
    ...
}
```

### GuestSession (removed)

The `guest_sessions` table is eliminated. It was a denormalization of display name that now lives on Identity.

**Manual cleanup required** — removing GuestSession from Domain.fs only deletes the generated code. These raw SQL references must be rewritten by hand:

| File | Line | Current | Change to |
|---|---|---|---|
| `Router.fs` | ~163 | `SELECT display_name FROM guest_sessions WHERE guest_id = ?` | **Remove.** Replace with `ResolveIdentity` callback (see Inversion of Control below). |
| `Router.fs` | ~166–173 | Returns `{ guestId, displayName }` | Framework calls app-provided `ResolveIdentity`; app returns whatever shape it wants. |
| `Handlers.fs` | ~131–136 | `INSERT OR REPLACE INTO guest_sessions ...` | **Remove entirely.** Guest session upsert was display name caching; no longer needed. |
| `Handlers.fs` | ~141 | `INSERT INTO comments (..., guest_id, ...)` | Replace `guest_id` with `identity_id`. |
| `Handlers.fs` | ~118 | `let guest = resolveGuest request` | Still needed — but after resolving guest, also resolve active Identity to get the identity_id for comment insertion. |

**Move guest/identity initialization out of submitComment.** Currently `submitComment` in Handlers.fs handles guest session creation as a side effect of commenting. With Identity as the primary driver, guest + anonymous identity creation should happen earlier — via an app-provided callback on first visit. This ensures every request has a resolved Identity before any handler runs, and keeps handlers focused on their own domain logic.

## User flows

### Flow 1: Anonymous browsing (unchanged)

1. First visit → cookie set, Guest created, anonymous Identity created (provider="anonymous", name="Brave Teal Ghost", picture=generated SVG)
2. Identity's ActivatedAt set to creation time
3. Comments attributed to anonymous Identity
4. All existing behavior preserved

### Flow 2: Claiming (anonymous → provider)

1. User clicks "Sign in with Google" → OAuth redirect
2. Framework handles OAuth dance → callback returns verified `UserInfo`
3. **Framework hands off to app** via `OnOAuthComplete` callback
4. App downloads provider picture to R2
5. **Check**: does this providerUserId already exist in Identity table?
   - **No** (first claim): create new Identity (provider="google"), prompt user
   - **Yes** (returning user, new device): cross-device merge — see Flow 4
6. Prompt: "Bring your comments with you, or start fresh?"
   - **Merge**: bulk update `SET identity_id = :new WHERE identity_id = :old` on item_comments
   - **Abandon**: leave comments pointing at anonymous Identity
7. New Identity's ActivatedAt set to now
8. Redirect to return_to URL

### Flow 3: Reverting (provider → anonymous or previous provider)

1. User opens identity switcher → sees all their Identities (anonymous + any providers)
2. Selects a previous identity
3. Same prompt: merge or abandon
   - **Merge**: comments from current identity reassigned to selected identity
   - **Abandon**: comments stay under current identity
4. Selected Identity's ActivatedAt set to now

### Flow 4: Cross-device merge

1. Device 2 has Guest B (anonymous Identity B)
2. User signs in with Google → providerUserId matches Identity A (owned by Guest A on Device 1)
3. Prompt: "Bring your comments from this device?"
   - **Merge**: Identity B's comments reassigned to Identity A
   - **Abandon**: Identity B's comments stay attributed to Identity B (orphaned anonymous identity, still visible)
4. Identity B transferred to Guest A (GuestId updated) so user retains access to it in identity switcher
5. Guest B soft-deleted
6. Device 2 cookie updated to Guest A's SessionId

### Flow 5: Re-authentication (same provider, refresh identity)

1. User signs in again with a provider they've already linked
2. Userinfo fetched — name/picture/email updated on existing Identity
3. New picture downloaded to R2, old one can be cleaned up
4. No merge/abandon prompt needed (same identity, just refreshed data)

## OAuth flow

### State token

`state` = base64url(`timestamp|guestId|returnTo|hmac`)

- `timestamp`: epoch seconds
- `guestId`: current guest's ID
- `returnTo`: URL-encoded page path the user was on
- `hmac`: HMAC-SHA256 of `timestamp|guestId|returnTo` using `OAUTH_SECRET` (or `ADMIN_KEY`)

On callback: verify HMAC, check timestamp freshness (15 min window). Zero storage.

### Flow (generic, all providers)

```
1. GET /api/auth/:provider/login       [FRAMEWORK]
   → parse provider from URL
   → generate state token (embed guestId + returnTo from Referer)
   → 302 redirect to provider authorize URL
     ?client_id=...&redirect_uri=.../callback&scope=...&state=...

2. GET /api/auth/:provider/callback    [FRAMEWORK]
   → verify state HMAC + freshness
   → POST to provider token endpoint (exchange code for access_token)
   → GET provider userinfo endpoint (name, picture URL, email)
   → normalize into UserInfo { name, pictureUrl, email, providerUserId, provider }
   → call app's OnOAuthComplete callback with (guestId, userInfo, returnTo)
   → app returns a redirect URL (e.g., /auth/claim?identity=:id&returnTo=:path)
   → 302 redirect to that URL

3. GET /auth/claim                     [APP — client-side SPA route]
   → client shows merge/abandon prompt
   → POST /api/auth/activate { identityId, merge: bool }

4. POST /api/auth/activate             [APP — app route, not framework]
   → if merge: bulk update comments from old identity → new identity
   → set new Identity.ActivatedAt to now
   → if cross-device: transfer identities, soft-delete old guest, update cookie
   → 200 OK

5. Client redirects to returnTo
```

Note: steps 1–2 are framework (Protocol). Steps 3–5 are app (Policy).

## API routes

### Framework-owned (Router.fs)

```
GET   /api/auth/me                  → calls app's ResolveIdentity callback (replaces /api/me)
GET   /api/auth/:provider/login     → 302 redirect to provider consent screen
GET   /api/auth/:provider/callback  → OAuth dance → calls app's OnOAuthComplete callback → redirect
```

### App-owned (generated or hand-written in Handlers.fs)

```
POST  /api/auth/activate            → apply identity switch (merge or abandon)
POST  /api/auth/revert              → switch to a previous identity (merge or abandon)
```

## Inversion of Control — WorkerConfig

The framework never queries the `identities` table directly. Instead, the app provides callbacks:

```fsharp
type UserInfo = {
    Name: string
    PictureUrl: string
    Email: string option
    ProviderUserId: string
    Provider: string
}

type OAuthConfig = {
    Secret: string
    Providers: Map<string, {| ClientId: string; ClientSecret: string |}>
    /// Called by /api/auth/me. App resolves guest → active identity (or null for anon).
    ResolveIdentity: D1Database -> string -> JS.Promise<obj option>
    /// Called after successful OAuth callback. App stores the identity, returns redirect URL.
    OnOAuthComplete: D1Database -> R2Bucket -> string -> UserInfo -> string -> JS.Promise<string>
    //                                        guestId    userInfo    returnTo    redirectUrl
}

type WorkerConfig = {
    Routes: WorkerRequest -> obj -> ExecutionContext -> JS.Promise<WorkerResponse> option
    Admin: (WorkerRequest -> obj -> Route -> JS.Promise<WorkerResponse> option) option
    OAuth: OAuthConfig option    // None disables auth routes entirely
}
```

This eliminates the existing coupling where Router.fs hardcodes SQL against `guest_sessions`. The framework asks; the app answers.

## Provider config

### Config.fs — enabled providers (client renders buttons)

```fsharp
type GlobalConfig = {
    SiteName: string
    Features: FeatureFlags
    IdentityProviders: string list   // e.g. ["google"; "microsoft"; "github"]
}
```

### Env.fs — runtime secrets per provider

```fsharp
type Env = {
    DB: D1Database
    EVENTS: DurableObjectNamespace
    BLOBS: R2Bucket
    ADMIN_KEY: string
    ENVIRONMENT: string
    OAUTH_SECRET: string              // HMAC signing key for state tokens
    GOOGLE_CLIENT_ID: string          // per-provider, optional
    GOOGLE_CLIENT_SECRET: string
    GITHUB_CLIENT_ID: string
    GITHUB_CLIENT_SECRET: string
    // MICROSOFT_*, FACEBOOK_* added as needed
}
```

Production secrets via `wrangler secret put`. Dev values in `wrangler.toml [vars]`.

Active providers = intersection of Config.fs list and env vars present at runtime.

### Client localStorage — provider preference

After first claim, store last-used provider in localStorage. Returning visitors see that provider surfaced first in the UI.

## Provider registry

Framework `OAuth.fs` — hardcoded configs:

| Provider | authorize_url | token_url | userinfo_url | scopes |
|---|---|---|---|---|
| Google | accounts.google.com/o/oauth2/v2/auth | oauth2.googleapis.com/token | googleapis.com/oauth2/v2/userinfo | openid profile email |
| GitHub | github.com/login/oauth/authorize | github.com/login/oauth/access_token | api.github.com/user | read:user user:email |
| Microsoft | login.microsoftonline.com/common/oauth2/v2.0/authorize | login.microsoftonline.com/common/oauth2/v2.0/token | graph.microsoft.com/v1.0/me | openid profile email |
| Facebook | facebook.com/v18.0/dialog/oauth | graph.facebook.com/v18.0/oauth/access_token | graph.facebook.com/me?fields=name,picture,email | public_profile email |

Each provider entry also includes a `parseUserinfo` function to normalize the response into `UserInfo`.

## Framework changes

### OAuth.fs (new)

- `type ProviderConfig` — authorize/token/userinfo URLs, scopes, parseUserinfo
- `type UserInfo` — normalized provider response: name, pictureUrl, email, providerUserId, provider
- `generateAuthUrl: ProviderConfig → clientId → guestId → returnTo → secret → redirectUrl`
- `verifyState: state → secret → Result<(guestId * returnTo), error>`
- `exchangeCode: ProviderConfig → code → redirectUri → clientId → clientSecret → Promise<accessToken>`
- `fetchUserinfo: ProviderConfig → accessToken → Promise<UserInfo>`

Pure protocol. No database access, no identity storage, no domain logic.

### Router.fs — auth routes in createWorker

Auth routes slot into the existing `createWorker` dispatch chain:

```
1. CORS preflight
2. Admin routes
3. Auth routes (/api/auth/*)        ← NEW (includes /api/auth/me, replacing /api/me)
4. WebSocket upgrade
5. Blob routes
6. App routes
7. SPA fallback
```

The auth block handles three routes:
- `/api/auth/me` — calls `OAuthConfig.ResolveIdentity`, returns result as JSON
- `/api/auth/:provider/login` — generates state, redirects to provider
- `/api/auth/:provider/callback` — verifies state, exchanges code, fetches userinfo, calls `OAuthConfig.OnOAuthComplete`, redirects to returned URL

If `WorkerConfig.OAuth` is `None`, the auth block is skipped entirely and `/api/auth/*` falls through to app routes (or 404). Hedge apps without OAuth just don't set it.

### Workers.fs — crypto helpers

SubtleCrypto bindings for HMAC-SHA256 (state token signing/verification). These are general-purpose CF Worker crypto helpers, not OAuth-specific.

### Hedge.fsproj — compilation order

Add OAuth.fs after Workers.fs, before Router.fs:

```
Interface.fs → Schema.fs → SchemaCodec.fs → Validate.fs → Codec.fs → Workers.fs → OAuth.fs → Router.fs
```

## Migration

### Schema migration (from current → new model)

```sql
-- 1. Create identities table
CREATE TABLE identities (
    id TEXT PRIMARY KEY,
    guest_id TEXT NOT NULL REFERENCES guests(id),
    provider TEXT NOT NULL DEFAULT 'anonymous',
    provider_user_id TEXT NOT NULL DEFAULT '',
    name TEXT NOT NULL,
    picture TEXT NOT NULL,
    email TEXT,
    activated_at INTEGER,
    created_at INTEGER NOT NULL
);

-- 2. Populate anonymous identities from existing guests
INSERT INTO identities (id, guest_id, provider, provider_user_id, name, picture, email, activated_at, created_at)
SELECT
    'ident-' || id,
    id,
    'anonymous',
    '',
    name,
    picture,
    NULL,
    created_at,
    created_at
FROM guests;

-- 3. Migrate item_comments from guest_id → identity_id
ALTER TABLE item_comments ADD COLUMN identity_id TEXT REFERENCES identities(id);
UPDATE item_comments SET identity_id = 'ident-' || guest_id;
-- (then drop guest_id column — or keep for backward compat during transition)

-- 4. Drop guest_sessions table
DROP TABLE guest_sessions;

-- 5. Drop name, picture from guests
-- (SQLite doesn't support DROP COLUMN before 3.35.0; may need table rebuild)
```

This migration runs via `npm run migrate`. The Gen pipeline generates the new schema; the migration bridges existing data.

## Implementation phases

### Phase 1: Foundation — schema + IoC

1. **Identity type** — add to app's Domain.fs
2. **Guest migration** — slim Guest (remove Name/Picture)
3. **ItemComment migration** — GuestId → IdentityId
4. **Remove GuestSession** — delete from Domain.fs
5. **`npm run gen`** → new schema, codecs, Db helpers
6. **Write migration SQL** — bridge existing data
7. **Inversion of Control** — add `ResolveIdentity` callback to WorkerConfig
8. **Rewrite Router.fs `/api/me`** → `/api/auth/me` using `ResolveIdentity` callback (eliminates raw SQL coupling)
9. **Implement `ResolveIdentity`** in app's worker-entry.js — queries identities table
10. **Remove raw SQL** — clean up guest_sessions references in Router.fs and Handlers.fs
11. **Move guest initialization** — out of submitComment, into app-provided callback on first visit
12. **Update client** — GuestSession.fs reads from `/api/auth/me`, identity-based response
13. **`./test.sh`** passes — existing behavior preserved with new schema

### Phase 2: OAuth framework (protocol only)

1. **Workers.fs** — SubtleCrypto bindings for HMAC-SHA256
2. **OAuth.fs** — provider registry, auth URL generation, code exchange, userinfo fetch, HMAC state
3. **Auth routes** — `/api/auth/:provider/login` and `/api/auth/:provider/callback` in Router.fs
4. **`OnOAuthComplete` callback** — add to OAuthConfig, wired in createWorker
5. **Test** — verify framework OAuth dance works end-to-end with a mock callback

### Phase 3: Claim flow (app policy)

1. **Implement `OnOAuthComplete`** in app — download picture to R2, find-or-create Identity, return redirect URL
2. **`/api/auth/activate`** — app route: merge or abandon logic, identity switching
3. **`/api/auth/revert`** — app route: switch to previous identity
4. **`/auth/claim` SPA route** — merge/abandon prompt UI
5. **Cross-device merge** — detect existing providerUserId, merge guests
6. **Identity switcher UI** — list all identities, switch between them
7. **Google** — register OAuth app, set env vars, test full flow

### Phase 4: Polish

1. **Picture editing** — after claim, allow upload via R2 blob infrastructure
2. **Re-authentication** — refresh name/picture/email from provider
3. **Provider preference** — localStorage cache of last-used provider
4. **Config UI** — admin can enable/disable providers
5. **Error handling** — OAuth failures, provider downtime, expired codes

## Files to modify

### Framework (`packages/hedge/src/Hedge/`)

| File | Change |
|---|---|
| `OAuth.fs` (new) | Provider registry, OAuth helpers, HMAC state, UserInfo type |
| `Router.fs` | Auth routes (`/api/auth/*`), `ResolveIdentity` + `OnOAuthComplete` callbacks, remove raw `guest_sessions` SQL |
| `Workers.fs` | SubtleCrypto bindings for HMAC-SHA256 |
| `Hedge.fsproj` | Add OAuth.fs to compilation order (after Workers.fs, before Router.fs) |

### Gen (`packages/hedge/src/Gen/`)

No changes needed — Identity is a regular app domain type, discovered normally.

### App — Models (`apps/microblog/src/Models/`)

| File | Change |
|---|---|
| `Domain.fs` | Add `Identity` type, slim `Guest` (remove Name/Picture), migrate `ItemComment` (GuestId → IdentityId), remove `GuestSession` |
| `Config.fs` | Add `IdentityProviders: string list` to GlobalConfig |
| `Api.fs` | Add activate/revert request/response types |

### App — Server (`apps/microblog/src/Server/`)

| File | Change |
|---|---|
| `Env.fs` | Add OAUTH_SECRET, per-provider client ID/secret |
| `Handlers.fs` | Update comment submission (use IdentityId), remove guest_sessions upsert, add activate/revert handlers, implement `OnOAuthComplete` |
| `worker-entry.js` | Pass OAuthConfig (with ResolveIdentity + OnOAuthComplete callbacks) to createWorker |

### App — Client (`apps/microblog/src/Client/`)

| File | Change |
|---|---|
| `GuestSession.fs` | Read identity from `/api/auth/me` response, identity switcher state |
| `Types.fs` | Identity list in Model, merge/abandon msg types |
| `Shared.fs` | "Sign in" button, provider list, identity switcher, claim prompt, `/auth/claim` route |

### App — Config

| File | Change |
|---|---|
| `wrangler.toml` | OAUTH_SECRET, provider client IDs (dev values) |
| `lib/guest-session.js` | Update to work with Identity-based `/api/auth/me` response |

### Migration

| File | Change |
|---|---|
| `migrations/NNNN_identity.sql` (new) | Schema migration SQL |

## Verification

### Automated

1. `npm run gen` — generates identities table, updated guests/item_comments schema, codecs
2. `npm run migrate` — applies migration to existing D1 database
3. `dotnet build` — server and client compile
4. `./test.sh` — golden model + scaffold pipeline pass

### Manual

1. Fresh site: anonymous guest created with anonymous Identity, can comment normally
2. Sign in with Google → consent → callback → Identity created, picture in R2
3. Merge prompt works: comments reassigned (or not) based on choice
4. Identity switcher: can revert to anonymous, can re-select provider identity
5. New device: sign in → same providerUserId found → cross-device merge works
6. Re-auth: sign in again with same provider → name/picture/email refreshed
7. Anonymous user can still browse and comment without ever claiming (zero regression)
8. Hedge app without OAuth configured: no auth routes, no Identity table needed, zero impact
