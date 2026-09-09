module Server.Handlers

open Fable.Core
open Fable.Core.JsInterop
open Thoth.Json
open Hedge.Codec
open Hedge.Workers
open Hedge.Router
open Models.Api
open Server.Env
open Server.Db

/// GET /api/site — the whole nav (flat; client builds the tree) + home page.
let getSite (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let! r = (env.DB.prepare Sql.listMenu).all()
        let nodes =
            r.results
            |> Array.map (fun row ->
                ({ Item = rowStr row "item"
                   Title = rowStr row "title"
                   Link = rowStr row "link"
                   Parent = rowStr row "parent_item"
                   Ordinal = rowInt row "ordinal" } : GetSite.MenuNode))
            |> Array.toList
        let resp : GetSite.Response = { Menu = nodes; Home = "front" }
        return okJson (encode resp |> Encode.toString 0)
    }

/// GET /api/page/:name — one page's content (original HTML body).
let getPage (name: string) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let slug = JS.decodeURIComponent name
        let! r = (bind (env.DB.prepare Sql.pageByName) [| box slug |]).all()
        if r.results.Length = 0 then
            return notFound ()
        else
            let row = r.results.[0]
            let view : GetPage.PageView =
                { Name = rowStr row "name"
                  Title = rowStr row "title"
                  Teaser = rowStr row "teaser"
                  Body = rowStr row "body" }
            let resp : GetPage.Response = { Page = view }
            return okJson (encode resp |> Encode.toString 0)
    }
