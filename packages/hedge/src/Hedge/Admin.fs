module Hedge.Admin

open Fable.Core
open Fable.Core.JsInterop
open Thoth.Json
open Hedge.Workers
open Hedge.Schema
open Hedge.SchemaCodec
open Hedge.Router

/// A generated admin table descriptor. Gen emits values of this type into each
/// app's Server/generated/AdminGen.fs; the schema-driven CRUD below runs off it,
/// so there is ONE copy of the handlers (here) instead of one per app.
type AdminTable = {
    Name: string
    Table: string
    Schema: TypeSchema
    SelectAll: string
    SelectOne: string
    Insert: string
    HasCreateTs: bool
    HasUpdateTs: bool
    Update: string
    Delete: string
    MutableFields: string list
}

/// An admin operation on a resource — what an authorization decision keys on, alongside the table
/// name. `List` = the collection read (`GET /api/admin/:type`); the rest are the obvious CRUD.
type AdminOp =
    | OpList
    | OpRead
    | OpCreate
    | OpUpdate
    | OpDelete

/// The identity behind an admin request, resolved ONCE per request (subject + grant lookup are async);
/// the per-(resource, operation) decision is then the sync `permits` predicate. Admin-owned (the app
/// maps its ADMIN_KEY / Hedge.AccessControl.RoleResult onto it) so the admin need not depend on the
/// access-control capability. The optional cookie is a guest-session renewal to echo on responses.
type AdminAccess =
    /// The owner (a matching ADMIN_KEY): permits every resource and operation.
    | AdminOwner
    /// An authenticated subject; `permits resource op` is the app's permission matrix (an unlisted
    /// resource/op is denied → 403). `setCookie` is an optional guest-cookie renewal.
    | AdminSubject of permits: (string -> AdminOp -> bool) * setCookie: string option
    /// No acceptable session — every operation is 401. `setCookie` carries any renewal.
    | AdminAnonymous of setCookie: string option

/// What an app hands the dispatcher: its generated tables, how to reach the D1 database from its env,
/// and how to authorize a request. `Authorize` runs once per admin request and yields who is asking;
/// the dispatcher enforces per (resource, operation) and echoes any renewal cookie. Owner-key-only
/// apps use `Hedge.Admin.ownerKey` to keep their pre-authorization behaviour.
type AdminConfig<'env> = {
    Tables: AdminTable list
    GetDb: 'env -> D1Database
    Authorize: WorkerRequest -> 'env -> JS.Promise<AdminAccess>
}

/// Adapter for owner-key-only deployments: full access on a matching key, else 401 — identical to the
/// pre-authorization admin. Apps that have not adopted delegated roles wire `Authorize = ownerKey f`.
let ownerKey (checkKey: WorkerRequest -> 'env -> bool) : WorkerRequest -> 'env -> JS.Promise<AdminAccess> =
    fun request env -> promise { return (if checkKey request env then AdminOwner else AdminAnonymous None) }

// ============================================================
// PascalCase → camelCase (JSON keys) / snake_case (DB columns)
// ============================================================

let private camelCase (s: string) =
    if s.Length = 0 then s
    else string (System.Char.ToLowerInvariant s.[0]) + s.[1..]

let private toSnakeCase (s: string) =
    s.ToCharArray()
    |> Array.mapi (fun i c ->
        if i > 0 && System.Char.IsUpper c then
            sprintf "_%c" (System.Char.ToLower c)
        else
            string (System.Char.ToLower c))
    |> String.concat ""

// ============================================================
// Row → JSON (generic, driven by schema)
// ============================================================

/// Coerce a DB value to a bool across representations (0/1, "0"/"1", "true"/"false").
/// Admin writes booleans as 0/1 ints (see mutableArgs); legacy rows may hold text,
/// and an erased F# cast of the raw column doesn't reliably yield a JS boolean.
let private coerceBool (v: obj) : bool =
    let s = (string v).ToLowerInvariant()
    not (s = "0" || s = "false" || s = "" || s = "null")

let private rowToJson (schema: TypeSchema) (row: obj) : JsonValue =
    let pairs =
        schema.Fields |> List.map (fun field ->
            let col = toSnakeCase field.Name
            let jsonKey = camelCase field.Name
            let v = getProp row col
            let encoded =
                match field.Type with
                | FInt -> if isNull v then Encode.nil else Encode.int (unbox v)
                | FBool -> if isNull v then Encode.nil else Encode.bool (coerceBool v)
                | FOption FInt -> if isNull v then Encode.nil else Encode.int (unbox v)
                | FOption FBool -> if isNull v then Encode.nil else Encode.bool (coerceBool v)
                | FOption _ -> if isNull v then Encode.nil else Encode.string (unbox v)
                | FList FString -> Encode.list []
                | _ -> if isNull v then Encode.nil else Encode.string (unbox v)
            jsonKey, encoded)
    Encode.object pairs

// ============================================================
// Generic CRUD handlers (over a D1Database + a generated AdminTable)
// ============================================================

let private genericList (db: D1Database) (table: AdminTable) : JS.Promise<string> =
    promise {
        let! result = db.prepare(table.SelectAll).all()
        let items =
            result.results
            |> Array.map (rowToJson table.Schema)
            |> Array.toList
        return Encode.list items |> Encode.toString 0
    }

let private genericGet (db: D1Database) (table: AdminTable) (id: string) : JS.Promise<string option> =
    promise {
        let stmt = bind (db.prepare(table.SelectOne)) [| box id |]
        let! result = stmt.all()
        if result.results.Length = 0 then
            return None
        else
            let json = rowToJson table.Schema result.results.[0]
            return Some (Encode.toString 0 json)
    }

/// The mutable-field values from a decoded body, in MutableFields order —
/// shared by update and create so both bind columns the same way. Conversion is
/// field-type-aware (option-transparent): booleans bind as 0/1 and ints as ints,
/// otherwise the JSON text ("false") lands in an INTEGER column and reads back as
/// true. Everything else binds as text (or NULL).
let private mutableArgs (table: AdminTable) (pairMap: Map<string, JsonValue>) =
    let rec strip t = match t with FOption inner -> strip inner | _ -> t
    let baseType name =
        table.Schema.Fields
        |> List.tryPick (fun f -> if f.Name = name then Some f.Type else None)
        |> Option.map strip
    table.MutableFields |> List.map (fun fieldName ->
        let jsonKey = camelCase fieldName
        match Map.tryFind jsonKey pairMap with
        | None -> jsNull
        | Some v ->
            match baseType fieldName with
            | Some FBool ->
                match Decode.fromValue "" Decode.bool v with
                | Ok b -> box (if b then 1 else 0)
                | _ ->
                    match Decode.fromValue "" Decode.string v with
                    | Ok s -> let s = s.ToLowerInvariant() in box (if s = "true" || s = "1" then 1 else 0)
                    | _ -> jsNull
            | Some FInt ->
                match Decode.fromValue "" Decode.int v with
                | Ok i -> box i
                | _ -> jsNull
            | _ ->
                match Decode.fromValue "" Decode.string v with
                | Ok s -> box s
                | _ ->
                    let s = Encode.toString 0 v
                    if s = "null" then jsNull else box s)

let private genericCreate (db: D1Database) (table: AdminTable) (body: string) : JS.Promise<string> =
    promise {
        match Decode.fromString (Decode.keyValuePairs Decode.value) body with
        | Error err -> return sprintf """{"error":"%s"}""" err
        | Ok pairs ->
            let id = newId ()
            let now = epochNow ()
            // Column order matches the generated INSERT: pk, mutables, created_at
            let allArgs =
                [ box id ] @ mutableArgs table (Map.ofList pairs)
                @ (if table.HasCreateTs then [ box now ] else [])
                |> List.toArray
            let stmt = bind (db.prepare(table.Insert)) allArgs
            let! _ = stmt.run()
            let! result = genericGet db table id
            return result |> Option.defaultValue """{"error":"Not found after create"}"""
    }

let private genericUpdate (db: D1Database) (table: AdminTable) (id: string) (body: string) : JS.Promise<string> =
    promise {
        match Decode.fromString (Decode.keyValuePairs Decode.value) body with
        | Error err -> return sprintf """{"error":"%s"}""" err
        | Ok pairs ->
            let args = mutableArgs table (Map.ofList pairs)
            // Matches the generated SET clause: mutables, then updated_at
            let allArgs =
                args
                @ (if table.HasUpdateTs then [ box (epochNow ()) ] else [])
                @ [ box id ]
                |> List.toArray
            let stmt = bind (db.prepare(table.Update)) allArgs
            let! _ = stmt.run()
            let! result = genericGet db table id
            return result |> Option.defaultValue """{"error":"Not found after update"}"""
    }

let private genericDelete (db: D1Database) (table: AdminTable) (id: string) : JS.Promise<unit> =
    promise {
        let stmt = bind (db.prepare(table.Delete)) [| box id |]
        let! _ = stmt.run()
        ()
    }

// ============================================================
// Response wrappers + route dispatch
// ============================================================

let private typesResponse (config: AdminConfig<'env>) : WorkerResponse =
    let body =
        Encode.object [
            "types", Encode.list (config.Tables |> List.map (fun t ->
                Encode.object [
                    "name", Encode.string t.Name
                    "schema", encodeTypeSchema t.Schema
                ]))
        ] |> Encode.toString 0
    okJson body

/// Wrap a 200 body, echoing an optional guest-session renewal cookie (delegated admin sessions).
let private ok (cookie: string option) (body: string) : WorkerResponse =
    match cookie with Some c -> okJsonWithCookie body c | None -> okJson body

let private listResponse (db: D1Database) (table: AdminTable) (cookie: string option) : JS.Promise<WorkerResponse> =
    promise {
        let! json = genericList db table
        return ok cookie (sprintf """{"records":%s}""" json)
    }

let private getResponse (db: D1Database) (table: AdminTable) (id: string) (cookie: string option) : JS.Promise<WorkerResponse> =
    promise {
        let! result = genericGet db table id
        match result with
        | None -> return notFound ()
        | Some json -> return ok cookie (sprintf """{"record":%s}""" json)
    }

let private createResponse (db: D1Database) (table: AdminTable) (request: WorkerRequest) (cookie: string option) : JS.Promise<WorkerResponse> =
    promise {
        if table.Insert = "" then
            return badRequest (sprintf "%s cannot be created from the admin (no primary key)" table.Name)
        else
            let! bodyText = request.text()
            let! json = genericCreate db table bodyText
            return ok cookie (sprintf """{"record":%s}""" json)
    }

let private updateResponse (db: D1Database) (table: AdminTable) (id: string) (request: WorkerRequest) (cookie: string option) : JS.Promise<WorkerResponse> =
    promise {
        let! bodyText = request.text()
        let! json = genericUpdate db table id bodyText
        return ok cookie (sprintf """{"record":%s}""" json)
    }

let private deleteResponse (db: D1Database) (table: AdminTable) (id: string) (cookie: string option) : JS.Promise<WorkerResponse> =
    promise {
        do! genericDelete db table id
        return ok cookie """{"ok":true}"""
    }

/// Try to handle an admin route. Returns Some promise if matched, None otherwise.
/// Wire from Worker.fs: `Admin = Some (fun req env route ->
///   Hedge.Admin.handleRequest AdminConfig.adminConfig req (env :?> Env) route)`.
let handleRequest (config: AdminConfig<'env>) (request: WorkerRequest) (env: 'env) (route: Route) : JS.Promise<WorkerResponse> option =
    let db = config.GetDb env
    let findTable (name: string) = config.Tables |> List.tryFind (fun t -> t.Name = name)
    // Resolve who is asking ONCE, then enforce the requested (resource, operation): owner acts on
    // everything; a subject acts only where its permits matrix allows (else 403); no session is 401.
    // Any guest-session renewal cookie rides through to the op's response (or the denial). The subject
    // + grant lookup are async, so this runs inside the matched route's promise, once per request.
    let gated (resource: string) (op: AdminOp) (act: string option -> JS.Promise<WorkerResponse>) : JS.Promise<WorkerResponse> =
        promise {
            let! access = config.Authorize request env
            match access with
            | AdminOwner -> return! act None
            | AdminSubject (permits, cookie) ->
                if permits resource op then return! act cookie
                else return (match cookie with Some c -> jsonResponseWithCookie """{"error":"Forbidden"}""" 403 c | None -> forbidden ())
            | AdminAnonymous cookie ->
                return (match cookie with Some c -> jsonResponseWithCookie """{"error":"Unauthorized"}""" 401 c | None -> unauthorized ())
        }
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
                match findTable typeName with
                | Some table -> Some (gated table.Name OpList (fun c -> listResponse db table c))
                | None -> None
            elif parts.Length = 2 then
                match findTable parts.[0] with
                | Some table -> Some (gated table.Name OpRead (fun c -> getResponse db table parts.[1] c))
                | None -> None
            else None
        | _ -> None

    // POST /api/admin/:type — create a record
    | POST path ->
        match matchPath "/api/admin/:id" path with
        | Some (WithParam (_, entityName)) when not (entityName.Contains "/") ->
            match findTable entityName with
            | Some table -> Some (gated table.Name OpCreate (fun c -> createResponse db table request c))
            | None -> None
        | _ -> None

    // PUT /api/admin/:type/:id — update record
    | PUT path ->
        match matchPath "/api/admin/:id" path with
        | Some (WithParam (_, rest)) ->
            let parts = rest.Split('/')
            if parts.Length = 2 then
                match findTable parts.[0] with
                | Some table -> Some (gated table.Name OpUpdate (fun c -> updateResponse db table parts.[1] request c))
                | None -> None
            else None
        | _ -> None

    // DELETE /api/admin/:type/:id — delete record
    | DELETE path ->
        match matchPath "/api/admin/:id" path with
        | Some (WithParam (_, rest)) ->
            let parts = rest.Split('/')
            if parts.Length = 2 then
                match findTable parts.[0] with
                | Some table -> Some (gated table.Name OpDelete (fun c -> deleteResponse db table parts.[1] c))
                | None -> None
            else None
        | _ -> None

    | _ -> None
