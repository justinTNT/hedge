module Client.Api

open Fable.Core
open Fable.Core.JsInterop
open Thoth.Json

/// Send a message to the background service worker and get the response.
let private sendMessage (msg: obj) : JS.Promise<obj> =
    HedgeExtension.Chrome.sendMessage msg

let fetchJson<'T> (url: string) (decoder: Decoder<'T>) : JS.Promise<Result<'T, string>> =
    promise {
        let! raw = sendMessage (createObj [ "type" ==> "api"; "method" ==> "GET"; "path" ==> url ])
        let ok = raw?ok : bool
        if ok then
            let data = raw?data
            let json = JS.JSON.stringify data
            return Decode.fromString decoder json
        else
            let err = raw?error
            let msg = if isNullOrUndefined err then "Request failed" else string err
            return Error msg
    }

let postJson<'T> (url: string) (body: string) (decoder: Decoder<'T>) : JS.Promise<Result<'T, string>> =
    promise {
        let parsed = JS.JSON.parse body
        let! raw = sendMessage (createObj [ "type" ==> "api"; "method" ==> "POST"; "path" ==> url; "body" ==> parsed ])
        let ok = raw?ok : bool
        if ok then
            let data = raw?data
            let json = JS.JSON.stringify data
            return Decode.fromString decoder json
        else
            let err = raw?error
            let msg = if isNullOrUndefined err then "Request failed" else string err
            return Error msg
    }
