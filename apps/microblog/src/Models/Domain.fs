module Models.Domain

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

[<Table "items">]
type MicroblogItem = {
    Id: PrimaryKey<string>
    Title: string
    Link: Link option
    Image: Link option
    Extract: RichContent option
    OwnerComment: RichContent
    /// The article's own date (drives display, sort and day-grouping). Editable
    /// and independent of CreatedAt/UpdatedAt, so imported/backdated articles sort
    /// by when they were written, not when the row was added. Unix seconds.
    ArticleDate: int
    Slug: string option
    CreatedAt: CreateTimestamp
    UpdatedAt: UpdateTimestamp option
    ViewCount: int
    DeletedAt: SoftDelete option
}

[<Table "comments">]
type ItemComment = {
    Id: PrimaryKey<string>
    ItemId: ForeignKey<MicroblogItem>
    IdentityId: ForeignKey<Identity>
    ParentId: string option
    Author: string
    Content: RichContent
    Removed: bool
    CreatedAt: CreateTimestamp
    DeletedAt: SoftDelete option
}

type Tag = {
    Id: PrimaryKey<string>
    Name: Unique<string>
    CreatedAt: CreateTimestamp
    DeletedAt: SoftDelete option
}

type ItemTag = {
    ItemId: ForeignKey<MicroblogItem>
    TagId: ForeignKey<Tag>
    DeletedAt: SoftDelete option
}