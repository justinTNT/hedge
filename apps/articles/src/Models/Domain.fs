module Models.Domain

// The app's shared, app-level identity schema (unprefixed) — NOT a content module.
// Content (posts/comments) now lives in the composed `articles` module
// (packages/modules/articles); this app provides only the shared identity that the
// articles (and, on justat, blog) comments reference via Hedge.Interface.IdentityRef.

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
