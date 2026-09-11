import { Record } from "../../../../../fable_modules/fable-library-js.4.29.0/Types.js";
import { bool_type, record_type, int32_type, option_type, string_type } from "../../../../../fable_modules/fable-library-js.4.29.0/Reflection.js";
import { Unique$1_$reflection, IdentityRef_$reflection, ForeignKey$1_$reflection, SoftDelete_$reflection, UpdateTimestamp_$reflection, CreateTimestamp_$reflection, RichContent_$reflection, Link_$reflection, PrimaryKey$1_$reflection } from "../../../../hedge/src/Hedge/Interface.js";

export class MicroblogItem extends Record {
    constructor(Id, Title, Link, Image, Extract, OwnerComment, ArticleDate, Slug, CreatedAt, UpdatedAt, ViewCount, DeletedAt) {
        super();
        this.Id = Id;
        this.Title = Title;
        this.Link = Link;
        this.Image = Image;
        this.Extract = Extract;
        this.OwnerComment = OwnerComment;
        this.ArticleDate = (ArticleDate | 0);
        this.Slug = Slug;
        this.CreatedAt = CreatedAt;
        this.UpdatedAt = UpdatedAt;
        this.ViewCount = (ViewCount | 0);
        this.DeletedAt = DeletedAt;
    }
}

export function MicroblogItem_$reflection() {
    return record_type("Blog.Domain.MicroblogItem", [], MicroblogItem, () => [["Id", PrimaryKey$1_$reflection(string_type)], ["Title", string_type], ["Link", option_type(Link_$reflection())], ["Image", option_type(Link_$reflection())], ["Extract", option_type(RichContent_$reflection())], ["OwnerComment", RichContent_$reflection()], ["ArticleDate", int32_type], ["Slug", option_type(string_type)], ["CreatedAt", CreateTimestamp_$reflection()], ["UpdatedAt", option_type(UpdateTimestamp_$reflection())], ["ViewCount", int32_type], ["DeletedAt", option_type(SoftDelete_$reflection())]]);
}

export class ItemComment extends Record {
    constructor(Id, ItemId, IdentityId, ParentId, Author, Content, Removed, CreatedAt, DeletedAt) {
        super();
        this.Id = Id;
        this.ItemId = ItemId;
        this.IdentityId = IdentityId;
        this.ParentId = ParentId;
        this.Author = Author;
        this.Content = Content;
        this.Removed = Removed;
        this.CreatedAt = CreatedAt;
        this.DeletedAt = DeletedAt;
    }
}

export function ItemComment_$reflection() {
    return record_type("Blog.Domain.ItemComment", [], ItemComment, () => [["Id", PrimaryKey$1_$reflection(string_type)], ["ItemId", ForeignKey$1_$reflection(MicroblogItem_$reflection())], ["IdentityId", IdentityRef_$reflection()], ["ParentId", option_type(string_type)], ["Author", string_type], ["Content", RichContent_$reflection()], ["Removed", bool_type], ["CreatedAt", CreateTimestamp_$reflection()], ["DeletedAt", option_type(SoftDelete_$reflection())]]);
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
    return record_type("Blog.Domain.Tag", [], Tag, () => [["Id", PrimaryKey$1_$reflection(string_type)], ["Name", Unique$1_$reflection(string_type)], ["CreatedAt", CreateTimestamp_$reflection()], ["DeletedAt", option_type(SoftDelete_$reflection())]]);
}

export class ItemTag extends Record {
    constructor(Id, ItemId, TagId, DeletedAt) {
        super();
        this.Id = Id;
        this.ItemId = ItemId;
        this.TagId = TagId;
        this.DeletedAt = DeletedAt;
    }
}

export function ItemTag_$reflection() {
    return record_type("Blog.Domain.ItemTag", [], ItemTag, () => [["Id", PrimaryKey$1_$reflection(string_type)], ["ItemId", ForeignKey$1_$reflection(MicroblogItem_$reflection())], ["TagId", ForeignKey$1_$reflection(Tag_$reflection())], ["DeletedAt", option_type(SoftDelete_$reflection())]]);
}

