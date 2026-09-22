module Models.Domain

// The app's shared, app-level identity schema (unprefixed) — NOT a content module.
// Content (items/comments/tags) now lives in the composed `blog` module
// (packages/modules/blog); this app provides only the shared identity that the
// blog's comments reference via Hedge.Interface.IdentityRef.

open Hedge.Interface

type Guest = {
    Id: PrimaryKey<string>
    SessionId: string
    CreatedAt: CreateTimestamp
    DeletedAt: SoftDelete option
}

type Identity = {
    Id: PrimaryKey<string>
    GuestId: ForeignKey<Guest>
    Provider: string
    ProviderUserId: string
    Name: string
    Picture: string
    Email: string option
    ActivatedAt: int option
    CreatedAt: CreateTimestamp
}

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
    GrantedAt: CreateTimestamp
}
