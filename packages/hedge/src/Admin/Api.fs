module Admin.Api

open Fable.Core
open Fable.Core.JsInterop
open Fetch
open Thoth.Json
open Hedge.Schema
open Hedge.SchemaCodec

/// Admin type descriptor returned by the server. `Ops` are the operations the CALLER may perform on
/// this resource ("list"/"read"/"create"/"update"/"delete") — the server scopes both the resource
/// list and these per the caller's role, so the client renders only permitted resources + controls.
type AdminType = {
    Name: string
    Schema: TypeSchema
    Ops: string list
}

let private decodeAdminType : Decoder<AdminType> =
    Decode.object (fun get ->
        { Name = get.Required.Field "name" Decode.string
          Schema = get.Required.Field "schema" decodeTypeSchema
          Ops = get.Optional.Field "ops" (Decode.list Decode.string) |> Option.defaultValue [] })

let private decodeTypesResponse : Decoder<AdminType list> =
    Decode.field "types" (Decode.list decodeAdminType)

/// Sub-path this admin is served under, e.g. "/st". Empty when at the root.
[<Emit("window.BASE_PATH || ''")>]
let private basePath : string = jsNative

/// Fetch with admin key header.
let private adminFetch (url: string) (method: HttpMethod) (key: string) (body: string option) : JS.Promise<string> =
    promise {
        let headers = [
            requestHeaders [
                ContentType "application/json"
                Custom ("X-Admin-Key", key)
            ]
            Method method
        ]
        let props =
            match body with
            | Some b -> Body (BodyInit.Case3 b) :: headers
            | None -> headers
        let! response = fetch (basePath + url) props
        return! response.text()
    }

/// GET /api/admin/types — the entity types the caller may access, each with its permitted ops. Now
/// authorization-scoped: sends the admin key (owner) and the same-origin guest cookie (a delegated
/// curator), so the result is the caller's permitted set. A 401 (no acceptable credential) is reported
/// as Error "unauthorized" so the SPA shows its sign-in rather than a decode failure.
let getTypes (key: string) : JS.Promise<Result<AdminType list, string>> =
    promise {
        let props = [
            requestHeaders [ Custom ("X-Admin-Key", key) ]
            Method HttpMethod.GET
        ]
        let! response = fetch (basePath + "/api/admin/types") props
        if response.Status = 401 then return Error "unauthorized"
        else
            let! text = response.text()
            return Decode.fromString decodeTypesResponse text
    }

/// GET /api/admin/:type — list records of a type.
let listRecords (key: string) (typeName: string) : JS.Promise<Result<obj list, string>> =
    promise {
        let! text = adminFetch (sprintf "/api/admin/%s" typeName) HttpMethod.GET key None
        return Decode.fromString (Decode.field "records" (Decode.list Decode.value)) text
    }

/// GET /api/admin/:type/:id — get a single record.
let getRecord (key: string) (typeName: string) (id: string) : JS.Promise<Result<obj, string>> =
    promise {
        let! text = adminFetch (sprintf "/api/admin/%s/%s" typeName id) HttpMethod.GET key None
        return Decode.fromString (Decode.field "record" Decode.value) text
    }

/// POST /api/admin/:type — create a record.
let createRecord (key: string) (typeName: string) (body: string) : JS.Promise<Result<obj, string>> =
    promise {
        let! text = adminFetch (sprintf "/api/admin/%s" typeName) HttpMethod.POST key (Some body)
        return Decode.fromString (Decode.field "record" Decode.value) text
    }

/// PUT /api/admin/:type/:id — update a record.
let updateRecord (key: string) (typeName: string) (id: string) (body: string) : JS.Promise<Result<obj, string>> =
    promise {
        let! text = adminFetch (sprintf "/api/admin/%s/%s" typeName id) HttpMethod.PUT key (Some body)
        return Decode.fromString (Decode.field "record" Decode.value) text
    }

/// DELETE /api/admin/:type/:id — delete a record.
let deleteRecord (key: string) (typeName: string) (id: string) : JS.Promise<Result<bool, string>> =
    promise {
        let! text = adminFetch (sprintf "/api/admin/%s/%s" typeName id) HttpMethod.DELETE key None
        return Decode.fromString (Decode.field "ok" Decode.bool) text
    }
