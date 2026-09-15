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
    Image: Image option
    Extract: RichContent option
    OwnerComment: RichContent
    /// The article's own date (drives display, sort and day-grouping). Editable
    /// and independent of CreatedAt/UpdatedAt. Unix seconds; the admin renders it
    /// as a date picker (EditableDate).
    ArticleDate: EditableDate
    /// URL slug. Unique when present (Gen emits the unique index); the submit
    /// handler's UNIQUE-violation catch is backed by that index on every site.
    Slug: Unique<string> option
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

/// A captured snapshot of an item's source page — the microblog as its own web archive.
/// The captured bytes live in R2 (BlobKey); this row is the pointer + status. A side table
/// (FK -> Item), so the populated blog_items stays untouched (no in-place-FK rebuild).
[<Table "snapshots">]
type ItemSnapshot = {
    Id: PrimaryKey<string>
    ItemId: ForeignKey<Item>
    /// "html" (cleaned rendered DOM) for now; "screenshot" is a later variant.
    Kind: string
    /// R2 object key, e.g. "archive/<id>.html".
    BlobKey: string
    /// The source URL captured (denormalized from Item.Link at capture time).
    SourceUrl: string
    /// "pending" | "ok" | "failed".
    Status: string
    /// Failure detail for the admin, when Status = "failed".
    Error: string option
    /// Capture time (the row's creation). Named CreatedAt to match the framework's
    /// CreateTimestamp convention (column `created_at` + its auto-index).
    CreatedAt: CreateTimestamp
    DeletedAt: SoftDelete option
}
