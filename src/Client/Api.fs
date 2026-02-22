module Client.Api

open Fable.Core
open Fable.Core.JsInterop
open Fetch
open Thoth.Json
open Hedge.Interface
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
    let (Get path) = GetFeed.endpoint
    fetchJson path Decode.getFeedResponse

let getItem (itemId: string) =
    let (GetOne f) = GetItem.endpoint
    fetchJson (f itemId) Decode.getItemResponse

let submitComment (req: SubmitComment.Request) =
    let (Post path) = SubmitComment.endpoint
    let body = Encode.submitCommentReq req |> Encode.toString 0
    postJson path body Decode.submitCommentResponse

let submitItem (req: SubmitItem.Request) =
    let (Post path) = SubmitItem.endpoint
    let body = Encode.submitItemReq req |> Encode.toString 0
    postJson path body Decode.submitItemResponse

let getTags () =
    let (Get path) = GetTags.endpoint
    fetchJson path Decode.getTagsResponse

let getItemsByTag (tag: string) =
    let (GetOne f) = GetItemsByTag.endpoint
    fetchJson (f tag) Decode.getItemsByTagResponse

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

let connectEvents (itemId: string) (onMessage: Models.Ws.NewCommentEvent -> unit) (onError: string -> unit) : (unit -> unit) =
    let (Get path) = Events.endpoint
    let url = sprintf "%s%s?itemId=%s" (wsBase()) path itemId
    openWebSocket
        url
        (fun e ->
            let text : string = e?data |> string
            match Decode.fromString (Decode.field "payload" Decode.newCommentEvent) text with
            | Ok event -> onMessage event
            | Error err -> onError err)
        (fun _ -> onError "WebSocket error")
