module Grants.Domain

// The access-control grant table, in a SEPARATE assembly (`GrantModels`, namespace
// `Grants`) so it stays OPT-IN per app: a host gains the `grants` table only by adding
// a second identity slice `{ "identity": true, "assembly": "GrantModels",
// "namespace": "Grants" }` to its gen manifest (microblog/idealist do; articles omits
// it, so no `grants` table there). Kept out of `Models.Domain` so the two shared
// identity assemblies never define the same module. The generated `grants` table is
// unchanged (table name derives from the type's short name `Grant`, not its namespace).

open Hedge.Interface

/// A role grant: the person identified by the OAuth pair (provider, provider_user_id) holds `role`
/// (e.g. "curator"). Keyed on the pair — stable across identity merges (never guest_id/identities.id).
/// Revoke via Enabled=false (the row is kept, not deleted, so the unique index never collides on a
/// re-grant). Owner-managed through the generic admin.
[<UniqueTogether("Provider", "ProviderUserId", "Role")>]
type Grant = {
    Id: PrimaryKey<string>
    Provider: string
    ProviderUserId: string
    Role: string
    Enabled: bool
    GrantedBy: string option
    CreatedAt: CreateTimestamp   // must be named CreatedAt -> created_at (framework CreateTimestamp convention)
}
