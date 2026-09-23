module Server.Env

open Hedge.Workers

type Env = {
    DB: D1Database
    EVENTS: DurableObjectNamespace
    BLOBS: R2Bucket
    ADMIN_KEY: string
    ENVIRONMENT: string
    OAUTH_SECRET: string
    /// Signed-guest-cookie signing secret (>= 32 random bytes), independent of OAUTH_SECRET. Bound
    /// as a Cloudflare secret; guest operations fail closed if it is absent (see Server.GuestConfig).
    GUEST_SECRET: string
    /// OPTIONAL legacy-upgrade bridge window (absolute epoch seconds), for a deployment whose legacy
    /// guest ids stayed PRIVATE. Both must be set to enable a bridge; absent/expired → hard cutover
    /// (the safe default for every deployment that ever exposed a guest id, e.g. in upload keys).
    /// See notes/GUEST-COOKIES-rollout-worksheet.md.
    GUEST_MIGRATION_START: string
    GUEST_BRIDGE_UNTIL: string
    /// OPTIONAL graceful-key-rotation keyring (Slice G): a JSON array of retiring signing keys
    /// { keyId, secret, retireAt } that still VERIFY (until retireAt) but never sign. Absent → the
    /// single GUEST_SECRET is the only key (no rotation). Bound as a Cloudflare secret. See
    /// Hedge.GuestSession.keyringFrom.
    GUEST_KEYRING: string
    /// OPTIONAL active signing key id (default "k1"). A key rotation sets a NEW id here (e.g. "k2")
    /// alongside a new GUEST_SECRET, and lists the OLD id+secret in GUEST_KEYRING so existing cookies
    /// keep verifying. Leaving the active id equal to a retiring key's id would shadow it.
    GUEST_KEY_ID: string
    GOOGLE_CLIENT_ID: string
    GOOGLE_CLIENT_SECRET: string
    GITHUB_CLIENT_ID: string
    GITHUB_CLIENT_SECRET: string
}
