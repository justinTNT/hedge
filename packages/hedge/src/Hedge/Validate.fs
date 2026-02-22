module Hedge.Validate

/// Generic validation engine.
///
/// Uses TypeInfo reflection + a TypeSchema to validate and sanitize
/// record values at runtime. Same Emit interop pattern as Codec.fs.

open Fable.Core
open Hedge.Schema

// ============================================================
// JS interop helpers (duplicated — F# modules can't share privates)
// ============================================================

[<Emit("$0[$1]")>]
let private getProp (obj: obj) (name: string) : obj = jsNative

[<Emit("($0 == null)")>]
let private isNull (v: obj) : bool = jsNative

[<Emit("new ($0.construct)(...$1)")>]
let private makeRecord (typeInfo: System.Type) (values: obj array) : obj = jsNative

[<Emit("$0.fields ? $0.fields() : []")>]
let private tiFields (t: System.Type) : (string * System.Type) array = jsNative

// ============================================================
// Error type
// ============================================================

type ValidationError = { Field: string; Message: string }

// ============================================================
// Field validation
// ============================================================

let private hasAttr attr (fs: FieldSchema) =
    fs.Attrs |> List.contains attr

let private getMinLength (fs: FieldSchema) =
    fs.Attrs |> List.tryPick (function MinLength n -> Some n | _ -> None)

let private getMaxLength (fs: FieldSchema) =
    fs.Attrs |> List.tryPick (function MaxLength n -> Some n | _ -> None)

/// Validate and sanitize a single field. Returns (sanitizedValue, errors).
let private validateField (fieldName: string) (fs: FieldSchema) (value: obj) : obj * ValidationError list =
    let errors = ResizeArray<ValidationError>()
    let addError msg = errors.Add({ Field = fieldName; Message = msg })

    match fs.Type with
    | FString ->
        let mutable s : string = unbox value
        if hasAttr Trim fs then s <- s.Trim()
        if hasAttr Required fs && s.Length = 0 then
            addError "is required"
        match getMinLength fs with
        | Some n when s.Length < n -> addError (sprintf "must be at least %d characters" n)
        | _ -> ()
        match getMaxLength fs with
        | Some n when s.Length > n -> addError (sprintf "must be at most %d characters" n)
        | _ -> ()
        box s, Seq.toList errors

    | FOption FString ->
        if isNull value then
            value, Seq.toList errors
        else
            let mutable s : string = unbox value
            if hasAttr Trim fs then s <- s.Trim()
            match getMinLength fs with
            | Some n when s.Length < n -> addError (sprintf "must be at least %d characters" n)
            | _ -> ()
            match getMaxLength fs with
            | Some n when s.Length > n -> addError (sprintf "must be at most %d characters" n)
            | _ -> ()
            box s, Seq.toList errors

    | FList _ ->
        let items : obj list = unbox value
        let count = List.length items
        if hasAttr Required fs && count = 0 then
            addError "is required"
        match getMaxLength fs with
        | Some n when count > n -> addError (sprintf "must have at most %d items" n)
        | _ -> ()
        value, Seq.toList errors

    | _ -> value, []

// ============================================================
// Record validation (public for inline access — FS1113)
// ============================================================

/// Walk a record's fields, validate against a TypeSchema, and
/// return either a sanitized record or a list of errors.
let validateRecord (typeInfo: System.Type) (ts: TypeSchema) (value: obj) : Result<obj, ValidationError list> =
    let fieldSchemaMap =
        ts.Fields |> List.map (fun f -> f.Name, f) |> Map.ofList
    let recordFields = tiFields typeInfo
    let allErrors = ResizeArray<ValidationError>()
    let values =
        recordFields |> Array.map (fun (name, _fieldTi) ->
            let rawValue = getProp value name
            match Map.tryFind name fieldSchemaMap with
            | Some fs ->
                let sanitized, errors = validateField name fs rawValue
                allErrors.AddRange(errors)
                sanitized
            | None -> rawValue
        )
    let errorList = Seq.toList allErrors
    if errorList.IsEmpty then
        Ok (makeRecord typeInfo values)
    else
        Error errorList

// ============================================================
// Public API
// ============================================================

/// Validate and sanitize a record value against a TypeSchema.
/// Returns Ok with sanitized record, or Error with validation errors.
let inline validate<'T> (ts: TypeSchema) (value: 'T) : Result<'T, ValidationError list> =
    validateRecord typeof<'T> ts (box value) |> Result.map unbox<'T>
