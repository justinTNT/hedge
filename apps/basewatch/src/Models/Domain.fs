module Models.Domain

open Hedge.Interface

/// basewatch.org — a structured pages site (US military base in Darwin advocacy).
/// Content is a set of Pages navigated by a hierarchical Menu. Migrated from the
/// old iojs MongoDB; read-only public + hedge admin (bar the odd correction).

/// A content page. `Name` is the URL slug (e.g. "rationale", "amendments20").
[<Table "pages">]
type Page = {
    Id: PrimaryKey<string>
    Name: string
    Title: string
    /// Short standfirst (often empty in the source). Rich text (admin TipTap);
    /// stored as HTML like Body, though it isn't rendered on the public page.
    Teaser: RichContent
    /// The page body — archived HTML, edited via the admin's rich-text editor
    /// and rendered as-is on the page.
    Body: RichContent
    CreatedAt: CreateTimestamp
    UpdatedAt: UpdateTimestamp option
    DeletedAt: SoftDelete option
}

/// A node in the navigation tree. `Item` is this node's slug; `Link` is the Page
/// `Name` it opens; `ParentItem` is the parent node's `Item` ("" = top level);
/// `Ordinal` orders siblings.
[<Table "menu_items">]
type MenuItem = {
    Id: PrimaryKey<string>
    Item: string
    Title: string
    Link: string
    ParentItem: string
    Ordinal: int
    CreatedAt: CreateTimestamp
    DeletedAt: SoftDelete option
}
