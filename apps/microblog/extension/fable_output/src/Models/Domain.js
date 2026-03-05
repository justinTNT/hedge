import { Record } from "../../fable_modules/fable-library-js.4.29.0/Types.js";
import { bool_type, int32_type, record_type, option_type, string_type } from "../../fable_modules/fable-library-js.4.29.0/Reflection.js";
import { Unique$1_$reflection, ForeignKey$1_$reflection, UpdateTimestamp_$reflection, RichContent_$reflection, Link_$reflection, SoftDelete_$reflection, CreateTimestamp_$reflection, PrimaryKey$1_$reflection } from "../../packages/hedge/src/Hedge/Interface.js";

export class Guest extends Record {
    constructor(Id, Name, Picture, SessionId, CreatedAt, DeletedAt) {
        super();
        this.Id = Id;
        this.Name = Name;
        this.Picture = Picture;
        this.SessionId = SessionId;
        this.CreatedAt = CreatedAt;
        this.DeletedAt = DeletedAt;
    }
}

export function Guest_$reflection() {
    return record_type("Models.Domain.Guest", [], Guest, () => [["Id", PrimaryKey$1_$reflection(string_type)], ["Name", string_type], ["Picture", string_type], ["SessionId", string_type], ["CreatedAt", CreateTimestamp_$reflection()], ["DeletedAt", option_type(SoftDelete_$reflection())]]);
}

export class MicroblogItem extends Record {
    constructor(Id, Title, Link, Image, Extract, OwnerComment, Slug, CreatedAt, UpdatedAt, ViewCount, DeletedAt) {
        super();
        this.Id = Id;
        this.Title = Title;
        this.Link = Link;
        this.Image = Image;
        this.Extract = Extract;
        this.OwnerComment = OwnerComment;
        this.Slug = Slug;
        this.CreatedAt = CreatedAt;
        this.UpdatedAt = UpdatedAt;
        this.ViewCount = (ViewCount | 0);
        this.DeletedAt = DeletedAt;
    }
}

export function MicroblogItem_$reflection() {
    return record_type("Models.Domain.MicroblogItem", [], MicroblogItem, () => [["Id", PrimaryKey$1_$reflection(string_type)], ["Title", string_type], ["Link", option_type(Link_$reflection())], ["Image", option_type(Link_$reflection())], ["Extract", option_type(RichContent_$reflection())], ["OwnerComment", RichContent_$reflection()], ["Slug", option_type(string_type)], ["CreatedAt", CreateTimestamp_$reflection()], ["UpdatedAt", option_type(UpdateTimestamp_$reflection())], ["ViewCount", int32_type], ["DeletedAt", option_type(SoftDelete_$reflection())]]);
}

export class ItemComment extends Record {
    constructor(Id, ItemId, GuestId, ParentId, Author, Content, Removed, CreatedAt, DeletedAt) {
        super();
        this.Id = Id;
        this.ItemId = ItemId;
        this.GuestId = GuestId;
        this.ParentId = ParentId;
        this.Author = Author;
        this.Content = Content;
        this.Removed = Removed;
        this.CreatedAt = CreatedAt;
        this.DeletedAt = DeletedAt;
    }
}

export function ItemComment_$reflection() {
    return record_type("Models.Domain.ItemComment", [], ItemComment, () => [["Id", PrimaryKey$1_$reflection(string_type)], ["ItemId", ForeignKey$1_$reflection(MicroblogItem_$reflection())], ["GuestId", ForeignKey$1_$reflection(Guest_$reflection())], ["ParentId", option_type(string_type)], ["Author", string_type], ["Content", RichContent_$reflection()], ["Removed", bool_type], ["CreatedAt", CreateTimestamp_$reflection()], ["DeletedAt", option_type(SoftDelete_$reflection())]]);
}

export class Tag extends Record {
    constructor(Id, Name, CreatedAt, DeletedAt) {
        super();
        this.Id = Id;
        this.Name = Name;
        this.CreatedAt = CreatedAt;
        this.DeletedAt = DeletedAt;
    }
}

export function Tag_$reflection() {
    return record_type("Models.Domain.Tag", [], Tag, () => [["Id", PrimaryKey$1_$reflection(string_type)], ["Name", Unique$1_$reflection(string_type)], ["CreatedAt", CreateTimestamp_$reflection()], ["DeletedAt", option_type(SoftDelete_$reflection())]]);
}

export class ItemTag extends Record {
    constructor(ItemId, TagId, DeletedAt) {
        super();
        this.ItemId = ItemId;
        this.TagId = TagId;
        this.DeletedAt = DeletedAt;
    }
}

export function ItemTag_$reflection() {
    return record_type("Models.Domain.ItemTag", [], ItemTag, () => [["ItemId", ForeignKey$1_$reflection(MicroblogItem_$reflection())], ["TagId", ForeignKey$1_$reflection(Tag_$reflection())], ["DeletedAt", option_type(SoftDelete_$reflection())]]);
}

export class GuestSession extends Record {
    constructor(GuestId, DisplayName, CreatedAt) {
        super();
        this.GuestId = GuestId;
        this.DisplayName = DisplayName;
        this.CreatedAt = (CreatedAt | 0);
    }
}

export function GuestSession_$reflection() {
    return record_type("Models.Domain.GuestSession", [], GuestSession, () => [["GuestId", string_type], ["DisplayName", string_type], ["CreatedAt", int32_type]]);
}

