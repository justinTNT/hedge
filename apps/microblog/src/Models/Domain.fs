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
    Slug: string option
    CreatedAt: CreateTimestamp
    UpdatedAt: UpdateTimestamp option
    ViewCount: int
    // Promotion idempotency key: the alert EntryKey a promoted item came from.
    // Nullable-unique — hand-authored items are NULL (multiple NULLs allowed);
    // a second promotion of the same draft violates the index and rolls back.
    OriginEntryKey: Unique<string> option
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

// A registered alert feed (e.g. a Google Alerts "Deliver to RSS" URL).
// Retire a topic with Enabled = false, not Delete (pending rows FK-reference it).
type AlertSource = {
    Id: PrimaryKey<string>
    Topic: string                 // tag applied to promoted items
    FeedUrl: Unique<string>
    Enabled: bool
    CreatedAt: CreateTimestamp
}

// A polled feed entry awaiting curation. Promotion is DERIVED (a draft is
// promoted iff its EntryKey appears in items.origin_entry_key) — no promoted_at
// flag for the admin PUT to clobber. Rejected rows persist as tombstones so the
// EntryKey unique index + INSERT OR IGNORE block re-import.
type PendingPost = {
    Id: PrimaryKey<string>
    SourceId: ForeignKey<AlertSource>
    EntryKey: Unique<string>      // Atom <id> — stable; the <link> is a redirect wrapper
    Title: string
    Link: Link
    Snippet: string
    PublishedAt: int
    Approved: bool
    Rejected: bool
    OwnerComment: RichContent     // '' until the admin writes one; NOT NULL like items
    CreatedAt: CreateTimestamp
}