module Admin.Api

open Fable.Core
open Fable.Core.JsInterop
open Fetch
open Thoth.Json
open Hedge.Schema
open Hedge.SchemaCodec

/// Admin type descriptor returned by the server.
type AdminType = {
    Name: string
    Schema: TypeSchema
}

let private decodeAdminType : Decoder<AdminType> =
    Decode.object (fun get ->
        { Name = get.Required.Field "name" Decode.string
          Schema = get.Required.Field "schema" decodeTypeSchema })

let private decodeTypesResponse : Decoder<AdminType list> =
    Decode.field "types" (Decode.list decodeAdminType)

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
        let! response = fetch url props
        return! response.text()
    }

/// Simple GET (no auth needed for types endpoint).
let private fetchJson (url: string) : JS.Promise<string> =
    promise {
        let! response = fetch url []
        return! response.text()
    }

/// GET /api/admin/types — list available entity types and their schemas.
let getTypes () : JS.Promise<Result<AdminType list, string>> =
    promise {
        let! text = fetchJson "/api/admin/types"
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
