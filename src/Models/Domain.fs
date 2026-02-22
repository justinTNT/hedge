module Models.Domain

open Hedge.Interface

type Guest = {
    Id: PrimaryKey<string>
    Name: string
    Picture: string
    SessionId: string
    CreatedAt: CreateTimestamp
    DeletedAt: SoftDelete option
}

// @table items
type MicroblogItem = {
    Id: PrimaryKey<string>
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

// @table comments
type ItemComment = {
    Id: PrimaryKey<string>
    ItemId: ForeignKey<MicroblogItem>
    GuestId: ForeignKey<Guest>
    ParentId: string option
    Author: string
    Content: RichContent
    Removed: bool
    CreatedAt: CreateTimestamp
    DeletedAt: SoftDelete option
}

type Tag = {
    Id: PrimaryKey<string>
    Name: string
    CreatedAt: CreateTimestamp
    DeletedAt: SoftDelete option
}

type ItemTag = {
    ItemId: ForeignKey<MicroblogItem>
    TagId: ForeignKey<Tag>
    DeletedAt: SoftDelete option
}

type GuestSession = {
    GuestId: string
    DisplayName: string
    CreatedAt: int
}
