module Server.Handlers

open Fable.Core
open Hedge.Workers
open Hedge.Router
open Codecs
open Models.Api
open Server.Env

let getArticle (id: string) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        // TODO: implement
        return notFound ()
    }

let submitComment (req: SubmitComment.Request) (request: WorkerRequest)
    (env: Env) (ctx: ExecutionContext) : JS.Promise<WorkerResponse> =
    promise {
        // TODO: implement
        return notFound ()
    }

let getArticles (id: string) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        // TODO: implement
        return notFound ()
    }
