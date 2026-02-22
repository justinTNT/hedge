module Server.AdminConfig

open Fable.Core
open Fable.Core.JsInterop
open Thoth.Json
open Hedge.Workers
open Hedge.Schema
open Server.Env
open Server.AdminGen

/// An admin-manageable entity. Each entity provides a schema
/// and handler functions for CRUD operations.
type AdminEntity = {
    Name: string
    Schema: TypeSchema
    List: Env -> JS.Promise<string>
    Get: string -> Env -> JS.Promise<string option>
    Update: string -> string -> Env -> JS.Promise<string>
    Delete: string -> Env -> JS.Promise<unit>
}

// ============================================================
// PascalCase → camelCase (for JSON keys)
// ============================================================

let private camelCase (s: string) =
    if s.Length = 0 then s
    else string (System.Char.ToLowerInvariant s.[0]) + s.[1..]

// ============================================================
// PascalCase → snake_case (for DB column names)
// ============================================================

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

let private rowToJson (schema: TypeSchema) (row: obj) : JsonValue =
    let pairs =
        schema.Fields |> List.map (fun field ->
            let col = toSnakeCase field.Name
            let jsonKey = camelCase field.Name
            let v = getProp row col
            let encoded =
                match field.Type with
                | FInt -> if isNull v then Encode.nil else Encode.int (unbox v)
                | FBool -> if isNull v then Encode.nil else Encode.bool (unbox v)
                | FOption _ -> if isNull v then Encode.nil else Encode.string (unbox v)
                | FList FString -> Encode.list []
                | _ -> if isNull v then Encode.nil else Encode.string (unbox v)
            jsonKey, encoded)
    Encode.object pairs

// ============================================================
// Generic CRUD handlers
// ============================================================

let private genericList (table: AdminTable) (env: Env) : JS.Promise<string> =
    promise {
        let! result = env.DB.prepare(table.SelectAll).all()
        let items =
            result.results
            |> Array.map (rowToJson table.Schema)
            |> Array.toList
        return Encode.list items |> Encode.toString 0
    }

let private genericGet (table: AdminTable) (id: string) (env: Env) : JS.Promise<string option> =
    promise {
        let stmt = bind (env.DB.prepare(table.SelectOne)) [| box id |]
        let! result = stmt.all()
        if result.results.Length = 0 then
            return None
        else
            let json = rowToJson table.Schema result.results.[0]
            return Some (Encode.toString 0 json)
    }

let private genericUpdate (table: AdminTable) (id: string) (body: string) (env: Env) : JS.Promise<string> =
    promise {
        match Decode.fromString (Decode.keyValuePairs Decode.value) body with
        | Error err -> return sprintf """{"error":"%s"}""" err
        | Ok pairs ->
            let pairMap = pairs |> Map.ofList
            let args =
                table.MutableFields |> List.map (fun fieldName ->
                    let jsonKey = camelCase fieldName
                    match Map.tryFind jsonKey pairMap with
                    | Some v ->
                        let s = Encode.toString 0 v
                        if s = "null" then jsNull
                        else
                            // Strip quotes from string values
                            let trimmed = s.Trim('"')
                            box trimmed
                    | None -> jsNull)
            let allArgs = args @ [box id] |> List.toArray
            let stmt = bind (env.DB.prepare(table.Update)) allArgs
            let! _ = stmt.run()
            let! result = genericGet table id env
            return result |> Option.defaultValue """{"error":"Not found after update"}"""
    }

let private genericDelete (table: AdminTable) (id: string) (env: Env) : JS.Promise<unit> =
    promise {
        let stmt = bind (env.DB.prepare(table.Delete)) [| box id |]
        let! _ = stmt.run()
        ()
    }

// ============================================================
// Entity registry — built from AdminGen.tables
// ============================================================

let entities : AdminEntity list =
    AdminGen.tables |> List.map (fun table ->
        { Name = table.Name
          Schema = table.Schema
          List = genericList table
          Get = genericGet table
          Update = genericUpdate table
          Delete = genericDelete table })
