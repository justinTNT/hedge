module Calendar.Client.Types

// The shared guest-session accessor is host-provided (packages/hedge/src/Client); alias it locally,
// the same way Client.RichText is aliased.
module GuestSession = Client.GuestSession

open Calendar.Api
open Calendar.Vocab
open Calendar.Client.Dates

/// The own-entry editor's draft. Body is the TipTap JSON the rich-text editor reports.
type EditorState = {
    Id: string option
    Day: Day
    Summary: string
    Body: string
    Repeat: string
    /// "yyyy-mm-dd" or "" (open-ended).
    RepeatUntil: string
    /// The public facet to ask an admin for (community | art), or "" to keep it personal.
    Requested: string
    /// Editing an entry that is currently public: saving takes it back to personal until re-approved.
    Published: bool
    Saving: bool
    Error: string option
}

type Model = {
    /// This site's facet vocabulary (host-supplied at emptyHosted): the chips, and what the editor may ask for.
    Facets: Facets
    /// The module-local route the host last entered.
    Route: string list
    /// The viewer's calendar date, read at the edge (emptyHosted / enterHosted) so the view is a
    /// function of the model alone.
    Today: Day
    Year: int
    Month: int
    /// The day whose modal is open (route-driven: yyyy/mm/dd).
    OpenDay: Day option
    /// The windowed stream for the viewed grid, as the server returned it.
    Entries: GetEntries.EntryItem list
    /// Server-confirmed: the last read carried a signed-in identity.
    SignedIn: bool
    Loading: bool
    /// Whether a window has ever been requested (the first route always loads).
    Loaded: bool
    /// Request identity: a read's result is applied only if it is the latest issued.
    LoadGen: int
    Error: string option
    /// Facets toggled off.
    Hidden: Set<string>
    Editor: EditorState option
    /// Snapshot of the host's authoritative session (identity is host-owned).
    GuestSession: GuestSession.GuestSessionData
}

type Msg =
    | Load
    | GotEntries of generation: int * Result<GetEntries.Response, Hedge.Http.ApiError>
    | ToggleCategory of string
    | CloseDay
    | StartNew
    | StartEdit of GetEntries.EntryItem
    | EditorChanged of EditorState
    | BodyChanged of string
    | SubmitEditor
    | GotSaved of Result<SaveEntry.Response, Hedge.Http.ApiError>
    | CancelEditor
    | DeleteEntry of string
    | GotDeleted of string * Result<DeleteEntry.Response, Hedge.Http.ApiError>
    | DismissError

/// Dependencies a host injects into the content update path — the per-instance host context and
/// the typed API client (built from the host's chosen transport).
type Deps = {
    Ctx: Content.HostContext
    Api: Calendar.ClientGen.Client
}

/// The one rich-text editor element (the modal hosts at most one editor at a time).
let [<Literal>] editorElementId = "calendar-entry-editor"

let locale = Hedge.Tenant.config.Locale

let monthSegments (year: int) (month: int) = [ string year; pad2 month ]
let daySegments (d: Day) = [ string (yearOf d); pad2 (monthOf d); pad2 (dayOfMonthOf d) ]
