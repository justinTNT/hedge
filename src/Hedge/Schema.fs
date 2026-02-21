module Hedge.Schema

/// Schema reflection engine.
///
/// Derives metadata from wrapper types (PrimaryKey, MultiTenant, etc.)
/// at runtime via Fable's TypeInfo. Each record field's TypeInfo carries
/// its full type name — so `PrimaryKey<string>` shows up as
/// "Hedge.Interface.PrimaryKey`1" and we can classify it.
///
/// This is the key win of the wrapper type approach: types ARE the schema,
/// and Fable preserves them in its reflection output.

open Fable.Core
open Fable.Core.JsInterop

// ============================================================
// Metadata types
// ============================================================

type FieldAttr =
    | PrimaryKey
    | CreateTimestamp
    | UpdateTimestamp
    | MultiTenant
    | SoftDelete
    | ForeignKey of table: string
    | RichContent
    | Link
    | Required
    | Trim
    | Inject
    | MinLength of int
    | MaxLength of int

type TypeAttr =
    | HostAdmin
    | ProjectAdmin

type FieldType =
    | FString
    | FInt
    | FBool
    | FOption of FieldType
    | FList of FieldType
    | FRecord of name: string

type FieldSchema = {
    Name: string
    Type: FieldType
    Attrs: FieldAttr list
}

type TypeSchema = {
    Name: string
    Fields: FieldSchema list
    Attrs: TypeAttr list
}

// ============================================================
// Display helpers
// ============================================================

let showAttr = function
    | PrimaryKey -> "PrimaryKey"
    | CreateTimestamp -> "CreateTimestamp"
    | UpdateTimestamp -> "UpdateTimestamp"
    | MultiTenant -> "MultiTenant"
    | SoftDelete -> "SoftDelete"
    | ForeignKey t -> sprintf "ForeignKey(%s)" t
    | RichContent -> "RichContent"
    | Link -> "Link"
    | Required -> "Required"
    | Trim -> "Trim"
    | Inject -> "Inject"
    | MinLength n -> sprintf "MinLength(%d)" n
    | MaxLength n -> sprintf "MaxLength(%d)" n

let rec showFieldType = function
    | FString -> "string"
    | FInt -> "int"
    | FBool -> "bool"
    | FOption t -> sprintf "%s option" (showFieldType t)
    | FList t -> sprintf "%s list" (showFieldType t)
    | FRecord n -> n

// ============================================================
// TypeInfo interop — access Fable's reflection data
// ============================================================

/// Get the fullname from a Fable TypeInfo object.
[<Emit("$0.fullname")>]
let private tiFullname (t: System.Type) : string = jsNative

/// Get generics array from a Fable TypeInfo object.
[<Emit("$0.generics || []")>]
let private tiGenerics (t: System.Type) : System.Type array = jsNative

/// Get record fields from a Fable TypeInfo object.
/// Returns array of [name, typeInfo] tuples.
[<Emit("$0.fields ? $0.fields() : []")>]
let private tiFields (t: System.Type) : (string * System.Type) array = jsNative

// ============================================================
// Reflection engine
// ============================================================

/// Classify a TypeInfo into a FieldType and optional FieldAttr.
let rec private classifyType (t: System.Type) : FieldType * FieldAttr option =
    let fn = tiFullname t
    let gs = tiGenerics t

    // Check for wrapper types by fullname
    if fn.Contains("Interface.PrimaryKey") then
        // PrimaryKey<'a> — inner type determines FieldType
        let innerType =
            if gs.Length > 0 then
                let g = tiFullname gs.[0]
                if g.Contains("Int32") then FInt else FString
            else FString
        innerType, Some PrimaryKey

    elif fn.Contains("Interface.MultiTenant") then
        FString, Some MultiTenant

    elif fn.Contains("Interface.CreateTimestamp") then
        FInt, Some CreateTimestamp

    elif fn.Contains("Interface.UpdateTimestamp") then
        FInt, Some UpdateTimestamp

    elif fn.Contains("Interface.SoftDelete") then
        FInt, Some SoftDelete

    elif fn.Contains("Interface.ForeignKey") then
        let table =
            if gs.Length > 0 then
                let g = tiFullname gs.[0]
                // Extract short name from e.g. "Models.Domain.MicroblogItem"
                let i = g.LastIndexOf(".")
                if i >= 0 then g.Substring(i + 1) else g
            else "?"
        FString, Some (ForeignKey table)

    elif fn.Contains("Interface.RichContent") then
        FString, Some RichContent

    elif fn.Contains("Interface.Link") then
        FString, Some Link

    // Standard types
    elif fn.Contains("String") then
        FString, None

    elif fn.Contains("Int32") then
        FInt, None

    elif fn.Contains("Boolean") then
        FBool, None

    elif fn.Contains("FSharpList") then
        let inner =
            if gs.Length > 0 then fst (classifyType gs.[0])
            else FString
        FList inner, None

    else
        // Unknown — treat as record reference
        let shortName =
            let i = fn.LastIndexOf(".")
            if i >= 0 then fn.Substring(i + 1) else fn
        FRecord shortName, None

/// Classify a single record field, handling option wrapping.
let private classifyField (name: string) (t: System.Type) : FieldSchema =
    let fn = tiFullname t
    let gs = tiGenerics t

    // Peel option wrapper
    if fn.Contains("FSharpOption") && gs.Length > 0 then
        let innerType, attr = classifyType gs.[0]
        { Name = name
          Type = FOption innerType
          Attrs = attr |> Option.toList }
    else
        let fieldType, attr = classifyType t
        { Name = name
          Type = fieldType
          Attrs = attr |> Option.toList }

/// Derive a TypeSchema from a record type's reflection data.
let deriveSchema (name: string) (t: System.Type) : TypeSchema =
    let fs = tiFields t
    { Name = name
      Fields = fs |> Array.map (fun (n, ft) -> classifyField n ft) |> Array.toList
      Attrs = [] }

// ============================================================
// Builder DSL — for manual schema definitions (validation, etc.)
// ============================================================

/// Define a field with no attributes.
let field name ftype : FieldSchema =
    { Name = name; Type = ftype; Attrs = [] }

/// Define a field with attributes.
let fieldWith name ftype attrs : FieldSchema =
    { Name = name; Type = ftype; Attrs = attrs }

/// Define a schema for a type.
let schema name fields : TypeSchema =
    { Name = name; Fields = fields; Attrs = [] }

/// Define a schema with type-level attributes.
let schemaWith name attrs fields : TypeSchema =
    { Name = name; Fields = fields; Attrs = attrs }
