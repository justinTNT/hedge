module Models.Ws

/// WebSocket event payloads.
/// These define the shape of data pushed to clients via WebSocket.

/// Sent when a new comment is created on an article.
/// Clients viewing the same article receive this to append the comment live.
type NewCommentEvent = {
    Id: string
    ArticleId: string
    IdentityId: string
    ParentId: string option
    Author: string
    Picture: string
    Content: string
    Timestamp: int
}
