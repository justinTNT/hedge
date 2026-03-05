import { Record } from "../../fable_modules/fable-library-js.4.29.0/Types.js";
import { bool_type, record_type, int32_type, option_type, string_type } from "../../fable_modules/fable-library-js.4.29.0/Reflection.js";

export class NewCommentEvent extends Record {
    constructor(Id, ItemId, GuestId, ParentId, Author, Content, Timestamp) {
        super();
        this.Id = Id;
        this.ItemId = ItemId;
        this.GuestId = GuestId;
        this.ParentId = ParentId;
        this.Author = Author;
        this.Content = Content;
        this.Timestamp = (Timestamp | 0);
    }
}

export function NewCommentEvent_$reflection() {
    return record_type("Models.Ws.NewCommentEvent", [], NewCommentEvent, () => [["Id", string_type], ["ItemId", string_type], ["GuestId", string_type], ["ParentId", option_type(string_type)], ["Author", string_type], ["Content", string_type], ["Timestamp", int32_type]]);
}

export class CommentModeratedEvent extends Record {
    constructor(CommentId, Removed) {
        super();
        this.CommentId = CommentId;
        this.Removed = Removed;
    }
}

export function CommentModeratedEvent_$reflection() {
    return record_type("Models.Ws.CommentModeratedEvent", [], CommentModeratedEvent, () => [["CommentId", string_type], ["Removed", bool_type]]);
}

export class CommentRemovedEvent extends Record {
    constructor(CommentId, PostId, Timestamp) {
        super();
        this.CommentId = CommentId;
        this.PostId = PostId;
        this.Timestamp = (Timestamp | 0);
    }
}

export function CommentRemovedEvent_$reflection() {
    return record_type("Models.Ws.CommentRemovedEvent", [], CommentRemovedEvent, () => [["CommentId", string_type], ["PostId", string_type], ["Timestamp", int32_type]]);
}

