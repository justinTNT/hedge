module Gen.Program

open System
open System.IO
open System.Reflection
open Microsoft.FSharp.Reflection
open Hedge.Interface
open Hedge.Schema

// ============================================================
// Helpers
// ============================================================

let toSnakeCase (s: string) =
    s.ToCharArray()
    |> Array.mapi (fun i c ->
        if i > 0 && Char.IsUpper c then
            sprintf "_%c" (Char.ToLower c)
        else
            string (Char.ToLower c))
    |> String.concat ""

let toCamelCase (s: string) =
    (Char.ToLower s.[0] |> string) + s.[1..]

let pluralize (s: string) =
    if s.EndsWith("s") then s + "es"
    elif s.EndsWith("y") then s.[..s.Length-2] + "ies"
    else s + "s"

let isSkippedField (f: FieldSchema) =
    match f.Type with
    | FList _ -> true
    | _ -> false

let isPrimaryKey (f: FieldSchema) =
    f.Attrs |> List.contains PrimaryKey

let isAutoManaged (f: FieldSchema) =
    f.Attrs |> List.exists (fun a ->
        match a with
        | PrimaryKey | CreateTimestamp | UpdateTimestamp | SoftDelete -> true
        | _ -> false)

let isForeignKey (f: FieldSchema) =
    f.Attrs |> List.exists (fun a ->
        match a with
        | ForeignKey _ -> true
        | _ -> false)

// ============================================================
// Reflection-based type discovery (Step 3)
// ============================================================

let private modelsAssembly =
    lazy (Assembly.Load("Models"))

let rec classifyFieldType (propType: Type) : FieldType * FieldAttr list =
    if propType.IsGenericType then
        let def = propType.GetGenericTypeDefinition()
        if def = typedefof<PrimaryKey<_>> then
            let inner = propType.GenericTypeArguments.[0]
            (if inner = typeof<int> then FInt else FString), [PrimaryKey]
        elif def = typedefof<ForeignKey<_>> then
            let refType = propType.GenericTypeArguments.[0]
            FString, [ForeignKey refType.Name]
        elif def = typedefof<option<_>> then
            let inner, attrs = classifyFieldType propType.GenericTypeArguments.[0]
            FOption inner, attrs
        elif def = typedefof<list<_>> then
            let inner, _ = classifyFieldType propType.GenericTypeArguments.[0]
            FList inner, []
        else FString, []
    elif propType = typeof<CreateTimestamp> then FInt, [CreateTimestamp]
    elif propType = typeof<UpdateTimestamp> then FInt, [UpdateTimestamp]
    elif propType = typeof<SoftDelete> then FInt, [SoftDelete]
    elif propType = typeof<RichContent> then FString, [RichContent]
    elif propType = typeof<Link> then FString, [Link]
    elif propType = typeof<string> then FString, []
    elif propType = typeof<int> then FInt, []
    elif propType = typeof<bool> then FBool, []
    else FRecord propType.Name, []

let getFieldSchemas (recordType: Type) : FieldSchema list =
    FSharpType.GetRecordFields(recordType)
    |> Array.map (fun prop ->
        let ft, attrs = classifyFieldType prop.PropertyType
        { Name = prop.Name; Type = ft; Attrs = attrs })
    |> Array.toList

/// Discover all record types in Models.Domain module
let discoverDomainTypes () : Type list =
    let assembly = modelsAssembly.Value
    assembly.GetTypes()
    |> Array.filter (fun t ->
        t.FullName.StartsWith("Models.Domain+")
        && FSharpType.IsRecord(t, BindingFlags.Public ||| BindingFlags.Instance))
    |> Array.toList

/// Discover all WS event types in Models.Ws module
let discoverWsTypes () : Type list =
    let assembly = modelsAssembly.Value
    assembly.GetTypes()
    |> Array.filter (fun t ->
        t.FullName.StartsWith("Models.Ws+")
        && FSharpType.IsRecord(t, BindingFlags.Public ||| BindingFlags.Instance))
    |> Array.toList

// ============================================================
// API endpoint discovery
// ============================================================

type EndpointMethod = EGet | EGetOne | EPost

type ParsedEndpoint = {
    ModuleName: string
    Method: EndpointMethod
    Path: string
    RequestType: Type option
    ResponseType: Type option
    ViewTypes: Type list
}

let discoverApiModules () : ParsedEndpoint list =
    let assembly = modelsAssembly.Value
    // Api modules are nested types under Models.Api
    let apiType =
        assembly.GetTypes()
        |> Array.tryFind (fun t -> t.FullName = "Models.Api")
    match apiType with
    | None -> []
    | Some apiParent ->
        let nestedModules = apiParent.GetNestedTypes(BindingFlags.Public ||| BindingFlags.Static)
        nestedModules
        |> Array.choose (fun moduleType ->
            let endpointProp = moduleType.GetProperty("endpoint", BindingFlags.Public ||| BindingFlags.Static)
            if endpointProp = null then None
            else
                let endpointValue = endpointProp.GetValue(null)
                let endpointType = endpointProp.PropertyType

                let typeDef =
                    if endpointType.IsGenericType then endpointType.GetGenericTypeDefinition()
                    else endpointType

                // Extract the path string from the DU value
                let fields = FSharpValue.GetUnionFields(endpointValue, endpointType) |> snd

                let method, path =
                    if typeDef = typedefof<Get<_>> then
                        let respArg = endpointType.GenericTypeArguments.[0]
                        // Skip Get<unit> (WebSocket upgrade endpoint)
                        if respArg = typeof<unit> then
                            EGet, ""
                        else
                            EGet, fields.[0] :?> string
                    elif typeDef = typedefof<Post<_,_>> then
                        EPost, fields.[0] :?> string
                    elif typeDef = typedefof<GetOne<_>> then
                        // GetOne contains a function string -> string
                        let func = fields.[0] :?> (string -> string)
                        EGetOne, func ":id"
                    else
                        EGet, ""

                // Skip websocket endpoints (Get<unit>)
                if path = "" then None
                else

                let nested = moduleType.GetNestedTypes(BindingFlags.Public)
                let requestType = nested |> Array.tryFind (fun t -> t.Name = "Request")
                let responseType = nested |> Array.tryFind (fun t -> t.Name = "Response")
                let viewTypes =
                    nested |> Array.filter (fun t ->
                        FSharpType.IsRecord(t, BindingFlags.Public ||| BindingFlags.Instance)
                        && t.Name <> "Request"
                        && t.Name <> "Response"
                        && t.Name <> "ServerContext")
                    |> Array.toList

                Some {
                    ModuleName = moduleType.Name
                    Method = method
                    Path = path
                    RequestType = requestType
                    ResponseType = responseType
                    ViewTypes = viewTypes
                })
        |> Array.toList

// ============================================================
// ParsedType — bridge between reflection and existing generators
// ============================================================

type ParsedType = {
    Name: string
    Table: string option
    Fields: FieldSchema list
}

let reflectToParsedType (t: Type) : ParsedType =
    let tableName =
        match t.GetCustomAttribute<TableAttribute>() with
        | null -> None
        | attr -> Some attr.Name
    { Name = t.Name; Table = tableName; Fields = getFieldSchemas t }

// ============================================================
// Shared table metadata — used by AdminGen, Db, and Schema.sql
// ============================================================

type TableMeta = {
    DisplayName: string
    TableName: string
    Schema: TypeSchema
    DbFields: FieldSchema list
    Cols: string list
    ColStr: string
    PkCol: string
    HasPk: bool
    HasCreateTs: bool
    HasUpdateTs: bool
    MutableFields: FieldSchema list
    MutableCols: string list
    FkFields: FieldSchema list
    SelectAll: string
    SelectOne: string
    Update: string
    Delete: string
}

let computeMeta (parsed: ParsedType) : TableMeta =
    let displayName = parsed.Name
    let tableName = parsed.Table |> Option.defaultValue (pluralize (toSnakeCase displayName))
    let schema : TypeSchema = { Name = displayName; Fields = parsed.Fields; Attrs = [] }

    let dbFields = schema.Fields |> List.filter (not << isSkippedField)
    let cols = dbFields |> List.map (fun f -> toSnakeCase f.Name)
    let colStr = cols |> String.concat ", "

    let hasPk = dbFields |> List.exists isPrimaryKey
    let pkCol =
        dbFields
        |> List.tryFind isPrimaryKey
        |> Option.map (fun f -> toSnakeCase f.Name)
        |> Option.defaultValue (toSnakeCase (List.head dbFields).Name)

    let hasCreateTs = dbFields |> List.exists (fun f -> f.Attrs |> List.contains CreateTimestamp)
    let hasUpdateTs = dbFields |> List.exists (fun f -> f.Attrs |> List.exists (function UpdateTimestamp -> true | _ -> false))

    let mutableFields = dbFields |> List.filter (fun f -> not (isAutoManaged f))
    let mutableCols = mutableFields |> List.map (fun f -> toSnakeCase f.Name)
    let fkFields = dbFields |> List.filter isForeignKey

    let hasCreatedAtCol = cols |> List.contains "created_at"
    let selectAll =
        if hasCreatedAtCol then
            sprintf "SELECT %s FROM %s ORDER BY created_at DESC LIMIT 100" colStr tableName
        else
            sprintf "SELECT %s FROM %s LIMIT 100" colStr tableName
    let selectOne = sprintf "SELECT %s FROM %s WHERE %s = ?" colStr tableName pkCol
    let updateSetClause = mutableCols |> List.map (fun c -> sprintf "%s = ?" c) |> String.concat ", "
    let update = sprintf "UPDATE %s SET %s WHERE %s = ?" tableName updateSetClause pkCol
    let delete = sprintf "DELETE FROM %s WHERE %s = ?" tableName pkCol

    { DisplayName = displayName; TableName = tableName; Schema = schema
      DbFields = dbFields; Cols = cols; ColStr = colStr
      PkCol = pkCol; HasPk = hasPk; HasCreateTs = hasCreateTs; HasUpdateTs = hasUpdateTs
      MutableFields = mutableFields; MutableCols = mutableCols; FkFields = fkFields
      SelectAll = selectAll; SelectOne = selectOne; Update = update; Delete = delete }

// ============================================================
// FieldType / FieldAttr -> DSL expression string (for AdminGen)
// ============================================================

let rec fieldTypeDsl (ft: FieldType) =
    match ft with
    | FString -> "FString"
    | FInt -> "FInt"
    | FBool -> "FBool"
    | FOption inner -> sprintf "(FOption %s)" (fieldTypeDsl inner)
    | FList inner -> sprintf "(FList %s)" (fieldTypeDsl inner)
    | FRecord name -> sprintf "(FRecord \"%s\")" name

let fieldAttrDsl (fa: FieldAttr) =
    match fa with
    | PrimaryKey -> "PrimaryKey"
    | CreateTimestamp -> "CreateTimestamp"
    | UpdateTimestamp -> "UpdateTimestamp"
    | SoftDelete -> "SoftDelete"
    | ForeignKey table -> sprintf "ForeignKey \"%s\"" table
    | RichContent -> "RichContent"
    | Link -> "Link"
    | Required -> "Required"
    | Trim -> "Trim"
    | Inject -> "Inject"
    | MinLength n -> sprintf "MinLength %d" n
    | MaxLength n -> sprintf "MaxLength %d" n

// ============================================================
// AdminGen.fs generation
// ============================================================

let generateAdminTable (m: TableMeta) : string list =
    let valName = toCamelCase m.DisplayName
    let mutableNames = m.MutableFields |> List.map (fun f -> f.Name)

    let fieldLines =
        m.Schema.Fields |> List.map (fun f ->
            let attrsStr =
                if f.Attrs.IsEmpty then "[]"
                else
                    let inner = f.Attrs |> List.map fieldAttrDsl |> String.concat "; "
                    sprintf "[%s]" inner
            sprintf "            fieldWith \"%s\" %s %s" f.Name (fieldTypeDsl f.Type) attrsStr)

    let mutableFieldsStr =
        mutableNames |> List.map (fun n -> sprintf "\"%s\"" n) |> String.concat "; "

    [ ""
      sprintf "let %s : AdminTable =" valName
      sprintf "    { Name = \"%s\"" m.DisplayName
      sprintf "      Table = \"%s\"" m.TableName
      "      Schema ="
      sprintf "        schema \"%s\" [" m.DisplayName ]
    @ fieldLines
    @ [ "        ]"
        sprintf "      SelectAll = \"%s\"" m.SelectAll
        sprintf "      SelectOne = \"%s\"" m.SelectOne
        sprintf "      Update = \"%s\"" m.Update
        sprintf "      Delete = \"%s\"" m.Delete
        sprintf "      MutableFields = [%s] }" mutableFieldsStr ]

let generateAdminFs (metas: TableMeta list) : string =
    let lines = ResizeArray<string>()
    let emit s = lines.Add(s)

    emit "// AUTO-GENERATED by src/Gen/Program.fs -- do not edit by hand."
    emit "module Server.AdminGen"
    emit ""
    emit "open Hedge.Schema"
    emit ""
    emit "type AdminTable = {"
    emit "    Name: string"
    emit "    Table: string"
    emit "    Schema: TypeSchema"
    emit "    SelectAll: string"
    emit "    SelectOne: string"
    emit "    Update: string"
    emit "    Delete: string"
    emit "    MutableFields: string list"
    emit "}"

    metas |> List.iter (fun m ->
        let tableLines = generateAdminTable m
        tableLines |> List.iter emit)

    emit ""

    let valNames =
        metas |> List.map (fun m ->
            sprintf "    %s" (toCamelCase m.DisplayName))

    emit "let tables : AdminTable list = ["
    valNames |> List.iter emit
    emit "]"
    emit ""

    lines |> String.concat "\n"

// ============================================================
// Db.fs generation
// ============================================================

let rec fsType (ft: FieldType) =
    match ft with
    | FString -> "string"
    | FInt -> "int"
    | FBool -> "bool"
    | FOption inner -> sprintf "%s option" (fsType inner)
    | FList inner -> sprintf "%s list" (fsType inner)
    | FRecord name -> name

let rowParserExpr (col: string) (ft: FieldType) =
    match ft with
    | FString -> sprintf "rowStr row \"%s\"" col
    | FInt -> sprintf "rowInt row \"%s\"" col
    | FBool -> sprintf "rowBool row \"%s\"" col
    | FOption FString -> sprintf "rowStrOpt row \"%s\"" col
    | FOption FInt -> sprintf "rowIntOpt row \"%s\"" col
    | _ -> sprintf "getProp row \"%s\" |> unbox" col

let bindExpr (fieldName: string) (ft: FieldType) =
    match ft with
    | FString | FInt | FBool -> sprintf "box create.%s" fieldName
    | FOption FString -> sprintf "optToDb create.%s" fieldName
    | FOption FInt -> sprintf "optIntToDb create.%s" fieldName
    | _ -> sprintf "box create.%s" fieldName

let generateDbTable (m: TableMeta) : string list =
    let plural = m.DisplayName + "s"

    let lines = ResizeArray<string>()
    let emit (s: string) = lines.Add(s)

    emit ""
    emit "// ============================================================"
    emit (sprintf "// %s (%s)" m.DisplayName m.TableName)
    emit "// ============================================================"

    // Row type
    emit ""
    emit (sprintf "type %sRow = {" m.DisplayName)
    for f in m.DbFields do
        emit (sprintf "    %s: %s" f.Name (fsType f.Type))
    emit "}"

    // Create type
    if not m.MutableFields.IsEmpty then
        emit ""
        emit (sprintf "type %sCreate = {" m.DisplayName)
        for f in m.MutableFields do
            emit (sprintf "    %s: %s" f.Name (fsType f.Type))
        emit "}"

    // Row parser
    emit ""
    emit (sprintf "let parse%sRow (row: obj) : %sRow =" m.DisplayName m.DisplayName)
    let dbFieldsWithCols = List.zip m.DbFields m.Cols
    match dbFieldsWithCols with
    | [] -> ()
    | [(f, col)] ->
        emit (sprintf "    { %s = %s }" f.Name (rowParserExpr col f.Type))
    | (f0, col0) :: rest ->
        emit (sprintf "    { %s = %s" f0.Name (rowParserExpr col0 f0.Type))
        let lastIdx = rest.Length - 1
        rest |> List.iteri (fun i (f, col) ->
            if i = lastIdx then
                emit (sprintf "      %s = %s }" f.Name (rowParserExpr col f.Type))
            else
                emit (sprintf "      %s = %s" f.Name (rowParserExpr col f.Type)))

    // selectAll
    emit ""
    emit (sprintf "let select%s (db: D1Database) : D1PreparedStatement =" plural)
    emit (sprintf "    db.prepare(\"%s\")" m.SelectAll)

    // selectOne
    emit ""
    emit (sprintf "let select%s (id: string) (db: D1Database) : D1PreparedStatement =" m.DisplayName)
    emit (sprintf "    bind (db.prepare(\"%s\")) [| box id |]" m.SelectOne)

    // insert
    if not m.MutableFields.IsEmpty then
        let insertCols = ResizeArray<string>()
        let insertVals = ResizeArray<string>()

        if m.HasPk then
            insertCols.Add(m.PkCol)
            insertVals.Add("box id")

        for f in m.MutableFields do
            insertCols.Add(toSnakeCase f.Name)
            insertVals.Add(bindExpr f.Name f.Type)

        if m.HasCreateTs then
            insertCols.Add("created_at")
            insertVals.Add("box now")

        let colList = insertCols |> String.concat ", "
        let placeholders = insertCols |> Seq.map (fun _ -> "?") |> String.concat ", "
        let valList = insertVals |> String.concat "; "

        emit ""
        emit (sprintf "let insert%s (db: D1Database) (create: %sCreate) =" m.DisplayName m.DisplayName)
        if m.HasPk then emit "    let id = newId()"
        if m.HasCreateTs then emit "    let now = epochNow()"
        emit "    let stmt ="
        emit (sprintf "        bind (db.prepare(\"INSERT INTO %s (%s) VALUES (%s)\"))" m.TableName colList placeholders)
        emit (sprintf "             [| %s |]" valList)

        if m.HasPk && m.HasCreateTs then
            emit "    {| Stmt = stmt; Id = id; CreatedAt = now |}"
        elif m.HasPk then
            emit "    {| Stmt = stmt; Id = id |}"
        elif m.HasCreateTs then
            emit "    {| Stmt = stmt; CreatedAt = now |}"
        else
            emit "    {| Stmt = stmt |}"

    // update
    if not m.MutableFields.IsEmpty then
        let setCols = ResizeArray<string * string>()
        for f in m.MutableFields do
            setCols.Add(toSnakeCase f.Name, bindExpr f.Name f.Type)
        if m.HasUpdateTs then
            setCols.Add("updated_at", "box now")

        let setClause = setCols |> Seq.map (fun (col, _) -> sprintf "%s = ?" col) |> String.concat ", "
        let updateVals = setCols |> Seq.map snd |> Seq.toList
        let allVals = updateVals @ ["box id"] |> String.concat "; "

        emit ""
        emit (sprintf "let update%s (id: string) (create: %sCreate) (db: D1Database) : D1PreparedStatement =" m.DisplayName m.DisplayName)
        if m.HasUpdateTs then emit "    let now = epochNow()"
        emit (sprintf "    bind (db.prepare(\"UPDATE %s SET %s WHERE %s = ?\"))" m.TableName setClause m.PkCol)
        emit (sprintf "         [| %s |]" allVals)

    // delete
    emit ""
    emit (sprintf "let delete%s (id: string) (db: D1Database) : D1PreparedStatement =" m.DisplayName)
    emit (sprintf "    bind (db.prepare(\"%s\")) [| box id |]" m.Delete)

    // FK selectors
    let hasCreatedAtCol = m.Cols |> List.contains "created_at"
    for f in m.FkFields do
        let fkCol = toSnakeCase f.Name
        let paramName = toCamelCase f.Name
        let orderClause = if hasCreatedAtCol then " ORDER BY created_at DESC LIMIT 100" else " LIMIT 100"
        emit ""
        emit (sprintf "let select%sBy%s (%s: string) (db: D1Database) : D1PreparedStatement =" plural f.Name paramName)
        emit (sprintf "    bind (db.prepare(\"SELECT %s FROM %s WHERE %s = ?%s\")) [| box %s |]" m.ColStr m.TableName fkCol orderClause paramName)

    lines |> Seq.toList

let generateDbFs (metas: TableMeta list) : string =
    let lines = ResizeArray<string>()
    let emit s = lines.Add(s)

    emit "// AUTO-GENERATED by src/Gen/Program.fs -- do not edit by hand."
    emit "module Server.Db"
    emit ""
    emit "open Hedge.Workers"

    metas |> List.iter (fun m ->
        let tableLines = generateDbTable m
        tableLines |> List.iter emit)

    emit ""

    lines |> String.concat "\n"

// ============================================================
// Schema.sql generation
// ============================================================

let rec sqlType (ft: FieldType) =
    match ft with
    | FString -> "TEXT"
    | FInt -> "INTEGER"
    | FBool -> "INTEGER"
    | FOption inner -> sqlType inner
    | FList _ -> "TEXT"
    | FRecord _ -> "TEXT"

let isNullable (f: FieldSchema) =
    match f.Type with
    | FOption _ -> true
    | _ -> false

let topoSort (metas: TableMeta list) (metasByName: Map<string, TableMeta>) : TableMeta list =
    let mutable visited = Set.empty<string>
    let result = ResizeArray<TableMeta>()

    let rec visit (m: TableMeta) =
        if visited |> Set.contains m.TableName then ()
        else
            visited <- visited |> Set.add m.TableName
            for f in m.FkFields do
                for attr in f.Attrs do
                    match attr with
                    | ForeignKey typeName ->
                        match metasByName |> Map.tryFind typeName with
                        | Some dep -> visit dep
                        | None -> ()
                    | _ -> ()
            result.Add(m)

    metas |> List.iter visit
    result |> Seq.toList

let generateCreateTable (m: TableMeta) (metasByName: Map<string, TableMeta>) : string =
    let colDefs = ResizeArray<string>()
    let fkConstraints = ResizeArray<string>()

    for f in m.DbFields do
        let col = toSnakeCase f.Name
        let typ = sqlType f.Type
        let pk = if isPrimaryKey f then " PRIMARY KEY" else ""
        let notNull =
            if isPrimaryKey f || isNullable f then ""
            else " NOT NULL"
        colDefs.Add(sprintf "    %s %s%s%s" col typ pk notNull)

        for attr in f.Attrs do
            match attr with
            | ForeignKey typeName ->
                match metasByName |> Map.tryFind typeName with
                | Some target ->
                    fkConstraints.Add(sprintf "    FOREIGN KEY (%s) REFERENCES %s(%s)" col target.TableName target.PkCol)
                | None -> ()
            | _ -> ()

    let allLines =
        [ yield! colDefs |> Seq.toList
          yield! fkConstraints |> Seq.toList ]

    let body = allLines |> String.concat ",\n"
    sprintf "CREATE TABLE %s (\n%s\n);" m.TableName body

let generateIndexes (m: TableMeta) : string list =
    let indexes = ResizeArray<string>()

    for f in m.FkFields do
        let col = toSnakeCase f.Name
        indexes.Add(sprintf "CREATE INDEX idx_%s_%s ON %s(%s);" m.TableName col m.TableName col)

    if m.HasCreateTs then
        indexes.Add(sprintf "CREATE INDEX idx_%s_created_at ON %s(created_at DESC);" m.TableName m.TableName)

    indexes |> Seq.toList

let generateSchemaSql (metas: TableMeta list) : string =
    let metasByName = metas |> List.map (fun m -> m.DisplayName, m) |> Map.ofList
    let sorted = topoSort metas metasByName

    let parts = ResizeArray<string>()
    parts.Add("-- AUTO-GENERATED by src/Gen/Program.fs -- do not edit by hand.")
    parts.Add("-- Full schema derived from src/Models/Domain.fs")
    parts.Add("")

    for m in sorted do
        parts.Add(generateCreateTable m metasByName)
        parts.Add("")

    let allIndexes = sorted |> List.collect generateIndexes
    if not allIndexes.IsEmpty then
        parts.Add("-- Indexes")
        for idx in allIndexes do
            parts.Add(idx)
        parts.Add("")

    parts |> String.concat "\n"

// ============================================================
// Codecs.fs generation (Step 5)
// ============================================================

/// Compute a unique codec name for a view type, appending "View" if it collides with a domain type
let viewCodecName (domainNames: Set<string>) (vt: Type) =
    let base_ = toCamelCase vt.Name
    if domainNames.Contains base_ then base_ + "View" else base_

let generateCodecsFs (domainTypes: Type list) (endpoints: ParsedEndpoint list) (wsTypes: Type list) : string =
    let domainNames = domainTypes |> List.map (fun t -> toCamelCase t.Name) |> Set.ofList
    let lines = ResizeArray<string>()
    let emit s = lines.Add(s)

    emit "// AUTO-GENERATED by src/Gen/Program.fs -- do not edit by hand."
    emit "module Codecs"
    emit ""
    emit "open Thoth.Json"
    emit "open Hedge.Interface"
    emit "open Hedge.Codec"
    emit "open Models.Domain"
    emit "open Models.Api"
    emit ""
    emit "/// Unwrap helpers — terse pattern matches used in Handlers.fs."
    emit "let inline pk (PrimaryKey v) = v"
    emit "let inline ct (CreateTimestamp v) = v"
    emit "let inline ut (UpdateTimestamp v) = v"
    emit "let inline sd (SoftDelete v) = v"
    emit "let inline fk (ForeignKey v) = v"
    emit "let inline rc (RichContent v) = v"
    emit "let inline lk (Link v) = v"
    emit ""
    emit "module Encode ="
    emit ""

    // Domain types
    emit "    // -- Domain types --"
    for t in domainTypes do
        let name = toCamelCase t.Name
        emit (sprintf "    let inline %s (v: %s) = encode v" name t.Name)
    emit ""

    // API view types
    emit "    // -- API view types --"
    for ep in endpoints do
        for vt in ep.ViewTypes do
            let name = viewCodecName domainNames vt
            emit (sprintf "    let inline %s (v: %s.%s) = encode v" name ep.ModuleName vt.Name)
    emit ""

    // API request encoders
    emit "    // -- API request encoders --"
    for ep in endpoints do
        match ep.RequestType with
        | Some _ ->
            let name = toCamelCase ep.ModuleName + "Req"
            emit (sprintf "    let inline %s (v: %s.Request) = encode v" name ep.ModuleName)
        | None -> ()
    emit ""

    // WS event encoders
    emit "    // -- WebSocket event encoders --"
    for t in wsTypes do
        let name = toCamelCase t.Name
        emit (sprintf "    let inline %s (e: Models.Ws.%s) = encode e" name t.Name)
    emit ""

    emit "module Decode ="
    emit ""

    // Domain types
    emit "    // -- Domain types --"
    for t in domainTypes do
        let name = toCamelCase t.Name
        emit (sprintf "    let %s : Decoder<%s> = decode<%s>()" name t.Name t.Name)
    emit ""

    // API view types
    emit "    // -- API view types --"
    for ep in endpoints do
        for vt in ep.ViewTypes do
            let name = viewCodecName domainNames vt
            emit (sprintf "    let %s : Decoder<%s.%s> = decode<%s.%s>()" name ep.ModuleName vt.Name ep.ModuleName vt.Name)
    emit ""

    // API response decoders
    emit "    // -- API response decoders --"
    for ep in endpoints do
        match ep.ResponseType with
        | Some _ ->
            let name = toCamelCase ep.ModuleName + "Response"
            emit (sprintf "    let %s : Decoder<%s.Response> = decode<%s.Response>()" name ep.ModuleName ep.ModuleName)
        | None -> ()
    emit ""

    // API request decoders
    emit "    // -- API request decoders --"
    for ep in endpoints do
        match ep.RequestType with
        | Some _ ->
            let name = toCamelCase ep.ModuleName + "Req"
            emit (sprintf "    let %s : Decoder<%s.Request> = decode<%s.Request>()" name ep.ModuleName ep.ModuleName)
        | None -> ()
    emit ""

    // WS event decoders
    emit "    // -- WebSocket event decoders --"
    for t in wsTypes do
        let name = toCamelCase t.Name
        emit (sprintf "    let %s : Decoder<Models.Ws.%s> = decode<Models.Ws.%s>()" name t.Name t.Name)
    emit ""

    // Validate module
    emit "module Validate ="
    emit ""
    emit "    open Hedge.Schema"
    emit "    open Hedge.Validate"
    emit ""

    for ep in endpoints do
        match ep.RequestType with
        | Some reqType ->
            let fields = getFieldSchemas reqType
            let schemaName = toCamelCase ep.ModuleName + "Schema"
            let valName = toCamelCase ep.ModuleName + "Req"

            emit (sprintf "    let %s =" schemaName)
            emit (sprintf "        schema \"%s.Request\" [" ep.ModuleName)

            for f in fields do
                // For request types, add sensible validation attrs
                let attrs =
                    match f.Type with
                    | FString -> [Required; Trim]
                    | FOption FString -> [Trim]
                    | FList _ -> []
                    | _ -> []
                let allAttrs = attrs
                let attrsStr =
                    if allAttrs.IsEmpty then "[]"
                    else
                        let inner = allAttrs |> List.map fieldAttrDsl |> String.concat "; "
                        sprintf "[%s]" inner
                emit (sprintf "            fieldWith \"%s\" %s %s" f.Name (fieldTypeDsl f.Type) attrsStr)

            emit "        ]"
            emit ""
            emit (sprintf "    let inline %s (r: %s.Request) = validate %s r" valName ep.ModuleName schemaName)
            emit ""
        | None -> ()

    lines |> String.concat "\n"

// ============================================================
// ClientGen.fs generation (Step 6)
// ============================================================

let generateClientGenFs (endpoints: ParsedEndpoint list) (wsTypes: Type list) : string =
    let lines = ResizeArray<string>()
    let emit s = lines.Add(s)

    emit "// AUTO-GENERATED by src/Gen/Program.fs -- do not edit by hand."
    emit "module Client.ClientGen"
    emit ""
    emit "open Fable.Core"
    emit "open Thoth.Json"
    emit "open Models.Api"
    emit "open Models.Ws"
    emit "open Codecs"
    emit "open Client.Api"
    emit ""
    emit "// --- HTTP API ---"

    for ep in endpoints do
        let funcName = toCamelCase ep.ModuleName
        match ep.Method with
        | EGet ->
            emit ""
            emit (sprintf "let %s () =" funcName)
            emit (sprintf "    fetchJson \"%s\" Decode.%sResponse" ep.Path (toCamelCase ep.ModuleName))
        | EGetOne ->
            emit ""
            emit (sprintf "let %s (id: string) =" funcName)
            // Replace :id with %s in path for sprintf
            let pathTemplate = ep.Path.Replace(":id", "%s")
            emit (sprintf "    fetchJson (sprintf \"%s\" id) Decode.%sResponse" pathTemplate (toCamelCase ep.ModuleName))
        | EPost ->
            emit ""
            emit (sprintf "let %s (req: %s.Request) =" funcName ep.ModuleName)
            emit (sprintf "    let body = Encode.%sReq req |> Encode.toString 0" (toCamelCase ep.ModuleName))
            emit (sprintf "    postJson \"%s\" body Decode.%sResponse" ep.Path (toCamelCase ep.ModuleName))

    emit ""
    emit "// --- WebSocket Events ---"
    emit ""

    if not wsTypes.IsEmpty then
        // WsEvent DU
        emit "type WsEvent ="
        for t in wsTypes do
            let caseName = t.Name.Replace("Event", "")
            emit (sprintf "    | %s of %s" caseName t.Name)
        emit ""

        emit "let decodeWsEvent (text: string) : Result<WsEvent, string> ="
        emit "    match Decode.fromString (Decode.field \"type\" Decode.string) text with"
        for t in wsTypes do
            let caseName = t.Name.Replace("Event", "")
            // The type field in the JSON is the case name (e.g. "NewComment")
            let decoderName = toCamelCase t.Name
            emit (sprintf "    | Ok \"%s\" ->" caseName)
            emit (sprintf "        Decode.fromString (Decode.field \"payload\" Decode.%s) text" decoderName)
            emit (sprintf "        |> Result.map %s" caseName)
        emit "    | Ok t -> Error (sprintf \"Unknown event: %s\" t)"
        emit "    | Error e -> Error e"
        emit ""

    lines |> String.concat "\n"

// ============================================================
// Routes.fs generation (Step 7)
// ============================================================

let generateRoutesFs (endpoints: ParsedEndpoint list) : string =
    let lines = ResizeArray<string>()
    let emit s = lines.Add(s)

    emit "// AUTO-GENERATED by src/Gen/Program.fs -- do not edit by hand."
    emit "module Server.Routes"
    emit ""
    emit "open Fable.Core"
    emit "open Thoth.Json"
    emit "open Hedge.Workers"
    emit "open Hedge.Router"
    emit "open Codecs"
    emit "open Server.Env"
    emit ""
    emit "let dispatch (request: WorkerRequest) (env: Env) (ctx: ExecutionContext)"
    emit "    : JS.Promise<WorkerResponse> option ="
    emit "    let route = parseRoute request"
    emit "    match route with"

    // GET exact routes
    let getExacts = endpoints |> List.filter (fun ep -> ep.Method = EGet)
    for ep in getExacts do
        let handlerName = toCamelCase ep.ModuleName
        emit (sprintf "    | GET path when matchPath \"%s\" path = Some (Exact \"%s\") ->" ep.Path ep.Path)
        emit (sprintf "        Some (Server.Handlers.%s env)" handlerName)
        emit ""

    // GetOne routes — collected into a single GET path branch
    let getOnes = endpoints |> List.filter (fun ep -> ep.Method = EGetOne)
    if not getOnes.IsEmpty then
        emit "    | GET path ->"
        for i, ep in getOnes |> List.indexed do
            let handlerName = toCamelCase ep.ModuleName
            // Convert :id back to a match pattern
            let pattern = ep.Path.Replace(":id", ":id")
            emit (sprintf "        match matchPath \"%s\" path with" pattern)
            emit (sprintf "        | Some (WithParam (_, id)) -> Some (Server.Handlers.%s id env)" handlerName)
            emit "        | _ ->"
        emit "        None"
        emit ""

    // POST routes
    let posts = endpoints |> List.filter (fun ep -> ep.Method = EPost)
    for ep in posts do
        let handlerName = toCamelCase ep.ModuleName
        let decoderName = toCamelCase ep.ModuleName + "Req"
        emit (sprintf "    | POST path when matchPath \"%s\" path = Some (Exact \"%s\") ->" ep.Path ep.Path)
        emit "        Some (promise {"
        emit "            let! bodyText = request.text()"
        emit (sprintf "            match Decode.fromString Decode.%s bodyText with" decoderName)
        emit "            | Error err -> return badRequest err"
        emit "            | Ok req ->"
        emit (sprintf "                return! Server.Handlers.%s req request env ctx" handlerName)
        emit "        })"
        emit ""

    emit "    | _ -> None"
    emit ""

    lines |> String.concat "\n"

// ============================================================
// Handlers.fs stub generation (Step 8 — init only)
// ============================================================

let generateHandlersFs (endpoints: ParsedEndpoint list) : string =
    let lines = ResizeArray<string>()
    let emit s = lines.Add(s)

    emit "module Server.Handlers"
    emit ""
    emit "open Fable.Core"
    emit "open Hedge.Workers"
    emit "open Hedge.Router"
    emit "open Codecs"
    emit "open Models.Api"
    emit "open Server.Env"
    emit ""

    for ep in endpoints do
        let handlerName = toCamelCase ep.ModuleName
        match ep.Method with
        | EGet ->
            emit (sprintf "let %s (env: Env) : JS.Promise<WorkerResponse> =" handlerName)
            emit "    promise {"
            emit "        // TODO: implement"
            emit "        return notFound ()"
            emit "    }"
            emit ""
        | EGetOne ->
            emit (sprintf "let %s (id: string) (env: Env) : JS.Promise<WorkerResponse> =" handlerName)
            emit "    promise {"
            emit "        // TODO: implement"
            emit "        return notFound ()"
            emit "    }"
            emit ""
        | EPost ->
            emit (sprintf "let %s (req: %s.Request) (request: WorkerRequest)" handlerName ep.ModuleName)
            emit "    (env: Env) (ctx: ExecutionContext) : JS.Promise<WorkerResponse> ="
            emit "    promise {"
            emit "        // TODO: implement"
            emit "        return notFound ()"
            emit "    }"
            emit ""

    lines |> String.concat "\n"

// ============================================================
// Migration support (.NET equivalents)
// ============================================================

type ColInfo = {
    ColName: string
    ColType: string
    NotNull: bool
    IsPk: bool
}

let execProcess (cmd: string) (args: string) : string =
    let psi = Diagnostics.ProcessStartInfo(cmd, args)
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    psi.UseShellExecute <- false
    psi.CreateNoWindow <- true
    let p = Diagnostics.Process.Start(psi)
    let output = p.StandardOutput.ReadToEnd()
    p.WaitForExit()
    output

let getDbName () : string =
    let content = File.ReadAllText("wrangler.toml")
    let lines = content.Split('\n')
    lines
    |> Array.tryFind (fun l -> l.Trim().StartsWith("database_name"))
    |> Option.bind (fun l ->
        let eqIdx = l.IndexOf('=')
        if eqIdx >= 0 then
            let v = l.Substring(eqIdx + 1).Trim()
            let commentIdx = v.IndexOf('#')
            let cleaned = if commentIdx >= 0 then v.Substring(0, commentIdx).Trim() else v
            Some (cleaned.Trim('"'))
        else None)
    |> Option.defaultValue ""

let wranglerQuery (dbName: string) (sql: string) : Text.Json.JsonElement array =
    let escaped = sql.Replace("\"", "\\\"")
    let args = sprintf "wrangler d1 execute %s --local --command \"%s\" --json" dbName escaped
    let output = execProcess "npx" args
    let doc = Text.Json.JsonDocument.Parse(output)
    let first = doc.RootElement.[0]
    let results = first.GetProperty("results")
    [| for i in 0 .. results.GetArrayLength() - 1 -> results.[i] |]

let getCurrentTables (dbName: string) : string list =
    let rows = wranglerQuery dbName "SELECT name FROM sqlite_master WHERE type='table'"
    rows
    |> Array.map (fun r -> r.GetProperty("name").GetString())
    |> Array.filter (fun n ->
        not (n.StartsWith("d1_") || n.StartsWith("sqlite_") || n.StartsWith("_cf_")))
    |> Array.toList

let getTableColumns (dbName: string) (tableName: string) : ColInfo list =
    let rows = wranglerQuery dbName (sprintf "PRAGMA table_info(%s)" tableName)
    rows
    |> Array.map (fun r ->
        { ColName = r.GetProperty("name").GetString()
          ColType = r.GetProperty("type").GetString()
          NotNull = r.GetProperty("notnull").GetInt32() = 1
          IsPk = r.GetProperty("pk").GetInt32() = 1 })
    |> Array.toList

// ============================================================
// Schema diff
// ============================================================

type ColumnChange =
    | AddColumn of col: string * sqlTyp: string * notNull: bool
    | AlterColumn of col: string
    | DropColumn of col: string

let diffTable (m: TableMeta) (currentCols: ColInfo list) : ColumnChange list =
    let currentByName = currentCols |> List.map (fun c -> c.ColName, c) |> Map.ofList
    let changes = ResizeArray<ColumnChange>()

    for f in m.DbFields do
        let col = toSnakeCase f.Name
        let desiredType = sqlType f.Type
        let desiredNotNull = not (isNullable f) && not (isPrimaryKey f)

        match currentByName |> Map.tryFind col with
        | None ->
            changes.Add(AddColumn(col, desiredType, desiredNotNull))
        | Some current ->
            let typeMatch = current.ColType.ToUpper() = desiredType.ToUpper()
            let nullMatch = current.NotNull = desiredNotNull || current.IsPk
            if not typeMatch || not nullMatch then
                changes.Add(AlterColumn col)

    let desiredCols = m.DbFields |> List.map (fun f -> toSnakeCase f.Name) |> Set.ofList
    for current in currentCols do
        if not (desiredCols |> Set.contains current.ColName) then
            changes.Add(DropColumn current.ColName)

    changes |> Seq.toList

// ============================================================
// Migration SQL generation
// ============================================================

let generateAddColumnSql (tableName: string) (col: string) (typ: string) (notNull: bool) : string =
    if notNull then
        let defaultVal = if typ = "TEXT" then "''" else "0"
        sprintf "-- NOTE: Adding NOT NULL column with default value\nALTER TABLE %s ADD COLUMN %s %s NOT NULL DEFAULT %s;" tableName col typ defaultVal
    else
        sprintf "ALTER TABLE %s ADD COLUMN %s %s;" tableName col typ

let generateRecreateTableSql (m: TableMeta) (currentCols: ColInfo list) (metasByName: Map<string, TableMeta>) : string =
    let createSql = generateCreateTable m metasByName
    let newCreateSql =
        createSql.Replace(
            sprintf "CREATE TABLE %s" m.TableName,
            sprintf "CREATE TABLE %s_new" m.TableName)

    let currentColNames = currentCols |> List.map (fun c -> c.ColName) |> Set.ofList
    let survivingOrdered =
        m.DbFields
        |> List.map (fun f -> toSnakeCase f.Name)
        |> List.filter (fun c -> currentColNames |> Set.contains c)

    let colList = survivingOrdered |> String.concat ", "

    let lines = ResizeArray<string>()
    lines.Add(newCreateSql)
    lines.Add(sprintf "INSERT INTO %s_new (%s) SELECT %s FROM %s;" m.TableName colList colList m.TableName)
    lines.Add(sprintf "DROP TABLE %s;" m.TableName)
    lines.Add(sprintf "ALTER TABLE %s_new RENAME TO %s;" m.TableName m.TableName)

    let indexes = generateIndexes m
    for idx in indexes do
        lines.Add(idx)

    lines |> String.concat "\n"

// ============================================================
// Migration file management
// ============================================================

let extractLeadingNumber (s: string) : int option =
    let mutable endIdx = 0
    while endIdx < s.Length && Char.IsDigit s.[endIdx] do
        endIdx <- endIdx + 1
    if endIdx > 0 then
        Some (int (s.Substring(0, endIdx)))
    else None

let nextMigrationNumber () : int =
    if not (Directory.Exists "migrations") then 1
    else
        let files = Directory.GetFiles("migrations") |> Array.map Path.GetFileName
        let numbers =
            files
            |> Array.choose extractLeadingNumber
        if numbers.Length = 0 then 1
        else (Array.max numbers) + 1

let writeMigration (sql: string) : string =
    if not (Directory.Exists "migrations") then
        Directory.CreateDirectory("migrations") |> ignore
    let num = nextMigrationNumber ()
    let padded = sprintf "%04d" num
    let filename = sprintf "migrations/%s_auto.sql" padded
    File.WriteAllText(filename, sql)
    filename

// ============================================================
// Migrate command
// ============================================================

let runMigrate (metas: TableMeta list) (dryRun: bool) =
    let metasByName = metas |> List.map (fun m -> m.DisplayName, m) |> Map.ofList
    let dbName = getDbName ()
    if dbName = "" then
        printfn "ERROR: Could not find database_name in wrangler.toml"
    else
        let currentTables = getCurrentTables dbName
        let currentTableSet = currentTables |> Set.ofList

        let migrationParts = ResizeArray<string>()
        migrationParts.Add("-- Auto-generated migration")
        migrationParts.Add("")

        let mutable hasChanges = false

        let sorted = topoSort metas metasByName

        for m in sorted do
            if not (currentTableSet |> Set.contains m.TableName) then
                hasChanges <- true
                migrationParts.Add(sprintf "-- New table: %s" m.TableName)
                migrationParts.Add(generateCreateTable m metasByName)
                let indexes = generateIndexes m
                for idx in indexes do
                    migrationParts.Add(idx)
                migrationParts.Add("")
            else
                let currentCols = getTableColumns dbName m.TableName
                let changes = diffTable m currentCols

                if not changes.IsEmpty then
                    hasChanges <- true
                    migrationParts.Add(sprintf "-- Changes to %s" m.TableName)

                    let hasOnlyAdds = changes |> List.forall (fun c ->
                        match c with
                        | AddColumn _ -> true
                        | _ -> false)

                    if hasOnlyAdds then
                        for change in changes do
                            match change with
                            | AddColumn(col, typ, notNull) ->
                                migrationParts.Add(generateAddColumnSql m.TableName col typ notNull)
                            | _ -> ()
                    else
                        migrationParts.Add(generateRecreateTableSql m currentCols metasByName)

                    migrationParts.Add("")

        for t in currentTables do
            let inDesired = metas |> List.exists (fun m -> m.TableName = t)
            if not inDesired then
                printfn "WARNING: Table '%s' exists in DB but not in Domain.fs (not auto-dropped)" t

        if not hasChanges then
            printfn "No schema changes detected."
        else
            let migrationSql = migrationParts |> String.concat "\n"
            let filename = writeMigration migrationSql
            printfn "Generated migration: %s" filename

            if not dryRun then
                let args = sprintf "wrangler d1 migrations apply %s --local" dbName
                let output = execProcess "npx" args
                printfn "%s" output
                printfn "Migration applied locally."
            else
                printfn "(dry-run: migration file written but not applied)"

// ============================================================
// File writing helpers
// ============================================================

let ensureDir (path: string) =
    let dir = Path.GetDirectoryName(path)
    if dir <> "" && dir <> null && not (Directory.Exists dir) then
        Directory.CreateDirectory(dir) |> ignore

let writeIfChanged (path: string) (content: string) =
    ensureDir path
    if File.Exists(path) then
        let existing = File.ReadAllText(path)
        if existing <> content then
            File.WriteAllText(path, content)
            printfn "  Updated: %s" path
        // else: no change, skip
    else
        File.WriteAllText(path, content)
        printfn "  Created: %s" path

// ============================================================
// Main
// ============================================================

[<EntryPoint>]
let main (argv: string array) =
    // Step 3: Discover types via reflection
    let domainTypes = discoverDomainTypes ()
    let endpoints = discoverApiModules ()
    let wsTypes = discoverWsTypes ()

    // Step 4: Build table metadata from reflected types
    let parsed = domainTypes |> List.map reflectToParsedType
    let metas = parsed |> List.map computeMeta

    // Generate existing files (Db, AdminGen, schema.sql)
    let admin = generateAdminFs metas
    writeIfChanged "src/Server/generated/AdminGen.fs" admin

    let db = generateDbFs metas
    writeIfChanged "src/Server/generated/Db.fs" db

    let schemaSql = generateSchemaSql metas
    writeIfChanged "schema.sql" schemaSql

    // Step 5: Generate Codecs.fs
    let codecs = generateCodecsFs domainTypes endpoints wsTypes
    writeIfChanged "src/Codecs/generated/Codecs.fs" codecs

    // Step 6: Generate ClientGen.fs
    let clientGen = generateClientGenFs endpoints wsTypes
    writeIfChanged "src/Client/generated/ClientGen.fs" clientGen

    // Step 7: Generate Routes.fs
    let routes = generateRoutesFs endpoints
    writeIfChanged "src/Server/generated/Routes.fs" routes

    // Step 8: Generate Handlers.fs stubs (only if file doesn't exist)
    if not (File.Exists "src/Server/Handlers.fs") then
        let handlers = generateHandlersFs endpoints
        writeIfChanged "src/Server/Handlers.fs" handlers
        printfn "  Created handler stubs: src/Server/Handlers.fs"

    printfn "Generated %d domain types, %d endpoints, %d WS events" (List.length domainTypes) (List.length endpoints) (List.length wsTypes)

    // Migration support
    let hasMigrate = argv |> Array.exists (fun a -> a = "migrate")
    let hasDryRun = argv |> Array.exists (fun a -> a = "--dry-run")

    if hasMigrate then
        runMigrate metas hasDryRun

    0
