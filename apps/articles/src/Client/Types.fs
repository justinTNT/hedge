module Client.Types

open Models.Api
open Client.ClientGen

type IdentityListItem = {
    Id: string
    Provider: string
    Name: string
    Picture: string
    ActivatedAt: int option
}

type Model = {
    Route: string list
    Feed: GetArticles.Response option
    /// True while a subsequent (infinite-scroll) page is in flight.
    FeedLoadingMore: bool
    CurrentItem: GetArticle.Response option
    IsLoading: bool
    Error: string option
    GuestSession: GuestSession.GuestSessionData
    CollapsedComments: Set<string>
    ReplyingTo: {| ArticleId: string; ParentId: string option |} option
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
    | GotFeed of Result<GetArticles.Response, string>
    | LoadMoreFeed
    | GotMoreFeed of Result<GetArticles.Response, string>
    | LoadItem of string
    | GotItem of Result<GetArticle.Response, string>
    | DismissError
    | SubmitComment
    | GotSubmitComment of Result<SubmitComment.Response, string>
    | ToggleCollapse of string
    | SetReplyTo of articleId: string * parentId: string option
    | CancelReply
    | ConnectEvents of string
    | DisconnectEvents
    | GotEvent of Models.Ws.NewCommentEvent
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
