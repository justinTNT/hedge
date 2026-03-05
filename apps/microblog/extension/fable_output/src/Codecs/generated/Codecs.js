import { decodeRecordObj } from "../../../packages/hedge/src/Hedge/Codec.js";
import { GuestSession_$reflection, ItemTag_$reflection, Tag_$reflection, ItemComment_$reflection, MicroblogItem_$reflection, Guest_$reflection } from "../../Models/Domain.js";
import { map } from "../../../fable_modules/Thoth.Json.10.2.0/Decode.fs.js";
import { uncurry2 } from "../../../fable_modules/fable-library-js.4.29.0/Util.js";
import { SubmitComment_Request_$reflection, SubmitItem_Request_$reflection, GetFeed_Response_$reflection, SubmitComment_Response_$reflection, SubmitItem_Response_$reflection, GetItem_Response_$reflection, GetTags_Response_$reflection, GetItemsByTag_Response_$reflection, GetFeed_FeedItem_$reflection, SubmitComment_CommentItem_$reflection, SubmitItem_MicroblogItem_$reflection } from "../../Models/Api.js";
import { CommentRemovedEvent_$reflection, CommentModeratedEvent_$reflection, NewCommentEvent_$reflection } from "../../Models/Ws.js";
import { FieldAttr, FieldType, fieldWith, schema } from "../../../packages/hedge/src/Hedge/Schema.js";
import { empty, singleton, ofArray } from "../../../fable_modules/fable-library-js.4.29.0/List.js";

export const Decode_guest = (() => {
    const d = decodeRecordObj(Guest_$reflection());
    return (path) => ((value_1) => map((value) => value, uncurry2(d), path, value_1));
})();

export const Decode_microblogItem = (() => {
    const d = decodeRecordObj(MicroblogItem_$reflection());
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

export const Decode_guestSession = (() => {
    const d = decodeRecordObj(GuestSession_$reflection());
    return (path) => ((value_1) => map((value) => value, uncurry2(d), path, value_1));
})();

export const Decode_microblogItemView = (() => {
    const d = decodeRecordObj(SubmitItem_MicroblogItem_$reflection());
    return (path) => ((value_1) => map((value) => value, uncurry2(d), path, value_1));
})();

export const Decode_commentItem = (() => {
    const d = decodeRecordObj(SubmitComment_CommentItem_$reflection());
    return (path) => ((value_1) => map((value) => value, uncurry2(d), path, value_1));
})();

export const Decode_feedItem = (() => {
    const d = decodeRecordObj(GetFeed_FeedItem_$reflection());
    return (path) => ((value_1) => map((value) => value, uncurry2(d), path, value_1));
})();

export const Decode_getItemsByTagResponse = (() => {
    const d = decodeRecordObj(GetItemsByTag_Response_$reflection());
    return (path) => ((value_1) => map((value) => value, uncurry2(d), path, value_1));
})();

export const Decode_getTagsResponse = (() => {
    const d = decodeRecordObj(GetTags_Response_$reflection());
    return (path) => ((value_1) => map((value) => value, uncurry2(d), path, value_1));
})();

export const Decode_getItemResponse = (() => {
    const d = decodeRecordObj(GetItem_Response_$reflection());
    return (path) => ((value_1) => map((value) => value, uncurry2(d), path, value_1));
})();

export const Decode_submitItemResponse = (() => {
    const d = decodeRecordObj(SubmitItem_Response_$reflection());
    return (path) => ((value_1) => map((value) => value, uncurry2(d), path, value_1));
})();

export const Decode_submitCommentResponse = (() => {
    const d = decodeRecordObj(SubmitComment_Response_$reflection());
    return (path) => ((value_1) => map((value) => value, uncurry2(d), path, value_1));
})();

export const Decode_getFeedResponse = (() => {
    const d = decodeRecordObj(GetFeed_Response_$reflection());
    return (path) => ((value_1) => map((value) => value, uncurry2(d), path, value_1));
})();

export const Decode_submitItemReq = (() => {
    const d = decodeRecordObj(SubmitItem_Request_$reflection());
    return (path) => ((value_1) => map((value) => value, uncurry2(d), path, value_1));
})();

export const Decode_submitCommentReq = (() => {
    const d = decodeRecordObj(SubmitComment_Request_$reflection());
    return (path) => ((value_1) => map((value) => value, uncurry2(d), path, value_1));
})();

export const Decode_newCommentEvent = (() => {
    const d = decodeRecordObj(NewCommentEvent_$reflection());
    return (path) => ((value_1) => map((value) => value, uncurry2(d), path, value_1));
})();

export const Decode_commentModeratedEvent = (() => {
    const d = decodeRecordObj(CommentModeratedEvent_$reflection());
    return (path) => ((value_1) => map((value) => value, uncurry2(d), path, value_1));
})();

export const Decode_commentRemovedEvent = (() => {
    const d = decodeRecordObj(CommentRemovedEvent_$reflection());
    return (path) => ((value_1) => map((value) => value, uncurry2(d), path, value_1));
})();

export const Validate_submitItemSchema = schema("SubmitItem.Request", ofArray([fieldWith("Title", new FieldType(0, []), ofArray([new FieldAttr(7, []), new FieldAttr(8, [])])), fieldWith("Slug", new FieldType(3, [new FieldType(0, [])]), singleton(new FieldAttr(8, []))), fieldWith("Link", new FieldType(3, [new FieldType(0, [])]), singleton(new FieldAttr(8, []))), fieldWith("Image", new FieldType(3, [new FieldType(0, [])]), singleton(new FieldAttr(8, []))), fieldWith("Extract", new FieldType(3, [new FieldType(0, [])]), singleton(new FieldAttr(8, []))), fieldWith("OwnerComment", new FieldType(0, []), ofArray([new FieldAttr(7, []), new FieldAttr(8, [])])), fieldWith("Tags", new FieldType(4, [new FieldType(0, [])]), empty())]));

export const Validate_submitCommentSchema = schema("SubmitComment.Request", ofArray([fieldWith("ItemId", new FieldType(0, []), ofArray([new FieldAttr(7, []), new FieldAttr(8, [])])), fieldWith("ParentId", new FieldType(3, [new FieldType(0, [])]), singleton(new FieldAttr(8, []))), fieldWith("Content", new FieldType(0, []), ofArray([new FieldAttr(7, []), new FieldAttr(8, [])])), fieldWith("Author", new FieldType(3, [new FieldType(0, [])]), singleton(new FieldAttr(8, [])))]));

