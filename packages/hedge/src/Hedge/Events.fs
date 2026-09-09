module Hedge.Events

open Fable.Core
open Fable.Core.JsInterop
open Thoth.Json
open Hedge.Workers

/// Broadcast an event to everyone connected to `topic` on the live-events DO
/// (see Hedge.EventHub). `payload` is the event's already-encoded JSON; it's
/// wrapped as {type, payload} and fanned out to every socket on that topic.
/// Fire-and-forget via ctx.waitUntil, so it never blocks the response.
///
/// Consumer side (e.g. a comment handler):
///   Hedge.Events.broadcast env.EVENTS ctx articleId "NewComment"
///     (Codecs.Encode.newCommentEvent event)
let broadcast (events: DurableObjectNamespace) (ctx: ExecutionContext)
              (topic: string) (eventType: string) (payload: JsonValue) : unit =
    let json =
        Encode.object [
            "type", Encode.string eventType
            "payload", payload
        ] |> Encode.toString 0
    let doId = events.idFromName(topic)
    let stub = events.get(doId)
    let req = createRequest "https://do/broadcast" "POST" json
    ctx.waitUntil(stub.fetch(req) |> unbox<JS.Promise<obj>>)
