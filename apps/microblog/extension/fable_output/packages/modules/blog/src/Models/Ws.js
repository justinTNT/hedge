import { Record } from "../../../../../fable_modules/fable-library-js.4.29.0/Types.js";
import { record_type, int32_type, option_type, string_type } from "../../../../../fable_modules/fable-library-js.4.29.0/Reflection.js";
import { ItemComment_$reflection, Item_$reflection } from "./Domain.js";
import { IdentityRef_$reflection, ForeignKey$1_$reflection } from "../../../../hedge/src/Hedge/Interface.js";

export class NewCommentEvent extends Record {
    constructor(Id, ItemId, IdentityId, ParentId, Author, Picture, Content, Timestamp) {
        super();
        this.Id = Id;
        this.ItemId = ItemId;
        this.IdentityId = IdentityId;
        this.ParentId = ParentId;
        this.Author = Author;
        this.Picture = Picture;
        this.Content = Content;
        this.Timestamp = (Timestamp | 0);
    }
}

export function NewCommentEvent_$reflection() {
    return record_type("Blog.Ws.NewCommentEvent", [], NewCommentEvent, () => [["Id", string_type], ["ItemId", ForeignKey$1_$reflection(Item_$reflection())], ["IdentityId", IdentityRef_$reflection()], ["ParentId", option_type(ForeignKey$1_$reflection(ItemComment_$reflection()))], ["Author", string_type], ["Picture", string_type], ["Content", string_type], ["Timestamp", int32_type]]);
}

