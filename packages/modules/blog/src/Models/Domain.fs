module Blog.Domain

// The blog module's data model — the reusable core (posts + comments + tags).
// Extracted from the microblog app; identity is NOT here (shared app-level layer),
// so ItemComment references it via the decoupled Hedge.Interface.IdentityRef.

open Hedge.Interface

[<Table "items">]
type MicroblogItem = {
    Id: PrimaryKey<string>
    Title: string
    Link: Link option
    Image: Link option
    Extract: RichContent option
    OwnerComment: RichContent
    /// The article's own date (drives display, sort and day-grouping). Editable
    /// and independent of CreatedAt/UpdatedAt. Unix seconds.
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
    /// The shared app-level identity — decoupled from any concrete Identity type.
    IdentityId: IdentityRef
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

[<Table "item_tags">]
type ItemTag = {
    Id: PrimaryKey<string>
    ItemId: ForeignKey<MicroblogItem>
    TagId: ForeignKey<Tag>
    DeletedAt: SoftDelete option
}
