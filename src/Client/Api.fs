module Client.Api

open Fable.Core
open Fable.Core.JsInterop
open Fetch
open Thoth.Json
open Codecs
open Models.Api

/// Typed HTTP client using Fetch + Thoth.

let private fetchJson<'T> (url: string) (decoder: Decoder<'T>) : JS.Promise<Result<'T, string>> =
    promise {
        let! response = fetch url []
        let! text = response.text()
        return Decode.fromString decoder text
    }

let private postJson<'T> (url: string) (body: string) (decoder: Decoder<'T>) : JS.Promise<Result<'T, string>> =
    promise {
        let! response = fetch url [
            Method HttpMethod.POST
            requestHeaders [ ContentType "application/json" ]
            Body (BodyInit.Case3 body)
        ]
        let! text = response.text()
        return Decode.fromString decoder text
    }

let getFeed () =
    fetchJson Routes.feed Decode.getFeedResponse

let getItem (itemId: string) =
    fetchJson (Routes.item itemId) Decode.getItemResponse

let submitComment (req: SubmitComment.Request) =
    let body = Encode.submitCommentReq req |> Encode.toString 0
    postJson Routes.submitComment body Decode.submitCommentResponse

let submitItem (req: SubmitItem.Request) =
    let body = Encode.submitItemReq req |> Encode.toString 0
    postJson Routes.submitItem body Decode.submitItemResponse

let getTags () =
    fetchJson Routes.tags Decode.getTagsResponse

let getItemsByTag (tag: string) =
    fetchJson (Routes.itemsByTag tag) Decode.getItemsByTagResponse

// -- WebSocket --

[<Emit("'ws://' + window.location.host")>]
let private wsBase () : string = jsNative

[<Emit("""
  (function() {
    var ws = new WebSocket($0);
    ws.onmessage = $1;
    ws.onerror = $2;
    return function() { ws.close(); };
  })()
""")>]
let private openWebSocket (url: string) (onMessage: obj -> unit) (onError: obj -> unit) : (unit -> unit) = jsNative

let connectEvents (itemId: string) (onMessage: Models.Sse.NewCommentEvent -> unit) (onError: string -> unit) : (unit -> unit) =
    let url = sprintf "%s%s?itemId=%s" (wsBase()) Routes.events itemId
    openWebSocket
        url
        (fun e ->
            let text : string = e?data |> string
            match Decode.fromString (Decode.field "payload" Decode.newCommentEvent) text with
            | Ok event -> onMessage event
            | Error err -> onError err)
        (fun _ -> onError "WebSocket error")
