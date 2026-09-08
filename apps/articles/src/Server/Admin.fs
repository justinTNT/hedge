module Server.Admin

open Fable.Core
open Thoth.Json
open Hedge.Workers
open Hedge.Router
open Hedge.SchemaCodec
open Server.Env
open Server.AdminConfig

let checkAdmin (request: WorkerRequest) (env: Env) : bool =
    let key = getHeader request "X-Admin-Key"
    key <> "" && key = env.ADMIN_KEY

let private findEntity (name: string) =
    entities |> List.tryFind (fun e -> e.Name = name)

let private typesResponse () : WorkerResponse =
    let body =
        Encode.object [
            "types", Encode.list (entities |> List.map (fun e ->
                Encode.object [
                    "name", Encode.string e.Name
                    "schema", encodeTypeSchema e.Schema
                ]))
        ] |> Encode.toString 0
    okJson body

let private listResponse (entity: AdminEntity) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let! json = entity.List env
        let body = sprintf """{"records":%s}""" json
        return okJson body
    }

let private getResponse (entity: AdminEntity) (id: string) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let! result = entity.Get id env
        match result with
        | None -> return notFound ()
        | Some json ->
            let body = sprintf """{"record":%s}""" json
            return okJson body
    }

let private updateResponse (entity: AdminEntity) (id: string) (request: WorkerRequest) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let! bodyText = request.text()
        let! json = entity.Update id bodyText env
        let body = sprintf """{"record":%s}""" json
        return okJson body
    }

let private deleteResponse (entity: AdminEntity) (id: string) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        do! entity.Delete id env
        return okJson """{"ok":true}"""
    }

/// Try to handle an admin route. Returns Some promise if matched, None otherwise.
let handleRequest (request: WorkerRequest) (env: Env) (route: Route) : JS.Promise<WorkerResponse> option =
    match route with
    // GET /api/admin/types — list available schemas
    | GET path when matchPath "/api/admin/types" path = Some (Exact "/api/admin/types") ->
        Some (promise { return typesResponse () })

    // GET /api/admin/:type — list records
    | GET path ->
        match matchPath "/api/admin/:id" path with
        | Some (WithParam (_, typeName)) ->
            // Check for /:type/:id pattern (path has extra segment)
            let parts = typeName.Split('/')
            if parts.Length = 1 then
                match findEntity typeName with
                | Some entity ->
                    Some (promise {
                        if not (checkAdmin request env) then return unauthorized ()
                        else return! listResponse entity env
                    })
                | None -> None
            elif parts.Length = 2 then
                let entityName = parts.[0]
                let recordId = parts.[1]
                match findEntity entityName with
                | Some entity ->
                    Some (promise {
                        if not (checkAdmin request env) then return unauthorized ()
                        else return! getResponse entity recordId env
                    })
                | None -> None
            else None
        | _ -> None

    // PUT /api/admin/:type/:id — update record
    | PUT path ->
        match matchPath "/api/admin/:id" path with
        | Some (WithParam (_, rest)) ->
            let parts = rest.Split('/')
            if parts.Length = 2 then
                let entityName = parts.[0]
                let recordId = parts.[1]
                match findEntity entityName with
                | Some entity ->
                    Some (promise {
                        if not (checkAdmin request env) then return unauthorized ()
                        else return! updateResponse entity recordId request env
                    })
                | None -> None
            else None
        | _ -> None

    // DELETE /api/admin/:type/:id — delete record
    | DELETE path ->
        match matchPath "/api/admin/:id" path with
        | Some (WithParam (_, rest)) ->
            let parts = rest.Split('/')
            if parts.Length = 2 then
                let entityName = parts.[0]
                let recordId = parts.[1]
                match findEntity entityName with
                | Some entity ->
                    Some (promise {
                        if not (checkAdmin request env) then return unauthorized ()
                        else return! deleteResponse entity recordId env
                    })
                | None -> None
            else None
        | _ -> None

    | _ -> None
