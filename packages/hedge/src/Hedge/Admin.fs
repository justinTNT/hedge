module Hedge.Admin

open Fable.Core
open Thoth.Json
open Hedge.Workers
open Hedge.Schema
open Hedge.SchemaCodec
open Hedge.Router

/// An admin-manageable entity: a schema plus CRUD handlers over the app's env.
/// The env is generic so this dispatcher stays app-agnostic — apps build the
/// list (from generated AdminGen tables) in their own AdminConfig.
type AdminEntity<'env> = {
    Name: string
    Schema: TypeSchema
    List: 'env -> JS.Promise<string>
    Get: string -> 'env -> JS.Promise<string option>
    /// None for tables without a real primary key — there'd be nowhere to put
    /// a generated id (see the Insert guard in Gen/Program.fs).
    Create: (string -> 'env -> JS.Promise<string>) option
    Update: string -> string -> 'env -> JS.Promise<string>
    Delete: string -> 'env -> JS.Promise<unit>
}

/// What an app hands the dispatcher: its entities, and how to authorise a
/// request (the app reads its own admin key off its own env).
type AdminConfig<'env> = {
    Entities: AdminEntity<'env> list
    CheckKey: WorkerRequest -> 'env -> bool
}

let private typesResponse (config: AdminConfig<'env>) : WorkerResponse =
    let body =
        Encode.object [
            "types", Encode.list (config.Entities |> List.map (fun e ->
                Encode.object [
                    "name", Encode.string e.Name
                    "schema", encodeTypeSchema e.Schema
                ]))
        ] |> Encode.toString 0
    okJson body

let private listResponse (entity: AdminEntity<'env>) (env: 'env) : JS.Promise<WorkerResponse> =
    promise {
        let! json = entity.List env
        return okJson (sprintf """{"records":%s}""" json)
    }

let private getResponse (entity: AdminEntity<'env>) (id: string) (env: 'env) : JS.Promise<WorkerResponse> =
    promise {
        let! result = entity.Get id env
        match result with
        | None -> return notFound ()
        | Some json -> return okJson (sprintf """{"record":%s}""" json)
    }

let private updateResponse (entity: AdminEntity<'env>) (id: string) (request: WorkerRequest) (env: 'env) : JS.Promise<WorkerResponse> =
    promise {
        let! bodyText = request.text()
        let! json = entity.Update id bodyText env
        return okJson (sprintf """{"record":%s}""" json)
    }

let private createResponse (entity: AdminEntity<'env>) (request: WorkerRequest) (env: 'env) : JS.Promise<WorkerResponse> =
    promise {
        match entity.Create with
        | None ->
            return badRequest (sprintf "%s cannot be created from the admin (no primary key)" entity.Name)
        | Some create ->
            let! bodyText = request.text()
            let! json = create bodyText env
            return okJson (sprintf """{"record":%s}""" json)
    }

let private deleteResponse (entity: AdminEntity<'env>) (id: string) (env: 'env) : JS.Promise<WorkerResponse> =
    promise {
        do! entity.Delete id env
        return okJson """{"ok":true}"""
    }

/// Try to handle an admin route. Returns Some promise if matched, None otherwise.
/// Wire from Worker.fs: `Admin = Some (fun req env route ->
///   Hedge.Admin.handleRequest AdminConfig.adminConfig req (env :?> Env) route)`.
let handleRequest (config: AdminConfig<'env>) (request: WorkerRequest) (env: 'env) (route: Route) : JS.Promise<WorkerResponse> option =
    let findEntity (name: string) = config.Entities |> List.tryFind (fun e -> e.Name = name)
    let authed () = config.CheckKey request env
    match route with
    // GET /api/admin/types — list available schemas
    | GET path when matchPath "/api/admin/types" path = Some (Exact "/api/admin/types") ->
        Some (promise { return typesResponse config })

    // GET /api/admin/:type — list records ; /api/admin/:type/:id — get one
    | GET path ->
        match matchPath "/api/admin/:id" path with
        | Some (WithParam (_, typeName)) ->
            let parts = typeName.Split('/')
            if parts.Length = 1 then
                match findEntity typeName with
                | Some entity ->
                    Some (promise {
                        if not (authed ()) then return unauthorized ()
                        else return! listResponse entity env
                    })
                | None -> None
            elif parts.Length = 2 then
                match findEntity parts.[0] with
                | Some entity ->
                    Some (promise {
                        if not (authed ()) then return unauthorized ()
                        else return! getResponse entity parts.[1] env
                    })
                | None -> None
            else None
        | _ -> None

    // POST /api/admin/:type — create a record
    | POST path ->
        match matchPath "/api/admin/:id" path with
        | Some (WithParam (_, entityName)) when not (entityName.Contains "/") ->
            match findEntity entityName with
            | Some entity ->
                Some (promise {
                    if not (authed ()) then return unauthorized ()
                    else return! createResponse entity request env
                })
            | None -> None
        | _ -> None

    // PUT /api/admin/:type/:id — update record
    | PUT path ->
        match matchPath "/api/admin/:id" path with
        | Some (WithParam (_, rest)) ->
            let parts = rest.Split('/')
            if parts.Length = 2 then
                match findEntity parts.[0] with
                | Some entity ->
                    Some (promise {
                        if not (authed ()) then return unauthorized ()
                        else return! updateResponse entity parts.[1] request env
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
                match findEntity parts.[0] with
                | Some entity ->
                    Some (promise {
                        if not (authed ()) then return unauthorized ()
                        else return! deleteResponse entity parts.[1] env
                    })
                | None -> None
            else None
        | _ -> None

    | _ -> None
