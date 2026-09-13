module Models.Domain

open Hedge.Interface

/// ntne.ws — the NT / Australian anti-nuclear news archive. Read-only: articles
/// were migrated from the old iojs MongoDB (3,514 rows) and are served, sectioned
/// and searchable, but not authored here (bar the odd admin correction).
[<Table "articles">]
type Article = {
    Id: PrimaryKey<string>
    /// Legacy numeric id from the pre-Mongo SQL site — kept for reference only;
    /// URLs use Id (the Mongo _id) so old /article/<id> links keep resolving.
    Aid: int
    Title: string
    /// The article body — the original archived HTML, rendered as-is.
    Body: string
    /// Exactly one of: waste | uranium | ranger | rumjungle | intervention.
    /// Drives the section pages and the front-page columns.
    Section: string
    /// Publication date (Unix seconds) — drives sort and the year-grouped index.
    /// Editable in the admin as a date picker (EditableDate).
    ArticleDate: EditableDate
    /// Original citation fields (frequently empty in the archive).
    Attrib: string
    Source: string
    CreatedAt: CreateTimestamp
    UpdatedAt: UpdateTimestamp option
    ViewCount: int
    DeletedAt: SoftDelete option
}
