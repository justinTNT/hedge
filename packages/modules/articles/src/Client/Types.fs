module Articles.Client.Types

// The shared guest-session accessor is host-provided (packages/hedge/src/Client);
// alias it locally, the same way Client.RichText is aliased.
module GuestSession = Client.GuestSession

open Articles.Api
open Articles.ClientGen

type Model = {
    Route: string list
    Feed: GetFeed.Response option
    /// True while a subsequent (infinite-scroll) page is in flight.
    FeedLoadingMore: bool
    CurrentItem: GetPost.Response option
    IsLoading: bool
    /// CP-C: the typed API failure (host-injected client preserves Hedge.Http.ApiError end to end);
    /// the view renders it via Hedge.Http.renderError. Identity failures are wrapped as HttpFailure.
    Error: Hedge.Http.ApiError option
    GuestSession: GuestSession.GuestSessionData
    CollapsedComments: Set<string>
    ReplyingTo: {| PostId: string; ParentId: string option |} option
    /// C1b: the in-progress comment as model state. The editor reports edits via onChange
    /// into this field, so SubmitComment builds the request from the model — not a DOM read.
    /// Cleared on confirmed submit and on navigation away from the post (no cross-post bleed);
    /// preserved across a reply box close/reopen within the same post.
    CommentDraft: string
    /// CP-A: monotonic read generation (see blog Types.fs) — each read completion carries the gen
    /// it was issued under and applies only while it still equals this; late/reordered/obsolete
    /// reads (and read failures) are dropped. Route/target checks are secondary.
    LoadGen: int
    /// CP-A: comment-draft revision — a submit's success clears the draft only if unchanged.
    DraftRev: int
}

type Msg =
    | LoadFeed
    | GotFeed of gen: int * Result<GetFeed.Response, Hedge.Http.ApiError>
    | LoadMoreFeed
    | GotMoreFeed of gen: int * cursor: string option * Result<GetFeed.Response, Hedge.Http.ApiError>
    | LoadItem of string
    | GotItem of gen: int * Result<GetPost.Response, Hedge.Http.ApiError>
    | DismissError
    | SubmitComment
    | GotSubmitComment of rev: int * Result<SubmitComment.Response, Hedge.Http.ApiError>
    | ToggleCollapse of string
    | SetReplyTo of postId: string * parentId: string option
    | SetCommentDraft of string
    | CancelReply
    | ConnectEvents of string
    | DisconnectEvents
    | GotEvent of Articles.Ws.NewCommentEvent
    | EventError of string

/// CP-C: dependencies a host injects into the content update path — the per-instance host context
/// and the typed API client (built from the host's chosen transport). Replaces the module-owned
/// browser-transport shim (was Shared.Api): pages call `deps.Api.*` and keep the typed ApiError.
type Deps = {
    Ctx: Content.HostContext
    Api: Articles.ClientGen.Client
}
