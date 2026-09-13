module Blog.Domain

// The blog module's data model — the reusable core (posts + comments + tags).
// Extracted from the microblog app; identity is NOT here (shared app-level layer),
// so ItemComment references it via the decoupled Hedge.Interface.IdentityRef.

open Hedge.Interface

// "Item" derives its table name (items -> blog_items when mounted); no [<Table>]
// override needed. Named neutrally: the app's name ("microblog") shouldn't live in
// the reusable module.
type Item = {
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
    ItemId: ForeignKey<Item>
    /// The shared app-level identity — decoupled from any concrete Identity type.
    IdentityId: IdentityRef
    /// The parent comment this reply targets (None for a top-level comment) — a
    /// typed self-reference, not a bare id string (wrap, don't unwrap).
    ParentId: ForeignKey<ItemComment> option
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
    ItemId: ForeignKey<Item>
    TagId: ForeignKey<Tag>
    DeletedAt: SoftDelete option
}
