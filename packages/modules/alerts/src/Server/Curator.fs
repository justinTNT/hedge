module Alerts.Curator

// The dedicated curator surface (NOT the owner admin): guest-cookie-authorized endpoints over the
// alerts curation queue. Authorization is INJECTED (services.AuthorizeCurator) — alerts names no
// access-control layer. State-gated: only UNDECIDED posts are mutable; a 0-row update means another
// curator already actioned it (or the cron may have snapshotted it for publication) -> 409.

open Fable.Core
open Fable.Core.JsInterop
open Thoth.Json
open Hedge.Interface
open Hedge.Workers
open Hedge.Router
open Alerts.Services

// -- responses, optionally attaching the guest-cookie renewal --
let private jsonWith (status: int) (body: string) (setCookie: string option) : WorkerResponse =
    match setCookie with
    | Some c -> jsonResponseWithCookie body status c
    | None -> jsonResponse body status

let private okBool (setCookie: string option) =
    jsonWith 200 (Encode.object [ "ok", Encode.bool true ] |> Encode.toString 0) setCookie

/// affected-row count from a run() result's meta.
let private changedRows (r: D1Result<obj>) : int =
    if isNull (box r.meta) then 0 else (r.meta?changes : int)

/// Gate an action on the injected curator authorization; map the three outcomes to 401 / 403 / act.
let private gated (services: Services) (request: WorkerRequest)
                  (act: string option -> JS.Promise<WorkerResponse>) : JS.Promise<WorkerResponse> =
    promise {
        let! auth = services.AuthorizeCurator request
        match auth with
        | AuthRequired sc -> return jsonWith 401 """{"error":"Unauthorized"}""" sc
        | Forbidden sc -> return jsonWith 403 """{"error":"Forbidden"}""" sc
        | Allowed sc -> return! act sc
    }

/// A state-guarded single-row UPDATE: 0 rows changed = already decided by another curator -> 409.
let private guardedUpdate (services: Services) (sql: string) (args: obj array) (sc: string option) : JS.Promise<WorkerResponse> =
    promise {
        let! r = (bind (services.DB.prepare sql) args).run()
        return if changedRows r > 0 then okBool sc
               else jsonWith 409 """{"error":"Already actioned by another curator"}""" sc
    }

let queue (services: Services) (request: WorkerRequest) : JS.Promise<WorkerResponse> =
    gated services request (fun sc -> promise {
        let! result = (services.DB.prepare Sql.selectPendingQueue).all()
        let items =
            result.results
            |> Array.map (fun row ->
                ({ Id = rowStr row "id"
                   Title = rowStr row "title"
                   Link = rowStr row "link"
                   Snippet = rowStr row "snippet"
                   OwnerComment = RichContent (rowStr row "owner_comment")
                   PublishedAt = rowInt row "published_at"
                   Topic = rowStr row "topic" } : Alerts.Api.Queue.Item))
            |> Array.toList
        let body =
            Encode.object [ "items", Encode.list (items |> List.map Alerts.Codecs.Encode.alertsItem) ]
            |> Encode.toString 0
        return jsonWith 200 body sc
    })

let approve (req: Alerts.Api.Approve.Request) (services: Services) (request: WorkerRequest) : JS.Promise<WorkerResponse> =
    gated services request (fun sc -> guardedUpdate services Sql.approvePendingPost [| box req.Id |] sc)

let dismiss (req: Alerts.Api.Dismiss.Request) (services: Services) (request: WorkerRequest) : JS.Promise<WorkerResponse> =
    gated services request (fun sc -> guardedUpdate services Sql.rejectPendingPost [| box req.Id |] sc)

let editFraming (req: Alerts.Api.EditFraming.Request) (services: Services) (request: WorkerRequest) : JS.Promise<WorkerResponse> =
    gated services request (fun sc ->
        guardedUpdate services Sql.updateFraming [| box req.Title; box req.Snippet; box req.OwnerComment; box req.Id |] sc)
