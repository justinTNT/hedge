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
    GOOGLE_CLIENT_ID: string
    GOOGLE_CLIENT_SECRET: string
    GITHUB_CLIENT_ID: string
    GITHUB_CLIENT_SECRET: string
}
