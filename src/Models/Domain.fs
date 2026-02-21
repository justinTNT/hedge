module Models.Domain

open Hedge.Interface

type Guest = {
    Id: PrimaryKey<string>
    Host: MultiTenant
    Name: string
    Picture: string
    SessionId: string
    CreatedAt: CreateTimestamp
    DeletedAt: SoftDelete option
}

type MicroblogItem = {
    Id: PrimaryKey<string>
    Host: MultiTenant
    Title: string
    Link: Link option
    Image: Link option
    Extract: RichContent option
    OwnerComment: RichContent
    CreatedAt: CreateTimestamp
    UpdatedAt: UpdateTimestamp option
    ViewCount: int
    DeletedAt: SoftDelete option
}

type ItemComment = {
    Id: PrimaryKey<string>
    Host: MultiTenant
    ItemId: ForeignKey<MicroblogItem>
    GuestId: ForeignKey<Guest>
    ParentId: string option
    AuthorName: string
    Text: RichContent
    Removed: bool
    CreatedAt: CreateTimestamp
    DeletedAt: SoftDelete option
}

type Tag = {
    Id: PrimaryKey<string>
    Host: MultiTenant
    Name: string
    CreatedAt: CreateTimestamp
    DeletedAt: SoftDelete option
}

type ItemTag = {
    ItemId: ForeignKey<MicroblogItem>
    TagId: ForeignKey<Tag>
    Host: MultiTenant
    DeletedAt: SoftDelete option
}

type GuestSession = {
    GuestId: string
    DisplayName: string
    CreatedAt: int
}
