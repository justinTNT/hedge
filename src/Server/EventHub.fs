module Server.EventHub

open Fable.Core
open Fable.Core.JsInterop
open Hedge.Workers
open Server.Env

[<AttachMembers>]
type EventHub(state: DurableObjectState, _env: Env) =

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
