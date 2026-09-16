module Blog.Client.Types

// The shared guest-session accessor is host-provided (packages/hedge/src/Client);
// alias it locally, the same way Client.RichText is aliased.
module GuestSession = Client.GuestSession

open Elmish
open Hedge.Interface
open Blog.Api
open Blog.ClientGen

type ItemForm = {
    Title: string
    Link: string
    Tags: string
}

type Model = {
    Route: string list
    Feed: GetFeed.Response option
    /// True while a subsequent (infinite-scroll) page is in flight, so the
    /// scroll sentinel can't fire overlapping requests.
    FeedLoadingMore: bool
    CurrentItem: GetItem.Response option
    TagItems: GetItemsByTag.Response option
    TagLoadingMore: bool
    IsLoading: bool
    /// CP-C: the typed API failure (host-injected client preserves Hedge.Http.ApiError end to end);
    /// the view renders it via Hedge.Http.renderError. Identity failures are wrapped as HttpFailure.
    Error: Hedge.Http.ApiError option
    GuestSession: GuestSession.GuestSessionData
    ItemForm: ItemForm
    CollapsedComments: Set<string>
    ReplyingTo: {| ItemId: string; ParentId: string option |} option
    /// C1b: the in-progress comment as model state. The editor reports edits via onChange
    /// into this field, so SubmitComment builds the request from the model — not a DOM read.
    /// Cleared on confirmed submit and on navigation away from the item (no cross-item bleed);
    /// preserved across a reply box close/reopen within the same item.
    CommentDraft: string
    /// CP-A: monotonic read generation — bumped on each new read, on navigation/entry, and on
    /// cache invalidation. Each read completion carries the gen it was issued under and applies
    /// only while it still equals this, so late / reordered / obsolete reads (and read failures)
    /// are dropped. The route/target checks remain as cheap secondary defence.
    LoadGen: int
    /// CP-A: comment-draft revision — bumped on each edit and on entry. A submit captures it; the
    /// success clears the draft only if it's unchanged, so a late success can't erase a newer draft.
    DraftRev: int
}

type Msg =
    | LoadFeed
    | GotFeed of gen: int * Result<GetFeed.Response, Hedge.Http.ApiError>
    | LoadMoreFeed
    | GotMoreFeed of gen: int * cursor: string option * Result<GetFeed.Response, Hedge.Http.ApiError>
    | LoadItem of string
    | GotItem of gen: int * Result<GetItem.Response, Hedge.Http.ApiError>
    | DismissError
    | ConnectEvents of string
    | DisconnectEvents
    | GotEvent of Blog.Ws.NewCommentEvent
    | EventError of string
    | LoadTagItems of string
    | GotTagItems of gen: int * Result<GetItemsByTag.Response, Hedge.Http.ApiError>
    | LoadMoreTagItems
    | GotMoreTagItems of gen: int * cursor: string option * Result<GetItemsByTag.Response, Hedge.Http.ApiError>
    | SubmitComment
    | GotSubmitComment of rev: int * Result<SubmitComment.Response, Hedge.Http.ApiError>
    | SetNewItemTitle of string
    | SetNewItemLink of string
    | SetNewItemTags of string
    | SubmitItem
    | GotSubmitItem of Result<SubmitItem.Response, Hedge.Http.ApiError>
    | ToggleCollapse of string
    | SetReplyTo of itemId: string * parentId: string option
    | SetCommentDraft of string
    | CancelReply

/// CP-C: dependencies a host injects into the content update path — the per-instance host context
/// and the typed API client (built from the host's chosen transport). Replaces the module-owned
/// browser-transport shim (was Shared.Api): pages call `deps.Api.*` and keep the typed ApiError.
type Deps = {
    Ctx: Content.HostContext
    Api: Blog.ClientGen.Client
}

let emptyItemForm = { Title = ""; Link = ""; Tags = "" }
