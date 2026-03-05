import { postJson, fetchJson } from "../../../packages/hedge-extension/Api.js";
import { printf, toText } from "../../../fable_modules/fable-library-js.4.29.0/String.js";
import { uncurry2 } from "../../../fable_modules/fable-library-js.4.29.0/Util.js";
import { Decode_commentRemovedEvent, Decode_commentModeratedEvent, Decode_newCommentEvent, Decode_getFeedResponse, Decode_submitCommentResponse, Decode_submitItemResponse, Decode_getItemResponse, Decode_getTagsResponse, Decode_getItemsByTagResponse } from "../../Codecs/generated/Codecs.js";
import { toString } from "../../../fable_modules/Thoth.Json.10.2.0/Encode.fs.js";
import { encodeRecord } from "../../../packages/hedge/src/Hedge/Codec.js";
import { SubmitComment_Request_$reflection, SubmitItem_Request_$reflection } from "../../Models/Api.js";
import { Union } from "../../../fable_modules/fable-library-js.4.29.0/Types.js";
import { CommentRemovedEvent_$reflection, CommentModeratedEvent_$reflection, NewCommentEvent_$reflection } from "../../Models/Ws.js";
import { union_type } from "../../../fable_modules/fable-library-js.4.29.0/Reflection.js";
import { string, field, fromString } from "../../../fable_modules/Thoth.Json.10.2.0/Decode.fs.js";
import { Result_Map, FSharpResult$2 } from "../../../fable_modules/fable-library-js.4.29.0/Result.js";

export function getItemsByTag(id) {
    return fetchJson(toText(printf("/api/tags/%s/items"))(id), uncurry2(Decode_getItemsByTagResponse));
}

export function getTags() {
    return fetchJson("/api/tags", uncurry2(Decode_getTagsResponse));
}

export function getItem(id) {
    return fetchJson(toText(printf("/api/item/%s"))(id), uncurry2(Decode_getItemResponse));
}

export function submitItem(req) {
    return postJson("/api/item", toString(0, encodeRecord(SubmitItem_Request_$reflection(), req)), uncurry2(Decode_submitItemResponse));
}

export function submitComment(req) {
    return postJson("/api/comment", toString(0, encodeRecord(SubmitComment_Request_$reflection(), req)), uncurry2(Decode_submitCommentResponse));
}

export function getFeed() {
    return fetchJson("/api/feed", uncurry2(Decode_getFeedResponse));
}

export class WsEvent extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["NewComment", "CommentModerated", "CommentRemoved"];
    }
}

export function WsEvent_$reflection() {
    return union_type("Client.ClientGen.WsEvent", [], WsEvent, () => [[["Item", NewCommentEvent_$reflection()]], [["Item", CommentModeratedEvent_$reflection()]], [["Item", CommentRemovedEvent_$reflection()]]]);
}

export function decodeWsEvent(text) {
    const matchValue = fromString((path_1, value_1) => field("type", string, path_1, value_1), text);
    if (matchValue.tag === 1) {
        return new FSharpResult$2(1, [matchValue.fields[0]]);
    }
    else {
        switch (matchValue.fields[0]) {
            case "NewComment":
                return Result_Map((Item) => (new WsEvent(0, [Item])), fromString((path_2, value_2) => field("payload", uncurry2(Decode_newCommentEvent), path_2, value_2), text));
            case "CommentModerated":
                return Result_Map((Item_1) => (new WsEvent(1, [Item_1])), fromString((path_3, value_3) => field("payload", uncurry2(Decode_commentModeratedEvent), path_3, value_3), text));
            case "CommentRemoved":
                return Result_Map((Item_2) => (new WsEvent(2, [Item_2])), fromString((path_4, value_4) => field("payload", uncurry2(Decode_commentRemovedEvent), path_4, value_4), text));
            default:
                return new FSharpResult$2(1, [toText(printf("Unknown event: %s"))(matchValue.fields[0])]);
        }
    }
}

