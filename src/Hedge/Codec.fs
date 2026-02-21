module Hedge.Codec

/// Generic runtime codec engine.
///
/// Uses Fable's TypeInfo reflection to auto-generate Thoth.Json
/// encoders and decoders for any record type, including wrapper
/// DU fields (PrimaryKey, RichContent, etc.).
///
/// Public API:
///   encode<'T> value  — encode a record to JsonValue
///   decode<'T> ()     — produce a Decoder<'T> for a record

open Fable.Core
open Thoth.Json

// ============================================================
// JS interop helpers
// ============================================================

/// Property access: obj[name]
[<Emit("$0[$1]")>]
let private getProp (obj: obj) (name: string) : obj = jsNative

/// Unwrap a single-case DU: value.fields[0]
[<Emit("$0.fields[0]")>]
let private unwrapDU (v: obj) : obj = jsNative

/// Null/undefined check (loose equality catches both)
[<Emit("($0 == null)")>]
let private isNull (v: obj) : bool = jsNative

/// Construct a single-case DU: new typeInfo.construct(arg)
/// Fable optimizes single-case DUs — constructor takes just the
/// inner value; the tag is hardcoded to 0 inside.
[<Emit("new ($0.construct)($1)")>]
let private constructDU (typeInfo: System.Type) (arg: obj) : obj = jsNative

/// Construct a record: new typeInfo.construct(...values)
[<Emit("new ($0.construct)(...$1)")>]
let private makeRecord (typeInfo: System.Type) (values: obj array) : obj = jsNative

// ============================================================
// TypeInfo accessors (same pattern as Schema.fs)
// ============================================================

[<Emit("$0.fullname")>]
let private tiFullname (t: System.Type) : string = jsNative

[<Emit("$0.generics || []")>]
let private tiGenerics (t: System.Type) : System.Type array = jsNative

[<Emit("$0.fields ? $0.fields() : []")>]
let private tiFields (t: System.Type) : (string * System.Type) array = jsNative

// ============================================================
// Helpers
// ============================================================

/// PascalCase → camelCase
let private camelCase (s: string) =
    if s.Length = 0 then s
    else string (System.Char.ToLowerInvariant s.[0]) + s.[1..]

/// Classify a wrapper DU type. Returns the base type name
/// ("string" or "int") if it's a known wrapper, None otherwise.
let private wrapperBaseType (fullname: string) (generics: System.Type array) : string option =
    if fullname.Contains("Interface.PrimaryKey") then
        if generics.Length > 0 && (tiFullname generics.[0]).Contains("Int32") then Some "int"
        else Some "string"
    elif fullname.Contains("Interface.MultiTenant") then Some "string"
    elif fullname.Contains("Interface.CreateTimestamp") then Some "int"
    elif fullname.Contains("Interface.UpdateTimestamp") then Some "int"
    elif fullname.Contains("Interface.SoftDelete") then Some "int"
    elif fullname.Contains("Interface.ForeignKey") then Some "string"
    elif fullname.Contains("Interface.RichContent") then Some "string"
    elif fullname.Contains("Interface.Link") then Some "string"
    else None

// ============================================================
// Encoder
// ============================================================

let rec encodeValue (typeInfo: System.Type) (value: obj) : JsonValue =
    let fn = tiFullname typeInfo
    let gs = tiGenerics typeInfo

    match wrapperBaseType fn gs with
    | Some "int" -> Encode.int (unbox (unwrapDU value))
    | Some _ -> Encode.string (unbox (unwrapDU value))
    | None ->
        if fn.Contains("String") then Encode.string (unbox value)
        elif fn.Contains("Int32") then Encode.int (unbox value)
        elif fn.Contains("Boolean") then Encode.bool (unbox value)
        elif fn.Contains("FSharpList") then
            let innerTi = if gs.Length > 0 then gs.[0] else typeInfo
            let items : obj list = unbox value
            items |> List.map (encodeValue innerTi) |> Encode.list
        else
            encodeRecord typeInfo value

and encodeRecord (typeInfo: System.Type) (value: obj) : JsonValue =
    let fs = tiFields typeInfo
    let pairs =
        fs |> Array.map (fun (name, fieldTi) ->
            let jsonName = camelCase name
            let fieldVal = getProp value name
            let fn = tiFullname fieldTi
            let gs = tiGenerics fieldTi

            if fn.Contains("FSharpOption") && gs.Length > 0 then
                let innerTi = gs.[0]
                if isNull fieldVal then
                    jsonName, Encode.nil
                else
                    jsonName, encodeValue innerTi fieldVal
            else
                jsonName, encodeValue fieldTi fieldVal
        )
    Encode.object (Array.toList pairs)

// ============================================================
// Decoder
// ============================================================

let rec decoderFor (typeInfo: System.Type) : Decoder<obj> =
    let fn = tiFullname typeInfo
    let gs = tiGenerics typeInfo

    match wrapperBaseType fn gs with
    | Some "int" ->
        Decode.int |> Decode.map (fun i -> constructDU typeInfo (box i))
    | Some _ ->
        Decode.string |> Decode.map (fun s -> constructDU typeInfo (box s))
    | None ->
        if fn.Contains("String") then Decode.string |> Decode.map box
        elif fn.Contains("Int32") then Decode.int |> Decode.map box
        elif fn.Contains("Boolean") then Decode.bool |> Decode.map box
        elif fn.Contains("FSharpList") then
            let innerTi = if gs.Length > 0 then gs.[0] else typeInfo
            Decode.list (decoderFor innerTi) |> Decode.map box
        else
            decodeRecordObj typeInfo

and decodeRecordObj (typeInfo: System.Type) : Decoder<obj> =
    Decode.object (fun get ->
        let fs = tiFields typeInfo
        let values =
            fs |> Array.map (fun (name, fieldTi) ->
                let jsonName = camelCase name
                decodeFieldValue get jsonName fieldTi
            )
        makeRecord typeInfo values
    )

and decodeFieldValue (get: Decode.IGetters) (jsonName: string) (fieldTi: System.Type) : obj =
    let fn = tiFullname fieldTi
    let gs = tiGenerics fieldTi

    if fn.Contains("FSharpOption") && gs.Length > 0 then
        let innerTi = gs.[0]
        let innerFn = tiFullname innerTi
        let innerGs = tiGenerics innerTi

        match wrapperBaseType innerFn innerGs with
        | Some "int" ->
            let optVal : int option = get.Optional.Field jsonName Decode.int
            box (optVal |> Option.map (fun i -> constructDU innerTi (box i)))
        | Some _ ->
            let optVal : string option = get.Optional.Field jsonName Decode.string
            box (optVal |> Option.map (fun s -> constructDU innerTi (box s)))
        | None ->
            if innerFn.Contains("String") then
                box (get.Optional.Field jsonName Decode.string)
            elif innerFn.Contains("Int32") then
                box (get.Optional.Field jsonName Decode.int)
            elif innerFn.Contains("Boolean") then
                box (get.Optional.Field jsonName Decode.bool)
            elif innerFn.Contains("FSharpList") then
                let innerInnerTi = if innerGs.Length > 0 then innerGs.[0] else innerTi
                let dec = Decode.list (decoderFor innerInnerTi)
                box (get.Optional.Field jsonName dec)
            else
                let dec : Decoder<obj> = decodeRecordObj innerTi
                box (get.Optional.Field jsonName dec)
    else
        let dec = decoderFor fieldTi
        get.Required.Field jsonName dec

// ============================================================
// Public API
// ============================================================

let inline encode (value: 'T) : JsonValue =
    encodeRecord typeof<'T> (box value)

let inline decode<'T> () : Decoder<'T> =
    decodeRecordObj typeof<'T> |> Decode.map unbox<'T>
