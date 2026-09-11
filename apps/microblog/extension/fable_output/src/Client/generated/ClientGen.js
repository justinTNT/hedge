import { postJson, fetchJson } from "../../../packages/hedge-extension/Api.js";
import { printf, toText } from "../../../fable_modules/fable-library-js.4.29.0/String.js";
import { uncurry2 } from "../../../fable_modules/fable-library-js.4.29.0/Util.js";
import { Decode_blogCommentRemovedEvent, Decode_blogCommentModeratedEvent, Decode_blogNewCommentEvent, Decode_blogGetFeedResponse, Decode_blogSubmitCommentResponse, Decode_blogSubmitItemResponse, Decode_blogGetItemResponse, Decode_blogGetTagsResponse, Decode_blogGetItemsByTagResponse } from "../../Codecs/generated/Codecs.js";
import { toString } from "../../../fable_modules/Thoth.Json.10.2.0/Encode.fs.js";
import { encodeRecord } from "../../../packages/hedge/src/Hedge/Codec.js";
import { SubmitComment_Request_$reflection, SubmitItem_Request_$reflection } from "../../../packages/modules/blog/src/Models/Api.js";
import { Union } from "../../../fable_modules/fable-library-js.4.29.0/Types.js";
import { CommentRemovedEvent_$reflection, CommentModeratedEvent_$reflection, NewCommentEvent_$reflection } from "../../../packages/modules/blog/src/Models/Ws.js";
import { union_type } from "../../../fable_modules/fable-library-js.4.29.0/Reflection.js";
import { string, field, fromString } from "../../../fable_modules/Thoth.Json.10.2.0/Decode.fs.js";
import { Result_Map, FSharpResult$2 } from "../../../fable_modules/fable-library-js.4.29.0/Result.js";

export function blogGetItemsByTag(id) {
    return fetchJson(toText(printf("/api/blog/tags/%s/items"))(id), uncurry2(Decode_blogGetItemsByTagResponse));
}

export function blogGetTags() {
    return fetchJson("/api/blog/tags", uncurry2(Decode_blogGetTagsResponse));
}

export function blogGetItem(id) {
    return fetchJson(toText(printf("/api/blog/item/%s"))(id), uncurry2(Decode_blogGetItemResponse));
}

export function blogSubmitItem(req) {
    return postJson("/api/blog/item", toString(0, encodeRecord(SubmitItem_Request_$reflection(), req)), uncurry2(Decode_blogSubmitItemResponse));
}

export function blogSubmitComment(req) {
    return postJson("/api/blog/comment", toString(0, encodeRecord(SubmitComment_Request_$reflection(), req)), uncurry2(Decode_blogSubmitCommentResponse));
}

export function blogGetFeed(id) {
    return fetchJson(toText(printf("/api/blog/feed/%s"))(id), uncurry2(Decode_blogGetFeedResponse));
}

export class BlogWsEvent extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["BlogNewComment", "BlogCommentModerated", "BlogCommentRemoved"];
    }
}

export function BlogWsEvent_$reflection() {
    return union_type("Client.ClientGen.BlogWsEvent", [], BlogWsEvent, () => [[["Item", NewCommentEvent_$reflection()]], [["Item", CommentModeratedEvent_$reflection()]], [["Item", CommentRemovedEvent_$reflection()]]]);
}

export function blogDecodeWsEvent(text) {
    const matchValue = fromString((path_1, value_1) => field("type", string, path_1, value_1), text);
    if (matchValue.tag === 1) {
        return new FSharpResult$2(1, [matchValue.fields[0]]);
    }
    else {
        switch (matchValue.fields[0]) {
            case "NewComment":
                return Result_Map((Item) => (new BlogWsEvent(0, [Item])), fromString((path_2, value_2) => field("payload", uncurry2(Decode_blogNewCommentEvent), path_2, value_2), text));
            case "CommentModerated":
                return Result_Map((Item_1) => (new BlogWsEvent(1, [Item_1])), fromString((path_3, value_3) => field("payload", uncurry2(Decode_blogCommentModeratedEvent), path_3, value_3), text));
            case "CommentRemoved":
                return Result_Map((Item_2) => (new BlogWsEvent(2, [Item_2])), fromString((path_4, value_4) => field("payload", uncurry2(Decode_blogCommentRemovedEvent), path_4, value_4), text));
            default:
                return new FSharpResult$2(1, [toText(printf("Unknown event: %s"))(matchValue.fields[0])]);
        }
    }
}

