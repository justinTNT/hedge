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

type IdentityListItem = {
    Id: string
    Provider: string
    Name: string
    Picture: string
    ActivatedAt: int option
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
    Error: string option
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
    Identities: IdentityListItem list
    /// Providers the server has credentials for — the connections pane offers
    /// only these, so an unconfigured provider is never a dead button
    AvailableProviders: string list
    ShowIdentitySwitcher: bool
    /// Identity id awaiting a merge/fresh decision in the switcher
    SelectedIdentity: string option
    /// Set on OAuth return; consumed by UrlChanged to open the switcher pre-selected
    PendingClaimFocus: string option
}

type Msg =
    | UrlChanged of string list
    | LoadFeed
    | GotFeed of gen: int * Result<GetFeed.Response, string>
    | LoadMoreFeed
    | GotMoreFeed of gen: int * cursor: string option * Result<GetFeed.Response, string>
    | LoadItem of string
    | GotItem of gen: int * Result<GetItem.Response, string>
    | DismissError
    | ConnectEvents of string
    | DisconnectEvents
    | GotEvent of Blog.Ws.NewCommentEvent
    | EventError of string
    | LoadTagItems of string
    | GotTagItems of gen: int * Result<GetItemsByTag.Response, string>
    | LoadMoreTagItems
    | GotMoreTagItems of gen: int * cursor: string option * Result<GetItemsByTag.Response, string>
    | SubmitComment
    | GotSubmitComment of rev: int * Result<SubmitComment.Response, string>
    | SetNewItemTitle of string
    | SetNewItemLink of string
    | SetNewItemTags of string
    | SubmitItem
    | GotSubmitItem of Result<SubmitItem.Response, string>
    | ToggleCollapse of string
    | SetReplyTo of itemId: string * parentId: string option
    | SetCommentDraft of string
    | CancelReply
    | GotSessionSync of GuestSession.GuestSessionData
    | RevertIdentity of identityId: string * merge: bool
    | GotRevertIdentity of Result<unit, string>
    | LoadIdentities
    | GotIdentities of IdentityListItem list
    | GotProviders of string list
    | ToggleIdentitySwitcher
    | DisconnectIdentity of identityId: string
    | GotDisconnect of Result<unit, string>
    | SelectIdentity of identityId: string

let emptyItemForm = { Title = ""; Link = ""; Tags = "" }
