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

// A request record with wrapper-typed ids (Hedge.Interface.ForeignKey), the shape the
// wire-id typing pass produces. The generated validation schema types such ids as FString
// + Trim, but at runtime they are wrapper objects, not strings — see the validate probe.
type private CommentReqProbe =
    { PostId: Hedge.Interface.ForeignKey<obj>
      ParentId: Hedge.Interface.ForeignKey<obj> option
      Content: string
      Author: string option }

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

    // 3. Runtime validation must not crash on wrapper-typed ids. The wire-id typing pass
    //    makes request ids Hedge.Interface.ForeignKey (a JS object), but the generated
    //    validation schema types them FString + Trim. validate must skip string sanitizing
    //    the wrapper (not throw "s.trim is not a function" — the live comment-submit 500),
    //    while still trimming the real string fields.
    let vschema : TypeSchema =
        { Name = "CommentReqProbe"
          Fields = [ { Name = "PostId"; Type = FString; Attrs = [ Required; Trim ] }
                     { Name = "ParentId"; Type = FOption FString; Attrs = [ Trim ] }
                     { Name = "Content"; Type = FString; Attrs = [ Required; Trim ] }
                     { Name = "Author"; Type = FOption FString; Attrs = [ Trim ] } ]
          Attrs = [] }
    let vreq =
        { PostId = Hedge.Interface.ForeignKey "p1"
          ParentId = Some (Hedge.Interface.ForeignKey "c1")
          Content = "  hi  "
          Author = Some "  bob " }
    (try
        match Hedge.Validate.validate vschema vreq with
        | Ok r when r.Content = "hi" && r.Author = Some "bob" -> ()   // strings trimmed, ids untouched
        | Ok r -> fail (sprintf "validate wrapper-id probe: unexpected sanitized value %A" r)
        | Error e -> fail (sprintf "validate wrapper-id probe errored: %A" e)
     with ex -> fail (sprintf "validate wrapper-id probe threw: %s" ex.Message))

    if failures > 0 then
        eprintfn "schema-roundtrip: %d failure(s)" failures
        1
    else
        printfn "schema-roundtrip: %d FieldAttr cases + TypeSchema(EditableDate) OK" (List.length allAttrs)
        0
