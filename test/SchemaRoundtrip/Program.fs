module SchemaRoundtrip.Program

// Exercises the admin schema JSON boundary at RUNTIME (not just build): the admin
// server encodes every registered schema via Hedge.SchemaCodec.encodeTypeSchema
// (Admin.fs /api/admin/types) and the admin client decodes it. A FieldAttr added to
// Schema.FieldAttr without a SchemaCodec case throws at that boundary — exactly the
// EditableDate regression. This asserts every FieldAttr case round-trips (encode ->
// JSON -> decode = itself), catching the encode gap (also caught by FS0025) AND the
// decode gap (which FS0025 does not catch), plus a full TypeSchema carrying EditableDate.

open Hedge.Schema
open Hedge.SchemaCodec
open Thoth.Json

let allAttrs : FieldAttr list =
    [ PrimaryKey; CreateTimestamp; UpdateTimestamp; SoftDelete; EditableDate
      ForeignKey "Item"; RichContent; Link; Unique; Required; Trim; Inject
      MinLength 3; MaxLength 5 ]

[<EntryPoint>]
let main _ =
    let mutable failures = 0
    let fail (m: string) = eprintfn "FAIL: %s" m; failures <- failures + 1

    // 1. Every FieldAttr encodes without throwing and decodes back to itself.
    for a in allAttrs do
        try
            let json = encodeFieldAttr a |> Encode.toString 0
            match Decode.fromString decodeFieldAttr json with
            | Ok back when back = a -> ()
            | Ok back -> fail (sprintf "%A round-tripped to %A (json=%s)" a back json)
            | Error e -> fail (sprintf "%A decode failed: %s (json=%s)" a e json)
        with ex -> fail (sprintf "%A encode threw: %s" a ex.Message)

    // 2. A full TypeSchema with an EditableDate field crosses the boundary intact
    //    (the shape Admin.fs encodeTypeSchema produces for /api/admin/types).
    let schema : TypeSchema =
        { Name = "Item"
          Fields = [ { Name = "Id"; Type = FString; Attrs = [ PrimaryKey ] }
                     { Name = "ArticleDate"; Type = FInt; Attrs = [ EditableDate ] } ]
          Attrs = [] }
    let sjson = encodeTypeSchema schema |> Encode.toString 0
    if not (sjson.Contains "editableDate") then
        fail (sprintf "schema JSON omits editableDate: %s" sjson)
    match Decode.fromString decodeTypeSchema sjson with
    | Ok back when back = schema -> ()
    | Ok back -> fail (sprintf "TypeSchema round-trip mismatch: %A" back)
    | Error e -> fail (sprintf "TypeSchema decode failed: %s" e)

    if failures > 0 then
        eprintfn "schema-roundtrip: %d failure(s)" failures
        1
    else
        printfn "schema-roundtrip: %d FieldAttr cases + TypeSchema(EditableDate) OK" (List.length allAttrs)
        0
