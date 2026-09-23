module Models.Domain

// The shared, app-level identity schema (unprefixed) — NOT a content module, and NOT
// app-specific: this assembly (`IdentityModels`) is the single source of the `guests`
// and `identities` tables that every identity host (microblog + articles) reflects via
// its `{ "identity": true, "assembly": "IdentityModels" }` gen slice. The module name
// stays `Models.Domain` so hosts' generated codecs (`Models.Domain.Guest/.Identity`)
// resolve unchanged after the extraction. Content comments reference `Identity` via
// `Hedge.Interface.IdentityRef` (resolved by the short name `Identity` -> `identities`).

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
