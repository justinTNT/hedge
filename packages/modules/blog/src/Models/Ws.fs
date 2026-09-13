module Blog.Ws

/// WebSocket event payloads — the blog's live-comment layer (consumes the
/// framework Hedge.EventHub transport).

/// A new comment on an item; clients viewing that item append it live.
type NewCommentEvent = {
    Id: string
    ItemId: string
    IdentityId: string
    ParentId: string option
    Author: string
    Picture: string
    Content: string
    Timestamp: int
}
