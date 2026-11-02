module Calendar.Api

open Hedge.Interface

/// The stream, windowed: every entry that can occur within [From, To] (epoch seconds, inclusive) —
/// one-off entries dated inside it plus recurring entries that started on or before its end and
/// have not ended before its start. The client projects occurrences from these. Personal entries
/// are included only for their owner, so the handler reads the request (requestContext).
module GetEntries =
    type EntryItem = {
        Id: string
        Date: int
        Category: string
        Summary: string
        Body: string
        Repeat: string
        RepeatUntil: int option
        /// The facet the owner asked for, if any (shown as "awaiting approval" while still personal).
        Requested: string option
        /// Owned by the requesting identity (editable/deletable), whether or not it has been approved.
        Mine: bool
    }
    type Query = { From: int; To: int }
    /// A stream fed by an instrument (a camera, a sensor) that can be off or not yet fetched: a day with no
    /// entry is then either quiet or unwatched, and the grid should say which. The host's Derived reports it;
    /// an authored calendar has no such notion (None in the response).
    type Observed = {
        /// The window's days with observations, as epoch seconds at UTC midnight (the module's day key).
        Days: int list
        /// The newest observed day in the whole stream — where a log ends, and so where it opens.
        Latest: int option
    }
    type Response = {
        Entries: EntryItem list
        /// Whether the request carried a signed-in identity — gates the personal facet + editor.
        SignedIn: bool
        Observed: Observed option
    }
    let requestContext = true
    let endpoint : GetQuery<Query, Response> = GetQuery "/api/entries"

/// Create (Id = None) or update (Id = Some own id) one of the signed-in identity's entries. A save always
/// lands as PERSONAL: `Requested` (community | art) asks an admin to publish it under that facet, and an
/// edit of an already-published entry takes it back to personal until it is approved again.
module SaveEntry =
    type Request = {
        Id: string option
        Date: int
        Summary: string
        Body: string
        Repeat: string
        RepeatUntil: int option
        Requested: string option
    }
    type Response = { Entry: GetEntries.EntryItem }
    let endpoint : Post<Request, Response> = Post "/api/entry"

/// Soft-delete one of the signed-in identity's own personal entries.
module DeleteEntry =
    type Request = { Id: string }
    type Response = { Ok: bool }
    let endpoint : Post<Request, Response> = Post "/api/entry/delete"
