module Articles.Ws

/// WebSocket event payloads — the articles module's live-comment layer (consumes
/// the framework Hedge.EventHub transport).

/// A new comment on a post; clients viewing that post append it live.
type NewCommentEvent = {
    Id: string
    PostId: string
    IdentityId: string
    ParentId: string option
    Author: string
    Picture: string
    Content: string
    Timestamp: int
}
