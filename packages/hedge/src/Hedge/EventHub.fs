module Hedge.EventHub

open Fable.Core
open Fable.Core.JsInterop
open Hedge.Workers

/// Generic hibernatable-WebSocket broadcast Durable Object — the live-events
/// transport. Payload-agnostic: keyed per topic (idFromName), it fans out an
/// opaque body POSTed to it to every socket connected to that topic. Consumers
/// (comments today; reactions/votes/presence later) define their own event
/// types and broadcast via Hedge.Events.broadcast. Each app re-exports this
/// class from its worker entry, because Cloudflare requires a Durable Object
/// class to be exported by the deploying worker.
[<AttachMembers>]
type EventHub(state: DurableObjectState, _env: obj) =

    member _.fetch(request: WorkerRequest) : JS.Promise<WorkerResponse> =
        promise {
            if isWebSocketUpgrade request then
                let pair = createWebSocketPair ()
                state.acceptWebSocket pair.[1]
                return upgradeResponse pair.[0]
            else
                let! body = request.text()
                for ws in state.getWebSockets() do
                    try ws.send body with _ -> ()
                let options = createObj [ "status" ==> 200 ]
                return WorkerResponse.create("""{"ok":true}""", options)
        }

    member _.webSocketMessage(_ws: WebSocket, _msg: string) : unit = ()
    member _.webSocketClose(_ws: WebSocket, _code: int, _reason: string, _wasClean: bool) : unit = ()
