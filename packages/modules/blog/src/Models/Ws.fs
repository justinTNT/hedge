module Blog.Ws

open Hedge.Interface

/// WebSocket event payloads — the blog's live-comment layer (consumes the
/// framework Hedge.EventHub transport).

/// A new comment on an item; clients viewing that item append it live.
/// Reference ids are typed (documents what they point at; serialize as bare
/// strings via the generic codec, so the wire is byte-identical). `Id` is the
/// comment's own id — left a plain string, like the API view types.
type NewCommentEvent = {
    Id: string
    ItemId: ForeignKey<Domain.Item>
    IdentityId: IdentityRef
    ParentId: ForeignKey<Domain.ItemComment> option
    Author: string
    Picture: string
    Content: string
    Timestamp: int
}
