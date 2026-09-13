module Articles.Domain

// The articles module's data model — the reusable core (posts + comments).
// Extracted from the articles app; identity is NOT here (shared app-level layer),
// so Comment references it via the decoupled Hedge.Interface.IdentityRef, and its
// parent comment via a typed self-FK (wrap, don't unwrap).

open Hedge.Interface

// "Post" derives its table name (posts -> articles_posts when mounted); no
// [<Table>] override needed. Named neutrally so the reusable module carries no
// host/app name.
type Post = {
    Id: PrimaryKey<string>
    Title: string
    /// Short standfirst/teaser shown in the list; the body is the essay itself.
    Teaser: RichContent option
    /// The post body — authored rich text (this is the content, not a link).
    Body: RichContent
    /// Optional hero image (most essays put images inline in the body).
    Image: Link option
    /// The post's own date (drives display, sort and day-grouping). Editable and
    /// independent of CreatedAt/UpdatedAt. Unix seconds; the admin renders it as a
    /// date picker (EditableDate).
    ArticleDate: EditableDate
    /// URL slug. Unique when present (Gen emits the unique index); admin post
    /// creation surfaces a duplicate as a UNIQUE violation, backed by that index.
    Slug: Unique<string> option
    CreatedAt: CreateTimestamp
    UpdatedAt: UpdateTimestamp option
    ViewCount: int
    DeletedAt: SoftDelete option
}

[<Table "comments">]
type Comment = {
    Id: PrimaryKey<string>
    PostId: ForeignKey<Post>
    /// The shared app-level identity — decoupled from any concrete Identity type.
    IdentityId: IdentityRef
    /// The parent comment this reply targets (None for a top-level comment) — a
    /// typed self-reference, not a bare id string.
    ParentId: ForeignKey<Comment> option
    Author: string
    Content: RichContent
    Removed: bool
    CreatedAt: CreateTimestamp
    DeletedAt: SoftDelete option
}
