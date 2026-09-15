import { Record } from "../../../../../fable_modules/fable-library-js.4.29.0/Types.js";
import { bool_type, record_type, int32_type, option_type, string_type } from "../../../../../fable_modules/fable-library-js.4.29.0/Reflection.js";
import { IdentityRef_$reflection, ForeignKey$1_$reflection, SoftDelete_$reflection, UpdateTimestamp_$reflection, CreateTimestamp_$reflection, Unique$1_$reflection, EditableDate_$reflection, RichContent_$reflection, Image_$reflection, Link_$reflection, PrimaryKey$1_$reflection } from "../../../../hedge/src/Hedge/Interface.js";

export class Item extends Record {
    constructor(Id, Title, Link, Image, Extract, OwnerComment, ArticleDate, Slug, CreatedAt, UpdatedAt, ViewCount, DeletedAt) {
        super();
        this.Id = Id;
        this.Title = Title;
        this.Link = Link;
        this.Image = Image;
        this.Extract = Extract;
        this.OwnerComment = OwnerComment;
        this.ArticleDate = ArticleDate;
        this.Slug = Slug;
        this.CreatedAt = CreatedAt;
        this.UpdatedAt = UpdatedAt;
        this.ViewCount = (ViewCount | 0);
        this.DeletedAt = DeletedAt;
    }
}

export function Item_$reflection() {
    return record_type("Blog.Domain.Item", [], Item, () => [["Id", PrimaryKey$1_$reflection(string_type)], ["Title", string_type], ["Link", option_type(Link_$reflection())], ["Image", option_type(Image_$reflection())], ["Extract", option_type(RichContent_$reflection())], ["OwnerComment", RichContent_$reflection()], ["ArticleDate", EditableDate_$reflection()], ["Slug", option_type(Unique$1_$reflection(string_type))], ["CreatedAt", CreateTimestamp_$reflection()], ["UpdatedAt", option_type(UpdateTimestamp_$reflection())], ["ViewCount", int32_type], ["DeletedAt", option_type(SoftDelete_$reflection())]]);
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
    return record_type("Blog.Domain.ItemComment", [], ItemComment, () => [["Id", PrimaryKey$1_$reflection(string_type)], ["ItemId", ForeignKey$1_$reflection(Item_$reflection())], ["IdentityId", IdentityRef_$reflection()], ["ParentId", option_type(ForeignKey$1_$reflection(ItemComment_$reflection()))], ["Author", string_type], ["Content", RichContent_$reflection()], ["Removed", bool_type], ["CreatedAt", CreateTimestamp_$reflection()], ["DeletedAt", option_type(SoftDelete_$reflection())]]);
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
    return record_type("Blog.Domain.ItemTag", [], ItemTag, () => [["Id", PrimaryKey$1_$reflection(string_type)], ["ItemId", ForeignKey$1_$reflection(Item_$reflection())], ["TagId", ForeignKey$1_$reflection(Tag_$reflection())], ["DeletedAt", option_type(SoftDelete_$reflection())]]);
}

export class ItemSnapshot extends Record {
    constructor(Id, ItemId, Kind, BlobKey, SourceUrl, Status, Error$, CreatedAt, DeletedAt) {
        super();
        this.Id = Id;
        this.ItemId = ItemId;
        this.Kind = Kind;
        this.BlobKey = BlobKey;
        this.SourceUrl = SourceUrl;
        this.Status = Status;
        this.Error = Error$;
        this.CreatedAt = CreatedAt;
        this.DeletedAt = DeletedAt;
    }
}

export function ItemSnapshot_$reflection() {
    return record_type("Blog.Domain.ItemSnapshot", [], ItemSnapshot, () => [["Id", PrimaryKey$1_$reflection(string_type)], ["ItemId", ForeignKey$1_$reflection(Item_$reflection())], ["Kind", string_type], ["BlobKey", string_type], ["SourceUrl", string_type], ["Status", string_type], ["Error", option_type(string_type)], ["CreatedAt", CreateTimestamp_$reflection()], ["DeletedAt", option_type(SoftDelete_$reflection())]]);
}

