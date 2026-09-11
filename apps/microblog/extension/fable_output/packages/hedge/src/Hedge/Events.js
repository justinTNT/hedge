import { object, toString } from "../../../../fable_modules/Thoth.Json.10.2.0/Encode.fs.js";

/**
 * Broadcast an event to everyone connected to `topic` on the live-events DO
 * (see Hedge.EventHub). `payload` is the event's already-encoded JSON; it's
 * wrapped as {type, payload} and fanned out to every socket on that topic.
 * Fire-and-forget via ctx.waitUntil, so it never blocks the response.
 * 
 * Consumer side (e.g. a comment handler):
 * Hedge.Events.broadcast env.EVENTS ctx articleId "NewComment"
 * (Codecs.Encode.newCommentEvent event)
 */
export function broadcast(events, ctx, topic, eventType, payload) {
    const json = toString(0, object([["type", eventType], ["payload", payload]]));
    const doId = events.idFromName(topic);
    const stub = events.get(doId);
    const req = new Request("https://do/broadcast", { method: "POST", body: json, headers: { 'Content-Type': 'application/json' } });
    ctx.waitUntil(stub.fetch(req));
}

