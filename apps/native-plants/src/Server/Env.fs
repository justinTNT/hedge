module Server.Env
open Hedge.Workers
type Env = {
    DB: D1Database
    BLOBS: R2Bucket
    ADMIN_KEY: string
    ENVIRONMENT: string
    CONTRIBUTIONS_REQUIRE_LOGIN: string
    GUEST_SECRET: string
    GUEST_KEY_ID: string
    GUEST_KEYRING: string
    OAUTH_SECRET: string
    GOOGLE_CLIENT_ID: string
    GOOGLE_CLIENT_SECRET: string
    GITHUB_CLIENT_ID: string
    GITHUB_CLIENT_SECRET: string
}
