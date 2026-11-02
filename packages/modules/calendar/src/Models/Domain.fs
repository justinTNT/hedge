module Calendar.Domain

open Hedge.Interface

/// One dated entry in the stream. The calendar is a projection over these — there are no
/// calendars as such — and `Category` is a filter facet (see Calendar.Vocab.Category).
[<Table "entries">]
// The month-window reads (Sql.publicEntries / visibleEntries) search live rows by date; without
// this they scan the whole table and sort.
[<Index("DeletedAt", "Date")>]
type Entry = {
    Id: PrimaryKey<string>
    /// The day this entry (or its first occurrence) falls on: epoch seconds at UTC midnight.
    Date: EditableDate
    /// The EFFECTIVE facet: sitewide | community | art | personal (Calendar.Vocab.Category). Personal
    /// means private to its owner; anything else is public. An owner-authored entry becomes public only
    /// when an admin sets this from personal to the facet the owner requested.
    Category: string
    /// The public facet the owner asked for (community | art); NULL when none. While Category is still
    /// personal, rows with a request form the admin's approval queue.
    Requested: string option
    /// The one-liner shown in the day's grid cell.
    Summary: string
    /// The full rich text shown in the day modal.
    Body: RichContent
    /// none | daily | weekly | fortnightly | monthly | yearly (Calendar.Vocab.Repeat).
    Repeat: string
    /// Last day a recurrence applies (inclusive); None = open-ended. Ignored when Repeat = none.
    RepeatUntil: EditableDate option
    /// Personal entries belong to the signed-in identity that wrote them and are visible only to
    /// it; NULL for the public categories (authored in the admin).
    IdentityId: IdentityRef option
    CreatedAt: CreateTimestamp
    UpdatedAt: UpdateTimestamp option
    DeletedAt: SoftDelete option
}
