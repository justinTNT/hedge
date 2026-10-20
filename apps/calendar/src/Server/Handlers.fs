module Server.Handlers

open Fable.Core
open Hedge.Workers
open Hedge.Router
open Codecs
open Models.Api
open Server.Env

let createPost (req: CreatePost.Request) (request: WorkerRequest)
    (env: Env) (ctx: ExecutionContext) : JS.Promise<WorkerResponse> =
    promise {
        // TODO: implement
        return notFound ()
    }

let getPosts (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        // TODO: implement
        return notFound ()
    }
