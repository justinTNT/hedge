import { decodeRecordObj } from "../../../hedge/src/Hedge/Codec.js";
import { ItemSnapshot_$reflection, ItemTag_$reflection, Tag_$reflection, ItemComment_$reflection, Item_$reflection } from "../src/Models/Domain.js";
import { map } from "../../../../fable_modules/Thoth.Json.10.2.0/Decode.fs.js";
import { uncurry2 } from "../../../../fable_modules/fable-library-js.4.29.0/Util.js";
import { SubmitComment_Request_$reflection, SubmitItem_Request_$reflection, GetFeed_Response_$reflection, SubmitComment_Response_$reflection, SubmitItem_Response_$reflection, GetItem_Response_$reflection, GetTags_Response_$reflection, GetItemsByTag_Response_$reflection, GetFeed_FeedItem_$reflection, SubmitComment_CommentItem_$reflection, SubmitItem_Item_$reflection } from "../src/Models/Api.js";
import { NewCommentEvent_$reflection } from "../src/Models/Ws.js";
import { FieldAttr, FieldType, fieldWith, schema } from "../../../hedge/src/Hedge/Schema.js";
import { empty, singleton, ofArray } from "../../../../fable_modules/fable-library-js.4.29.0/List.js";

export const Decode_item = (() => {
    const d = decodeRecordObj(Item_$reflection());
    return (path) => ((value_1) => map((value) => value, uncurry2(d), path, value_1));
})();

export const Decode_itemComment = (() => {
    const d = decodeRecordObj(ItemComment_$reflection());
    return (path) => ((value_1) => map((value) => value, uncurry2(d), path, value_1));
})();

export const Decode_tag = (() => {
    const d = decodeRecordObj(Tag_$reflection());
    return (path) => ((value_1) => map((value) => value, uncurry2(d), path, value_1));
})();

export const Decode_itemTag = (() => {
    const d = decodeRecordObj(ItemTag_$reflection());
    return (path) => ((value_1) => map((value) => value, uncurry2(d), path, value_1));
})();

export const Decode_itemSnapshot = (() => {
    const d = decodeRecordObj(ItemSnapshot_$reflection());
    return (path) => ((value_1) => map((value) => value, uncurry2(d), path, value_1));
})();

export const Decode_blogItemView = (() => {
    const d = decodeRecordObj(SubmitItem_Item_$reflection());
    return (path) => ((value_1) => map((value) => value, uncurry2(d), path, value_1));
})();

export const Decode_blogCommentItem = (() => {
    const d = decodeRecordObj(SubmitComment_CommentItem_$reflection());
    return (path) => ((value_1) => map((value) => value, uncurry2(d), path, value_1));
})();

export const Decode_blogFeedItem = (() => {
    const d = decodeRecordObj(GetFeed_FeedItem_$reflection());
    return (path) => ((value_1) => map((value) => value, uncurry2(d), path, value_1));
})();

export const Decode_blogGetItemsByTagResponse = (() => {
    const d = decodeRecordObj(GetItemsByTag_Response_$reflection());
    return (path) => ((value_1) => map((value) => value, uncurry2(d), path, value_1));
})();

export const Decode_blogGetTagsResponse = (() => {
    const d = decodeRecordObj(GetTags_Response_$reflection());
    return (path) => ((value_1) => map((value) => value, uncurry2(d), path, value_1));
})();

export const Decode_blogGetItemResponse = (() => {
    const d = decodeRecordObj(GetItem_Response_$reflection());
    return (path) => ((value_1) => map((value) => value, uncurry2(d), path, value_1));
})();

export const Decode_blogSubmitItemResponse = (() => {
    const d = decodeRecordObj(SubmitItem_Response_$reflection());
    return (path) => ((value_1) => map((value) => value, uncurry2(d), path, value_1));
})();

export const Decode_blogSubmitCommentResponse = (() => {
    const d = decodeRecordObj(SubmitComment_Response_$reflection());
    return (path) => ((value_1) => map((value) => value, uncurry2(d), path, value_1));
})();

export const Decode_blogGetFeedResponse = (() => {
    const d = decodeRecordObj(GetFeed_Response_$reflection());
    return (path) => ((value_1) => map((value) => value, uncurry2(d), path, value_1));
})();

export const Decode_blogSubmitItemReq = (() => {
    const d = decodeRecordObj(SubmitItem_Request_$reflection());
    return (path) => ((value_1) => map((value) => value, uncurry2(d), path, value_1));
})();

export const Decode_blogSubmitCommentReq = (() => {
    const d = decodeRecordObj(SubmitComment_Request_$reflection());
    return (path) => ((value_1) => map((value) => value, uncurry2(d), path, value_1));
})();

export const Decode_blogNewCommentEvent = (() => {
    const d = decodeRecordObj(NewCommentEvent_$reflection());
    return (path) => ((value_1) => map((value) => value, uncurry2(d), path, value_1));
})();

export const Validate_blogSubmitItemSchema = schema("Blog.Api.SubmitItem.Request", ofArray([fieldWith("Title", new FieldType(0, []), ofArray([new FieldAttr(9, []), new FieldAttr(10, [])])), fieldWith("Slug", new FieldType(3, [new FieldType(0, [])]), singleton(new FieldAttr(10, []))), fieldWith("Link", new FieldType(3, [new FieldType(0, [])]), singleton(new FieldAttr(10, []))), fieldWith("Image", new FieldType(3, [new FieldType(0, [])]), singleton(new FieldAttr(10, []))), fieldWith("Extract", new FieldType(3, [new FieldType(0, [])]), singleton(new FieldAttr(10, []))), fieldWith("OwnerComment", new FieldType(0, []), ofArray([new FieldAttr(9, []), new FieldAttr(10, [])])), fieldWith("Tags", new FieldType(4, [new FieldType(0, [])]), empty())]));

export const Validate_blogSubmitCommentSchema = schema("Blog.Api.SubmitComment.Request", ofArray([fieldWith("ItemId", new FieldType(0, []), ofArray([new FieldAttr(9, []), new FieldAttr(10, [])])), fieldWith("ParentId", new FieldType(3, [new FieldType(0, [])]), singleton(new FieldAttr(10, []))), fieldWith("Content", new FieldType(0, []), ofArray([new FieldAttr(9, []), new FieldAttr(10, [])])), fieldWith("Author", new FieldType(3, [new FieldType(0, [])]), singleton(new FieldAttr(10, [])))]));

