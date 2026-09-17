module Alerts.Domain

// The alerts module's data model — registered feeds, a curation queue, and module-owned
// promotion dedup state. Ported from the pre-module `alerts` branch
// (apps/microblog/src/Models/Domain.fs), adapted for the consolidated module architecture:
// promotion state lives in this module's own `Promotion` table (was items.origin_entry_key on the
// monolith), so promoting into a host's feed never touches that host's schema. No identity here;
// no content client — alerts is admin + cron only.

open Hedge.Interface

/// A registered alert feed (e.g. a Google Alerts "Deliver to RSS" URL).
/// Retire a topic with Enabled = false, not Delete (pending rows FK-reference it).
type AlertSource = {
    Id: PrimaryKey<string>
    Topic: string                 // tag applied to promoted items
    FeedUrl: Unique<string>
    Enabled: bool
    CreatedAt: CreateTimestamp
}

/// A polled feed entry awaiting curation. Rejected rows persist as tombstones so the EntryKey
/// unique index + INSERT OR IGNORE block re-import. Promotion is tracked in `Promotion`, not a
/// flag here, so the admin PUT has nothing to clobber.
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

/// Promotion dedup state — module-owned (replaces the monolith's items.origin_entry_key, so the
/// host's feed schema is never touched). One row per promoted entry; the unique EntryKey index
/// makes a concurrent double-promote roll back at COMMIT. ItemId is the id created in the host
/// feed (a bare string — a cross-module ForeignKey<Blog.Item> stays deliberately rejected;
/// provenance is a plain key, not a compile-time dependency on blog).
type Promotion = {
    Id: PrimaryKey<string>
    EntryKey: Unique<string>
    ItemId: string
    CreatedAt: CreateTimestamp
}
