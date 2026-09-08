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

[<Table "articles">]
type Article = {
    Id: PrimaryKey<string>
    Title: string
    /// Short standfirst/teaser shown in the list; the body is the essay itself.
    Teaser: RichContent option
    /// The article body — authored rich text (this is the content, not a link).
    Body: RichContent
    /// Optional hero image (most justat essays put images inline in the body).
    Image: Link option
    /// The article's own date (drives display, sort and day-grouping). Editable
    /// and independent of CreatedAt/UpdatedAt, so imported/backdated essays sort
    /// by when they were written, not when the row was added. Unix seconds.
    ArticleDate: int
    Slug: string option
    CreatedAt: CreateTimestamp
    UpdatedAt: UpdateTimestamp option
    ViewCount: int
    DeletedAt: SoftDelete option
}

[<Table "comments">]
type ArticleComment = {
    Id: PrimaryKey<string>
    ArticleId: ForeignKey<Article>
    IdentityId: ForeignKey<Identity>
    ParentId: string option
    Author: string
    Content: RichContent
    Removed: bool
    CreatedAt: CreateTimestamp
    DeletedAt: SoftDelete option
}
