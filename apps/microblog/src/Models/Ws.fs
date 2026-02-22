module Models.Ws

/// WebSocket event payloads.
/// These define the shape of data pushed to clients via WebSocket.

/// Sent when a new comment is created on an item.
/// Clients viewing the same item receive this to append the comment live.
type NewCommentEvent = {
    Id: string
    ItemId: string
    GuestId: string
    ParentId: string option
    Author: string
    Content: string
    Timestamp: int
}

/// Sent when an admin moderates a comment (sets removed = true/false).
/// Clients viewing the same item receive this to update comment visibility.
type CommentModeratedEvent = {
    CommentId: string
    Removed: bool
}

/// Sent when a comment is hard-deleted.
type CommentRemovedEvent = {
    CommentId: string
    PostId: string
    Timestamp: int
}
