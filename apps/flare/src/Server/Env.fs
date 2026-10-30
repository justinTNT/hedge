module Server.Env

open Hedge.Workers

type Env = {
    DB: D1Database
    EVENTS: DurableObjectNamespace
    BLOBS: R2Bucket
    ADMIN_KEY: string
    ENVIRONMENT: string
    /// The flarecam2 Worker whose /api/log is projected into the calendar (Server.Flare): a service binding.
    FLARECAM: ServiceBinding
    /// Public base URL of the camera pictures; a period's first frame is shown from here.
    FLARECAM_PICTURES: string
    OAUTH_SECRET: string
    /// Signed-guest-cookie signing secret (>= 32 random bytes), independent of OAUTH_SECRET. Bound as a
    /// Cloudflare secret; guest operations fail closed if it is absent (see Server.GuestConfig).
    GUEST_SECRET: string
    /// OPTIONAL legacy-upgrade bridge window — a fresh deployment never had legacy guests, so leave unset
    /// (hard cutover). See notes/GUEST-COOKIES-rollout-worksheet.md.
    GUEST_MIGRATION_START: string
    GUEST_BRIDGE_UNTIL: string
    /// OPTIONAL graceful-key-rotation keyring + active key id. See Hedge.GuestSession.keyringFrom.
    GUEST_KEYRING: string
    GUEST_KEY_ID: string
    GITHUB_CLIENT_ID: string
    GITHUB_CLIENT_SECRET: string
    // OIDC providers (empty ⇒ hidden from /api/auth/providers rather than erroring).
    GOOGLE_CLIENT_ID: string
    GOOGLE_CLIENT_SECRET: string
    MICROSOFT_CLIENT_ID: string
    MICROSOFT_CLIENT_SECRET: string
    LINKEDIN_CLIENT_ID: string
    LINKEDIN_CLIENT_SECRET: string
    // Email magic-link: EMAIL_PROVIDER (resend|mailgun|ses|stub); empty ⇒ off.
    EMAIL_PROVIDER: string
    EMAIL_FROM: string
    RESEND_API_KEY: string
    MAILGUN_API_KEY: string
    MAILGUN_DOMAIN: string
    AWS_SES_ACCESS_KEY_ID: string
    AWS_SES_SECRET_ACCESS_KEY: string
    AWS_SES_REGION: string
}
