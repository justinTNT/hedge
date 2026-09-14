module Articles.Ws

open Hedge.Interface

/// WebSocket event payloads — the articles module's live-comment layer (consumes
/// the framework Hedge.EventHub transport).

/// A new comment on a post; clients viewing that post append it live.
/// Reference ids are typed (documents what they point at; serialize as bare
/// strings via the generic codec, so the wire is byte-identical). `Id` is the
/// comment's own id — left a plain string, like the API view types.
type NewCommentEvent = {
    Id: string
    PostId: ForeignKey<Domain.Post>
    IdentityId: IdentityRef
    ParentId: ForeignKey<Domain.Comment> option
    Author: string
    Picture: string
    Content: string
    Timestamp: int
}
