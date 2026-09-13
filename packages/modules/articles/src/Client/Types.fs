module Articles.Client.Types

// The shared guest-session accessor is host-provided (packages/hedge/src/Client);
// alias it locally, the same way Client.RichText is aliased.
module GuestSession = Client.GuestSession

open Articles.Api
open Articles.ClientGen

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
    /// True while a subsequent (infinite-scroll) page is in flight.
    FeedLoadingMore: bool
    CurrentItem: GetPost.Response option
    IsLoading: bool
    Error: string option
    GuestSession: GuestSession.GuestSessionData
    CollapsedComments: Set<string>
    ReplyingTo: {| PostId: string; ParentId: string option |} option
    Identities: IdentityListItem list
    /// Providers the server has credentials for.
    AvailableProviders: string list
    ShowIdentitySwitcher: bool
    /// Identity id awaiting a merge/fresh decision in the switcher.
    SelectedIdentity: string option
    /// Set on OAuth return; consumed by UrlChanged to open the switcher pre-selected.
    PendingClaimFocus: string option
}

type Msg =
    | UrlChanged of string list
    | LoadFeed
    | GotFeed of Result<GetFeed.Response, string>
    | LoadMoreFeed
    | GotMoreFeed of Result<GetFeed.Response, string>
    | LoadItem of string
    | GotItem of Result<GetPost.Response, string>
    | DismissError
    | SubmitComment
    | GotSubmitComment of Result<SubmitComment.Response, string>
    | ToggleCollapse of string
    | SetReplyTo of postId: string * parentId: string option
    | CancelReply
    | ConnectEvents of string
    | DisconnectEvents
    | GotEvent of Articles.Ws.NewCommentEvent
    | EventError of string
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
