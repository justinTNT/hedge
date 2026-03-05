import { Record } from "../../fable_modules/fable-library-js.4.29.0/Types.js";
import { list_type, record_type, int32_type, option_type, string_type } from "../../fable_modules/fable-library-js.4.29.0/Reflection.js";
import { GetOne$1, Link_$reflection, Post$2, Get$1, RichContent_$reflection } from "../../packages/hedge/src/Hedge/Interface.js";
import { printf, toText } from "../../fable_modules/fable-library-js.4.29.0/String.js";

export class GetFeed_FeedItem extends Record {
    constructor(Id, Title, Slug, Image, Extract, OwnerComment, Timestamp) {
        super();
        this.Id = Id;
        this.Title = Title;
        this.Slug = Slug;
        this.Image = Image;
        this.Extract = Extract;
        this.OwnerComment = OwnerComment;
        this.Timestamp = (Timestamp | 0);
    }
}

export function GetFeed_FeedItem_$reflection() {
    return record_type("Models.Api.GetFeed.FeedItem", [], GetFeed_FeedItem, () => [["Id", string_type], ["Title", string_type], ["Slug", option_type(string_type)], ["Image", option_type(string_type)], ["Extract", option_type(RichContent_$reflection())], ["OwnerComment", RichContent_$reflection()], ["Timestamp", int32_type]]);
}

export class GetFeed_Response extends Record {
    constructor(Items) {
        super();
        this.Items = Items;
    }
}

export function GetFeed_Response_$reflection() {
    return record_type("Models.Api.GetFeed.Response", [], GetFeed_Response, () => [["Items", list_type(GetFeed_FeedItem_$reflection())]]);
}

export const GetFeed_endpoint = new Get$1("/api/feed");

export class SubmitComment_CommentItem extends Record {
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

export function SubmitComment_CommentItem_$reflection() {
    return record_type("Models.Api.SubmitComment.CommentItem", [], SubmitComment_CommentItem, () => [["Id", string_type], ["ItemId", string_type], ["GuestId", string_type], ["ParentId", option_type(string_type)], ["Author", string_type], ["Content", RichContent_$reflection()], ["Timestamp", int32_type]]);
}

export class SubmitComment_Request extends Record {
    constructor(ItemId, ParentId, Content, Author) {
        super();
        this.ItemId = ItemId;
        this.ParentId = ParentId;
        this.Content = Content;
        this.Author = Author;
    }
}

export function SubmitComment_Request_$reflection() {
    return record_type("Models.Api.SubmitComment.Request", [], SubmitComment_Request, () => [["ItemId", string_type], ["ParentId", option_type(string_type)], ["Content", string_type], ["Author", option_type(string_type)]]);
}

export class SubmitComment_ServerContext extends Record {
    constructor(FreshGuestId, FreshCommentId) {
        super();
        this.FreshGuestId = FreshGuestId;
        this.FreshCommentId = FreshCommentId;
    }
}

export function SubmitComment_ServerContext_$reflection() {
    return record_type("Models.Api.SubmitComment.ServerContext", [], SubmitComment_ServerContext, () => [["FreshGuestId", string_type], ["FreshCommentId", string_type]]);
}

export class SubmitComment_Response extends Record {
    constructor(Comment$) {
        super();
        this.Comment = Comment$;
    }
}

export function SubmitComment_Response_$reflection() {
    return record_type("Models.Api.SubmitComment.Response", [], SubmitComment_Response, () => [["Comment", SubmitComment_CommentItem_$reflection()]]);
}

export const SubmitComment_endpoint = new Post$2("/api/comment");

export class SubmitItem_MicroblogItem extends Record {
    constructor(Id, Title, Slug, Link, Image, Extract, OwnerComment, Tags, Comments, Timestamp) {
        super();
        this.Id = Id;
        this.Title = Title;
        this.Slug = Slug;
        this.Link = Link;
        this.Image = Image;
        this.Extract = Extract;
        this.OwnerComment = OwnerComment;
        this.Tags = Tags;
        this.Comments = Comments;
        this.Timestamp = (Timestamp | 0);
    }
}

export function SubmitItem_MicroblogItem_$reflection() {
    return record_type("Models.Api.SubmitItem.MicroblogItem", [], SubmitItem_MicroblogItem, () => [["Id", string_type], ["Title", string_type], ["Slug", option_type(string_type)], ["Link", option_type(Link_$reflection())], ["Image", option_type(Link_$reflection())], ["Extract", option_type(RichContent_$reflection())], ["OwnerComment", RichContent_$reflection()], ["Tags", list_type(string_type)], ["Comments", list_type(SubmitComment_CommentItem_$reflection())], ["Timestamp", int32_type]]);
}

export class SubmitItem_Request extends Record {
    constructor(Title, Slug, Link, Image, Extract, OwnerComment, Tags) {
        super();
        this.Title = Title;
        this.Slug = Slug;
        this.Link = Link;
        this.Image = Image;
        this.Extract = Extract;
        this.OwnerComment = OwnerComment;
        this.Tags = Tags;
    }
}

export function SubmitItem_Request_$reflection() {
    return record_type("Models.Api.SubmitItem.Request", [], SubmitItem_Request, () => [["Title", string_type], ["Slug", option_type(string_type)], ["Link", option_type(string_type)], ["Image", option_type(string_type)], ["Extract", option_type(string_type)], ["OwnerComment", string_type], ["Tags", list_type(string_type)]]);
}

export class SubmitItem_ServerContext extends Record {
    constructor(FreshTagIds) {
        super();
        this.FreshTagIds = FreshTagIds;
    }
}

export function SubmitItem_ServerContext_$reflection() {
    return record_type("Models.Api.SubmitItem.ServerContext", [], SubmitItem_ServerContext, () => [["FreshTagIds", list_type(string_type)]]);
}

export class SubmitItem_Response extends Record {
    constructor(Item) {
        super();
        this.Item = Item;
    }
}

export function SubmitItem_Response_$reflection() {
    return record_type("Models.Api.SubmitItem.Response", [], SubmitItem_Response, () => [["Item", SubmitItem_MicroblogItem_$reflection()]]);
}

export const SubmitItem_endpoint = new Post$2("/api/item");

export class GetItem_Response extends Record {
    constructor(Item) {
        super();
        this.Item = Item;
    }
}

export function GetItem_Response_$reflection() {
    return record_type("Models.Api.GetItem.Response", [], GetItem_Response, () => [["Item", SubmitItem_MicroblogItem_$reflection()]]);
}

export const GetItem_endpoint = new GetOne$1((() => {
    const clo = toText(printf("/api/item/%s"));
    return clo;
})());

export class GetTags_Response extends Record {
    constructor(Tags) {
        super();
        this.Tags = Tags;
    }
}

export function GetTags_Response_$reflection() {
    return record_type("Models.Api.GetTags.Response", [], GetTags_Response, () => [["Tags", list_type(string_type)]]);
}

export const GetTags_endpoint = new Get$1("/api/tags");

export class GetItemsByTag_Response extends Record {
    constructor(Tag, Items) {
        super();
        this.Tag = Tag;
        this.Items = Items;
    }
}

export function GetItemsByTag_Response_$reflection() {
    return record_type("Models.Api.GetItemsByTag.Response", [], GetItemsByTag_Response, () => [["Tag", string_type], ["Items", list_type(GetFeed_FeedItem_$reflection())]]);
}

export const GetItemsByTag_endpoint = new GetOne$1((() => {
    const clo = toText(printf("/api/tags/%s/items"));
    return clo;
})());

export const Events_endpoint = new Get$1("/api/events");

