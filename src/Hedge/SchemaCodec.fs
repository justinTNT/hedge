module Hedge.SchemaCodec

open Thoth.Json
open Hedge.Schema

// ============================================================
// Encoders
// ============================================================

let rec encodeFieldType (ft: FieldType) : JsonValue =
    match ft with
    | FString -> Encode.string "string"
    | FInt -> Encode.string "int"
    | FBool -> Encode.string "bool"
    | FOption inner -> Encode.object [ "option", encodeFieldType inner ]
    | FList inner -> Encode.object [ "list", encodeFieldType inner ]
    | FRecord name -> Encode.object [ "record", Encode.string name ]

let encodeFieldAttr (fa: FieldAttr) : JsonValue =
    match fa with
    | PrimaryKey -> Encode.string "primaryKey"
    | CreateTimestamp -> Encode.string "createTimestamp"
    | UpdateTimestamp -> Encode.string "updateTimestamp"
    | SoftDelete -> Encode.string "softDelete"
    | ForeignKey table -> Encode.object [ "foreignKey", Encode.string table ]
    | RichContent -> Encode.string "richContent"
    | Link -> Encode.string "link"
    | Required -> Encode.string "required"
    | Trim -> Encode.string "trim"
    | Inject -> Encode.string "inject"
    | MinLength n -> Encode.object [ "minLength", Encode.int n ]
    | MaxLength n -> Encode.object [ "maxLength", Encode.int n ]

let encodeTypeAttr (ta: TypeAttr) : JsonValue =
    match ta with
    | HostAdmin -> Encode.string "hostAdmin"
    | ProjectAdmin -> Encode.string "projectAdmin"

let encodeFieldSchema (fs: FieldSchema) : JsonValue =
    Encode.object [
        "name", Encode.string fs.Name
        "type", encodeFieldType fs.Type
        "attrs", Encode.list (List.map encodeFieldAttr fs.Attrs)
    ]

let encodeTypeSchema (ts: TypeSchema) : JsonValue =
    Encode.object [
        "name", Encode.string ts.Name
        "fields", Encode.list (List.map encodeFieldSchema ts.Fields)
        "attrs", Encode.list (List.map encodeTypeAttr ts.Attrs)
    ]

// ============================================================
// Decoders
// ============================================================

let decodeFieldType : Decoder<FieldType> =
    let rec inner path value =
        let tryString =
            Decode.string |> Decode.andThen (fun s ->
                match s with
                | "string" -> Decode.succeed FString
                | "int" -> Decode.succeed FInt
                | "bool" -> Decode.succeed FBool
                | _ -> Decode.fail (sprintf "Unknown field type: %s" s))
        match tryString path value with
        | Ok _ as result -> result
        | Error _ ->
        match (Decode.field "option" inner |> Decode.map FOption) path value with
        | Ok _ as result -> result
        | Error _ ->
        match (Decode.field "list" inner |> Decode.map FList) path value with
        | Ok _ as result -> result
        | Error _ ->
        (Decode.field "record" Decode.string |> Decode.map FRecord) path value
    inner

let decodeFieldAttr : Decoder<FieldAttr> =
    Decode.oneOf [
        Decode.string |> Decode.andThen (fun s ->
            match s with
            | "primaryKey" -> Decode.succeed PrimaryKey
            | "createTimestamp" -> Decode.succeed CreateTimestamp
            | "updateTimestamp" -> Decode.succeed UpdateTimestamp
            | "softDelete" -> Decode.succeed SoftDelete
            | "richContent" -> Decode.succeed RichContent
            | "link" -> Decode.succeed Link
            | "required" -> Decode.succeed Required
            | "trim" -> Decode.succeed Trim
            | "inject" -> Decode.succeed Inject
            | _ -> Decode.fail (sprintf "Unknown field attr: %s" s))
        Decode.field "foreignKey" Decode.string |> Decode.map ForeignKey
        Decode.field "minLength" Decode.int |> Decode.map MinLength
        Decode.field "maxLength" Decode.int |> Decode.map MaxLength
    ]

let decodeTypeAttr : Decoder<TypeAttr> =
    Decode.string |> Decode.andThen (fun s ->
        match s with
        | "hostAdmin" -> Decode.succeed HostAdmin
        | "projectAdmin" -> Decode.succeed ProjectAdmin
        | _ -> Decode.fail (sprintf "Unknown type attr: %s" s))

let decodeFieldSchema : Decoder<FieldSchema> =
    Decode.object (fun get ->
        { Name = get.Required.Field "name" Decode.string
          Type = get.Required.Field "type" decodeFieldType
          Attrs = get.Required.Field "attrs" (Decode.list decodeFieldAttr) })

let decodeTypeSchema : Decoder<TypeSchema> =
    Decode.object (fun get ->
        { Name = get.Required.Field "name" Decode.string
          Fields = get.Required.Field "fields" (Decode.list decodeFieldSchema)
          Attrs = get.Required.Field "attrs" (Decode.list decodeTypeAttr) })
