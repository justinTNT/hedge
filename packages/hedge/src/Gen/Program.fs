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

let capitalize (s: string) =
    if s.Length = 0 then s else (Char.ToUpper s.[0] |> string) + s.[1..]

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

/// A module composed into the site: its Models assembly, and the prefixes that
/// keep it from colliding with other modules. Root module = all-empty + the
/// app's own Server.Handlers (byte-identical to the pre-modules single-app path).
type GenModule = {
    Assembly: string
    Namespace: string     // Models root namespace: "Models" (root) | "Blog" (a module)
    TablePrefix: string   // e.g. "blog_" ; "" for the root module
    RoutePrefix: string   // e.g. "/api/blog" ; "" for the root module
    HandlerNs: string     // e.g. "Server.Handlers" | "Blog.Handlers"
    NamePrefix: string    // generated-identifier discriminator: "" (root) | "blog" (a module)
}

/// Insert a module's route prefix after the shared "/api" segment:
/// "/api/feed" + "/api/blog" -> "/api/blog/feed". "" (the WS/unit endpoint) and
/// the root module (prefix "") are left untouched.
let applyRoutePrefix (routePrefix: string) (path: string) : string =
    if routePrefix = "" || path = "" then path
    elif path.StartsWith("/api") then routePrefix + path.Substring(4)
    else routePrefix + path

let rec classifyFieldType (propType: Type) : FieldType * FieldAttr list =
    if propType.IsGenericType then
        let def = propType.GetGenericTypeDefinition()
        if def = typedefof<PrimaryKey<_>> then
            let inner = propType.GenericTypeArguments.[0]
            (if inner = typeof<int> then FInt else FString), [PrimaryKey]
        elif def = typedefof<Unique<_>> then
            let inner = propType.GenericTypeArguments.[0]
            (if inner = typeof<int> then FInt else FString), [Unique]
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
    // IdentityRef is a decoupled handle to the shared identity layer — treat it
    // as a FK to the shared `identities` table (reusing ForeignKey machinery),
    // without the module depending on any concrete Identity type.
    elif propType = typeof<IdentityRef> then FString, [ForeignKey "Identity"]
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

/// Discover all record types in the module's <ns>.Domain module.
let discoverDomainTypes (ns: string) (assembly: Assembly) : Type list =
    assembly.GetTypes()
    |> Array.filter (fun t ->
        t.FullName.StartsWith(ns + ".Domain+")
        && FSharpType.IsRecord(t, BindingFlags.Public ||| BindingFlags.Instance))
    |> Array.toList

/// Discover all WS event types in the module's <ns>.Ws module.
let discoverWsTypes (ns: string) (assembly: Assembly) : Type list =
    assembly.GetTypes()
    |> Array.filter (fun t ->
        t.FullName.StartsWith(ns + ".Ws+")
        && FSharpType.IsRecord(t, BindingFlags.Public ||| BindingFlags.Instance))
    |> Array.toList

// ============================================================
// API endpoint discovery
// ============================================================

// The GET family is a 2x2 over (path param? x typed query?): EGet (neither),
// EGetQuery (query only), EGetBy (path param only, formerly EGetOne), EGetByQuery
// (both). EGetQuery/EGetByQuery carry the query record type in ParsedEndpoint.QueryType.
type EndpointMethod = EGet | EGetQuery | EGetBy | EGetByQuery | EPost

type ParsedEndpoint = {
    ModuleName: string
    Namespace: string     // the module's Models namespace, e.g. "Models" | "Blog"
    NamePrefix: string     // generated-identifier discriminator ("" for root)
    Method: EndpointMethod
    Path: string
    HandlerNs: string
    RequestType: Type option
    ResponseType: Type option
    QueryType: Type option   // the 'query record for EGetQuery / EGetByQuery
    ViewTypes: Type list
}

let discoverApiModules (ns: string) (namePrefix: string) (assembly: Assembly) (routePrefix: string) (handlerNs: string) : ParsedEndpoint list =
    // Api modules are nested types under <ns>.Api
    let apiType =
        assembly.GetTypes()
        |> Array.tryFind (fun t -> t.FullName = ns + ".Api")
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
                    elif typeDef = typedefof<GetBy<_>> then
                        // GetBy contains a function string -> string
                        let func = fields.[0] :?> (string -> string)
                        EGetBy, func ":id"
                    elif typeDef = typedefof<GetQuery<_,_>> then
                        EGetQuery, fields.[0] :?> string
                    elif typeDef = typedefof<GetByQuery<_,_>> then
                        let func = fields.[0] :?> (string -> string)
                        EGetByQuery, func ":id"
                    else
                        EGet, ""

                // The query record is the first type arg of GetQuery / GetByQuery.
                let queryType =
                    if typeDef = typedefof<GetQuery<_,_>> || typeDef = typedefof<GetByQuery<_,_>>
                    then Some endpointType.GenericTypeArguments.[0]
                    else None

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
                        && t.Name <> "ServerContext"
                        && t.Name <> "Query")   // Query is URL-encoded, not a JSON codec type
                    |> Array.toList

                Some {
                    ModuleName = moduleType.Name
                    Namespace = ns
                    NamePrefix = namePrefix
                    Method = method
                    Path = applyRoutePrefix routePrefix path
                    HandlerNs = handlerNs
                    RequestType = requestType
                    ResponseType = responseType
                    QueryType = queryType
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
    Insert: string
    Update: string
    Delete: string
}

let computeMeta (tablePrefix: string) (parsed: ParsedType) : TableMeta =
    let displayName = parsed.Name
    let tableName = tablePrefix + (parsed.Table |> Option.defaultValue (pluralize (toSnakeCase displayName)))
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
    // Soft-delete column (deleted_at), if this type has a SoftDelete field. When present,
    // the admin soft-deletes (stamps the timestamp) instead of a hard DELETE — matching
    // the domain model, avoiding FK violations when the row has referencing children
    // (e.g. deleting a blog_items row that still has blog_comments), and hiding
    // soft-deleted rows from admin listings.
    let sdCol =
        dbFields |> List.tryPick (fun f ->
            if f.Attrs |> List.exists (function SoftDelete -> true | _ -> false)
            then Some (toSnakeCase f.Name) else None)
    let liveFilter = match sdCol with Some c -> sprintf " WHERE %s IS NULL" c | None -> ""
    let selectAll =
        if hasCreatedAtCol then
            sprintf "SELECT %s FROM %s%s ORDER BY created_at DESC LIMIT 100" colStr tableName liveFilter
        else
            sprintf "SELECT %s FROM %s%s LIMIT 100" colStr tableName liveFilter
    let selectOne =
        match sdCol with
        | Some c -> sprintf "SELECT %s FROM %s WHERE %s = ? AND %s IS NULL" colStr tableName pkCol c
        | None -> sprintf "SELECT %s FROM %s WHERE %s = ?" colStr tableName pkCol
    // updated_at is auto-managed, so it isn't a mutable column — but the admin
    // still has to stamp it, or editing through the admin silently leaves it
    // null while the typed Db update sets it.
    let updateSetClause =
        (mutableCols @ (if hasUpdateTs then [ "updated_at" ] else []))
        |> List.map (fun c -> sprintf "%s = ?" c)
        |> String.concat ", "
    let update = sprintf "UPDATE %s SET %s WHERE %s = ?" tableName updateSetClause pkCol
    // Soft-delete when the type supports it (stamp deleted_at via SQLite's clock, so
    // the admin's single id-bind still matches); otherwise a real DELETE.
    let delete =
        match sdCol with
        | Some c -> sprintf "UPDATE %s SET %s = CAST(strftime('%%s','now') AS INTEGER) WHERE %s = ?" tableName c pkCol
        | None -> sprintf "DELETE FROM %s WHERE %s = ?" tableName pkCol

    // Admin create is only meaningful for a table with a real primary key —
    // without one, pkCol is just the first column and we'd write a generated id
    // into a data column (e.g. item_tags.item_id). Those tables get no INSERT.
    let insert =
        if not hasPk then ""
        else
            let insertCols = [ pkCol ] @ mutableCols @ (if hasCreateTs then [ "created_at" ] else [])
            let placeholders = insertCols |> List.map (fun _ -> "?") |> String.concat ", "
            sprintf "INSERT INTO %s (%s) VALUES (%s)" tableName (String.concat ", " insertCols) placeholders

    { DisplayName = displayName; TableName = tableName; Schema = schema
      DbFields = dbFields; Cols = cols; ColStr = colStr
      PkCol = pkCol; HasPk = hasPk; HasCreateTs = hasCreateTs; HasUpdateTs = hasUpdateTs
      MutableFields = mutableFields; MutableCols = mutableCols; FkFields = fkFields
      SelectAll = selectAll; SelectOne = selectOne; Insert = insert; Update = update; Delete = delete }

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
    | Unique -> "Unique"
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
        sprintf "      Insert = \"%s\"" m.Insert
        sprintf "      HasCreateTs = %s" (if m.HasCreateTs then "true" else "false")
        sprintf "      HasUpdateTs = %s" (if m.HasUpdateTs then "true" else "false")
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
    emit "open Hedge.Admin"
    emit ""
    emit "// The AdminTable type + the schema-driven CRUD live in Hedge.Admin; this"
    emit "// file only emits the table descriptors."

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

    // FK selectors — carry the soft-delete filter so relationship reads (e.g. a
    // post's comments) never surface soft-deleted rows, matching the single-row and
    // list selectors above.
    let hasCreatedAtCol = m.Cols |> List.contains "created_at"
    let sdCol =
        m.DbFields |> List.tryPick (fun f ->
            if f.Attrs |> List.exists (function SoftDelete -> true | _ -> false)
            then Some (toSnakeCase f.Name) else None)
    let liveClause = match sdCol with Some c -> sprintf " AND %s IS NULL" c | None -> ""
    for f in m.FkFields do
        let fkCol = toSnakeCase f.Name
        let paramName = toCamelCase f.Name
        let orderClause = if hasCreatedAtCol then " ORDER BY created_at DESC LIMIT 100" else " LIMIT 100"
        emit ""
        emit (sprintf "let select%sBy%s (%s: string) (db: D1Database) : D1PreparedStatement =" plural f.Name paramName)
        emit (sprintf "    bind (db.prepare(\"SELECT %s FROM %s WHERE %s = ?%s%s\")) [| box %s |]" m.ColStr m.TableName fkCol liveClause orderClause paramName)

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
    // Table names (namespace-aware) for hand-written SQL — reference these instead
    // of hardcoding a literal, so a module's SQL works whether its tables are
    // `items` (standalone) or `blog_items` (mounted with a prefix).
    emit "module Tables ="
    if metas.IsEmpty then emit "    ()"
    else metas |> List.iter (fun m -> emit (sprintf "    let %s = \"%s\"" (toCamelCase m.DisplayName) m.TableName))

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
                | None ->
                    // A ForeignKey/IdentityRef whose target isn't in the composition would
                    // otherwise silently lose its SQL FK constraint. Fail loudly instead.
                    failwithf "%s.%s: foreign-key target '%s' is not a domain type in this composition — is its module missing from gen-modules(.<site>).json?" m.DisplayName col typeName
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

    for f in m.DbFields do
        if f.Attrs |> List.contains FieldAttr.Unique then
            let col = toSnakeCase f.Name
            indexes.Add(sprintf "CREATE UNIQUE INDEX idx_%s_%s ON %s(%s);" m.TableName col m.TableName col)

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

// -- Composed-codegen naming (root unprefixed = byte-identical; non-root prefixed) --

/// Fully-qualified F# name of a (possibly nested) type, e.g. "Blog.Ws.NewCommentEvent".
let qualifiedTypeName (t: Type) = t.FullName.Replace("+", ".")

/// An API module's fully-qualified path: "<ns>.Api.<Module>".
let apiRef (ep: ParsedEndpoint) = sprintf "%s.Api.%s" ep.Namespace ep.ModuleName

/// A disambiguated generated identifier: root ("") keeps the plain camelCase name
/// (byte-identical); a non-root module prefixes it (e.g. "blogSubmitComment").
let genName (namePrefix: string) (pascalBase: string) =
    if namePrefix = "" then toCamelCase pascalBase else namePrefix + pascalBase

/// Type-reference emission. When `qualify` (a multi-module compose), every type ref
/// is namespace-qualified so colliding module names (both apps have `SubmitComment`)
/// disambiguate; when single-module, refs stay unqualified via `open` — byte-identical.
let domainRef (qualify: bool) (t: Type) = if qualify then qualifiedTypeName t else t.Name
let apiTypeRef (qualify: bool) (ep: ParsedEndpoint) (suffix: string) =
    if qualify then sprintf "%s.Api.%s.%s" ep.Namespace ep.ModuleName suffix
    else sprintf "%s.%s" ep.ModuleName suffix

/// A field of a GetQuery/GetByQuery `'query` record, reduced to what codegen needs:
/// the query-string key (field name, first char lowercased) and how to (de)serialize it.
type QueryFieldKind = QOptString | QReqString | QOptInt | QReqInt
type QueryField = { Name: string; Key: string; Kind: QueryFieldKind }

let queryFields (t: Type) : QueryField list =
    FSharpType.GetRecordFields t
    |> Array.toList
    |> List.map (fun p ->
        let key = string (System.Char.ToLowerInvariant p.Name.[0]) + p.Name.Substring 1
        let pt = p.PropertyType
        let isOpt = pt.IsGenericType && pt.GetGenericTypeDefinition() = typedefof<option<_>>
        let inner = if isOpt then pt.GenericTypeArguments.[0] else pt
        let isInt = inner = typeof<int>
        let kind =
            match isOpt, isInt with
            | true,  true  -> QOptInt
            | true,  false -> QOptString
            | false, true  -> QReqInt
            | false, false -> QReqString
        { Name = p.Name; Key = key; Kind = kind })

/// Compute a unique codec name for a view type, appending "View" if it collides with a domain type
let viewCodecName (domainNames: Set<string>) (namePrefix: string) (vt: Type) =
    let base_ = toCamelCase vt.Name
    let disambiguated = if domainNames.Contains base_ then base_ + "View" else base_
    if namePrefix = "" then disambiguated else namePrefix + (capitalize disambiguated)

let generateCodecsFs (domainTypes: Type list) (endpoints: ParsedEndpoint list) (wsTypes: (Type * string) list) (qualify: bool) (singleNs: string) : string =
    let domainNames = domainTypes |> List.map (fun t -> toCamelCase t.Name) |> Set.ofList
    let lines = ResizeArray<string>()
    let emit s = lines.Add(s)

    emit "// AUTO-GENERATED by src/Gen/Program.fs -- do not edit by hand."
    emit "module Codecs"
    emit ""
    emit "open Thoth.Json"
    emit "open Hedge.Interface"
    emit "open Hedge.Codec"
    // Composing modules qualifies every type ref (colliding module names), so no
    // opens; single-module opens its own namespace (`singleNs`) for byte-identical
    // output — `Models` for the current apps, but any module's ns (e.g. `Blog`).
    if not qualify then
        emit (sprintf "open %s.Domain" singleNs)
        emit (sprintf "open %s.Api" singleNs)
    emit ""
    emit "/// Unwrap helpers — terse pattern matches used in Handlers.fs."
    emit "let inline pk (PrimaryKey v) = v"
    emit "let inline ct (CreateTimestamp v) = v"
    emit "let inline ut (UpdateTimestamp v) = v"
    emit "let inline sd (SoftDelete v) = v"
    emit "let inline fk (ForeignKey v) = v"
    emit "let inline rc (RichContent v) = v"
    emit "let inline lk (Link v) = v"
    emit "let inline uq (Unique v) = v"
    emit ""
    emit "module Encode ="
    emit ""

    // Domain types
    emit "    // -- Domain types --"
    for t in domainTypes do
        let name = toCamelCase t.Name
        emit (sprintf "    let inline %s (v: %s) = encode v" name (domainRef qualify t))
    emit ""

    // API view types
    emit "    // -- API view types --"
    for ep in endpoints do
        for vt in ep.ViewTypes do
            let name = viewCodecName domainNames ep.NamePrefix vt
            emit (sprintf "    let inline %s (v: %s) = encode v" name (apiTypeRef qualify ep vt.Name))
    emit ""

    // API request encoders
    emit "    // -- API request encoders --"
    for ep in endpoints do
        match ep.RequestType with
        | Some _ ->
            let name = genName ep.NamePrefix ep.ModuleName + "Req"
            emit (sprintf "    let inline %s (v: %s) = encode v" name (apiTypeRef qualify ep "Request"))
        | None -> ()
    emit ""

    // WS event encoders
    emit "    // -- WebSocket event encoders --"
    for (t, np) in wsTypes do
        let name = genName np t.Name
        emit (sprintf "    let inline %s (e: %s) = encode e" name (qualifiedTypeName t))
    emit ""

    emit "module Decode ="
    emit ""

    // Domain types
    emit "    // -- Domain types --"
    for t in domainTypes do
        let name = toCamelCase t.Name
        let tref = domainRef qualify t
        emit (sprintf "    let %s : Decoder<%s> = decode<%s>()" name tref tref)
    emit ""

    // API view types
    emit "    // -- API view types --"
    for ep in endpoints do
        for vt in ep.ViewTypes do
            let name = viewCodecName domainNames ep.NamePrefix vt
            let tref = apiTypeRef qualify ep vt.Name
            emit (sprintf "    let %s : Decoder<%s> = decode<%s>()" name tref tref)
    emit ""

    // API response decoders
    emit "    // -- API response decoders --"
    for ep in endpoints do
        match ep.ResponseType with
        | Some _ ->
            let name = genName ep.NamePrefix ep.ModuleName + "Response"
            let tref = apiTypeRef qualify ep "Response"
            emit (sprintf "    let %s : Decoder<%s> = decode<%s>()" name tref tref)
        | None -> ()
    emit ""

    // API request decoders
    emit "    // -- API request decoders --"
    for ep in endpoints do
        match ep.RequestType with
        | Some _ ->
            let name = genName ep.NamePrefix ep.ModuleName + "Req"
            let tref = apiTypeRef qualify ep "Request"
            emit (sprintf "    let %s : Decoder<%s> = decode<%s>()" name tref tref)
        | None -> ()
    emit ""

    // WS event decoders
    emit "    // -- WebSocket event decoders --"
    for (t, np) in wsTypes do
        let name = genName np t.Name
        let tref = qualifiedTypeName t
        emit (sprintf "    let %s : Decoder<%s> = decode<%s>()" name tref tref)
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
            let schemaName = genName ep.NamePrefix ep.ModuleName + "Schema"
            let valName = genName ep.NamePrefix ep.ModuleName + "Req"
            let label = if qualify then sprintf "%s.Api.%s.Request" ep.Namespace ep.ModuleName else sprintf "%s.Request" ep.ModuleName

            emit (sprintf "    let %s =" schemaName)
            emit (sprintf "        schema \"%s\" [" label)

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
            emit (sprintf "    let inline %s (r: %s) = validate %s r" valName (apiTypeRef qualify ep "Request") schemaName)
            emit ""
        | None -> ()

    lines |> String.concat "\n"

// ============================================================
// ClientGen.fs generation (Step 6)
// ============================================================

let generateClientGenFs (endpoints: ParsedEndpoint list) (wsTypes: (Type * string) list) (qualify: bool) (singleNs: string) : string =
    let lines = ResizeArray<string>()
    let emit s = lines.Add(s)

    emit "// AUTO-GENERATED by src/Gen/Program.fs -- do not edit by hand."
    emit "module Client.ClientGen"
    emit ""
    emit "open Fable.Core"
    emit "open Thoth.Json"
    // Composed modules qualify type refs; single-module opens its own namespace
    // (`singleNs`) for byte-identical output.
    if not qualify then
        emit (sprintf "open %s.Api" singleNs)
        emit (sprintf "open %s.Ws" singleNs)
    emit "open Codecs"
    emit "open Client.Api"
    emit ""
    emit "// --- HTTP API ---"

    for ep in endpoints do
        // Function names are prefixed for non-root modules (both apps have
        // `submitComment`/`events`); codec refs match the combined Codecs (also prefixed).
        let funcName = genName ep.NamePrefix ep.ModuleName
        let respName = genName ep.NamePrefix ep.ModuleName + "Response"
        let reqName = genName ep.NamePrefix ep.ModuleName + "Req"
        // Build the client-side query-string pairs from a `'query` record var: each
        // field becomes a (key, value) option, filtered by `List.choose id`, then
        // `buildQuery` (Client.Api) URL-encodes + joins into "?k=v&...".
        let queryPairsExpr (recVar: string) (qt: Type) =
            let items =
                queryFields qt |> List.map (fun f ->
                    match f.Kind with
                    | QOptString -> sprintf "(match %s.%s with Some v -> Some (\"%s\", v) | None -> None)" recVar f.Name f.Key
                    | QReqString -> sprintf "Some (\"%s\", %s.%s)" f.Key recVar f.Name
                    | QOptInt    -> sprintf "(match %s.%s with Some v -> Some (\"%s\", string v) | None -> None)" recVar f.Name f.Key
                    | QReqInt    -> sprintf "Some (\"%s\", string %s.%s)" f.Key recVar f.Name)
            // `List.choose (fun p -> p)`, not `List.choose id`: the path param is
            // named `id` in GetByQuery clients and would shadow the identity function.
            "buildQuery (List.choose (fun p -> p) [ " + String.concat "; " items + " ])"
        match ep.Method with
        | EGet ->
            emit ""
            emit (sprintf "let %s () =" funcName)
            emit (sprintf "    fetchJson \"%s\" Decode.%s" ep.Path respName)
        | EGetBy ->
            emit ""
            emit (sprintf "let %s (id: string) =" funcName)
            // Replace :id with %s in path for sprintf
            let pathTemplate = ep.Path.Replace(":id", "%s")
            emit (sprintf "    fetchJson (sprintf \"%s\" id) Decode.%s" pathTemplate respName)
        | EGetQuery ->
            emit ""
            emit (sprintf "let %s (query: %s) =" funcName (apiTypeRef qualify ep "Query"))
            emit (sprintf "    let qs = %s" (queryPairsExpr "query" ep.QueryType.Value))
            emit (sprintf "    fetchJson (\"%s\" + qs) Decode.%s" ep.Path respName)
        | EGetByQuery ->
            emit ""
            emit (sprintf "let %s (id: string) (query: %s) =" funcName (apiTypeRef qualify ep "Query"))
            emit (sprintf "    let qs = %s" (queryPairsExpr "query" ep.QueryType.Value))
            let fmt = ep.Path.Replace(":id", "%s") + "%s"
            emit (sprintf "    fetchJson (sprintf \"%s\" id qs) Decode.%s" fmt respName)
        | EPost ->
            emit ""
            emit (sprintf "let %s (req: %s) =" funcName (apiTypeRef qualify ep "Request"))
            emit (sprintf "    let body = Encode.%s req |> Encode.toString 0" reqName)
            emit (sprintf "    postJson \"%s\" body Decode.%s" ep.Path respName)

    emit ""
    emit "// --- WebSocket Events ---"
    emit ""

    // One WsEvent DU + decode fn PER module: both apps emit wire-type "NewComment",
    // so a single combined decode would have duplicate match arms. Root keeps the
    // plain `WsEvent`/`decodeWsEvent` names (byte-identical); non-root is prefixed.
    let wsByModule = wsTypes |> List.groupBy snd
    for (np, group) in wsByModule do
        let duName = if np = "" then "WsEvent" else capitalize np + "WsEvent"
        let decodeName = genName np "DecodeWsEvent"
        let tref (t: Type) = if qualify then qualifiedTypeName t else t.Name
        // Case names are prefixed for non-root modules too: both apps' DUs are
        // opened together in a composed client, so an unprefixed `NewComment`
        // would shadow across DUs. The wire "type" value stays unprefixed.
        let caseName (t: Type) =
            let bare = t.Name.Replace("Event", "")
            if np = "" then bare else capitalize np + bare
        emit (sprintf "type %s =" duName)
        for (t, _) in group do
            emit (sprintf "    | %s of %s" (caseName t) (tref t))
        emit ""

        emit (sprintf "let %s (text: string) : Result<%s, string> =" decodeName duName)
        emit "    match Decode.fromString (Decode.field \"type\" Decode.string) text with"
        for (t, _) in group do
            // The type field in the JSON is the bare event name (e.g. "NewComment")
            let wireName = t.Name.Replace("Event", "")
            let decoderName = genName np t.Name
            emit (sprintf "    | Ok \"%s\" ->" wireName)
            emit (sprintf "        Decode.fromString (Decode.field \"payload\" Decode.%s) text" decoderName)
            emit (sprintf "        |> Result.map %s" (caseName t))
        emit "    | Ok t -> Error (sprintf \"Unknown event: %s\" t)"
        emit "    | Error e -> Error e"
        emit ""

    lines |> String.concat "\n"

// ============================================================
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

    // Construct a `'query` record from the request's query string. Optional fields
    // map an absent/empty param to None; required fields to a sensible default.
    let queryRecordExpr (qt: Type) (typeRef: string) =
        let fields =
            queryFields qt |> List.map (fun f ->
                let raw = sprintf "(getQueryParam request.url \"%s\")" f.Key
                match f.Kind with
                | QOptString -> sprintf "%s = (let v = %s in if isNull v || v = \"\" then None else Some v)" f.Name raw
                | QReqString -> sprintf "%s = (let v = %s in if isNull v then \"\" else v)" f.Name raw
                | QOptInt    -> sprintf "%s = (let v = %s in if isNull v || v = \"\" then None else Some (int v))" f.Name raw
                | QReqInt    -> sprintf "%s = (let v = %s in if isNull v || v = \"\" then 0 else int v)" f.Name raw)
        sprintf "({ %s } : %s)" (String.concat "; " fields) typeRef
    let queryTypeRef (ep: ParsedEndpoint) = sprintf "%s.Api.%s.Query" ep.Namespace ep.ModuleName

    // GET exact routes (no query)
    let getExacts = endpoints |> List.filter (fun ep -> ep.Method = EGet)
    for ep in getExacts do
        let handlerName = toCamelCase ep.ModuleName
        emit (sprintf "    | GET path when matchPath \"%s\" path = Some (Exact \"%s\") ->" ep.Path ep.Path)
        emit (sprintf "        Some (%s.%s env)" ep.HandlerNs handlerName)
        emit ""

    // GET exact routes with a typed query (parse the query record, pass it in)
    let getQueries = endpoints |> List.filter (fun ep -> ep.Method = EGetQuery)
    for ep in getQueries do
        let handlerName = toCamelCase ep.ModuleName
        emit (sprintf "    | GET path when matchPath \"%s\" path = Some (Exact \"%s\") ->" ep.Path ep.Path)
        emit (sprintf "        let query = %s" (queryRecordExpr ep.QueryType.Value (queryTypeRef ep)))
        emit (sprintf "        Some (%s.%s query env)" ep.HandlerNs handlerName)
        emit ""

    // GET by path param (with or without a typed query) — one shared GET-path branch
    let getOnes = endpoints |> List.filter (fun ep -> ep.Method = EGetBy || ep.Method = EGetByQuery)
    if not getOnes.IsEmpty then
        emit "    | GET path ->"
        for ep in getOnes do
            let handlerName = toCamelCase ep.ModuleName
            emit (sprintf "        match matchPath \"%s\" path with" ep.Path)
            match ep.Method with
            | EGetByQuery ->
                emit "        | Some (WithParam (_, id)) ->"
                emit (sprintf "            let query = %s" (queryRecordExpr ep.QueryType.Value (queryTypeRef ep)))
                emit (sprintf "            Some (%s.%s id query env)" ep.HandlerNs handlerName)
            | _ ->
                emit (sprintf "        | Some (WithParam (_, id)) -> Some (%s.%s id env)" ep.HandlerNs handlerName)
            emit "        | _ ->"
        emit "        None"
        emit ""

    // POST routes
    let posts = endpoints |> List.filter (fun ep -> ep.Method = EPost)
    for ep in posts do
        let handlerName = toCamelCase ep.ModuleName
        let decoderName = genName ep.NamePrefix ep.ModuleName + "Req"
        emit (sprintf "    | POST path when matchPath \"%s\" path = Some (Exact \"%s\") ->" ep.Path ep.Path)
        emit "        Some (promise {"
        emit "            let! bodyText = request.text()"
        emit (sprintf "            match Decode.fromString Decode.%s bodyText with" decoderName)
        emit "            | Error err -> return badRequest err"
        emit "            | Ok req ->"
        emit (sprintf "                return! %s.%s req request env ctx" ep.HandlerNs handlerName)
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
        | EGetBy ->
            emit (sprintf "let %s (id: string) (env: Env) : JS.Promise<WorkerResponse> =" handlerName)
            emit "    promise {"
            emit "        // TODO: implement"
            emit "        return notFound ()"
            emit "    }"
            emit ""
        | EGetQuery ->
            emit (sprintf "let %s (query: %s.Query) (env: Env) : JS.Promise<WorkerResponse> =" handlerName ep.ModuleName)
            emit "    promise {"
            emit "        // TODO: implement"
            emit "        return notFound ()"
            emit "    }"
            emit ""
        | EGetByQuery ->
            emit (sprintf "let %s (id: string) (query: %s.Query) (env: Env) : JS.Promise<WorkerResponse> =" handlerName ep.ModuleName)
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
    let extract (l: string) =
        let eqIdx = l.IndexOf('=')
        if eqIdx >= 0 then
            let v = l.Substring(eqIdx + 1).Trim()
            let commentIdx = v.IndexOf('#')
            let cleaned = if commentIdx >= 0 then v.Substring(0, commentIdx).Trim() else v
            Some (cleaned.Trim('"'))
        else None
    // Select the database for HEDGE_SITE (the same flag that drives the gen manifest),
    // so a per-site migration diffs the right tenant DB. Without it, take the top-level
    // default (the first database_name before any [env.*] block).
    let site = System.Environment.GetEnvironmentVariable "HEDGE_SITE"
    if System.String.IsNullOrEmpty site then
        lines
        |> Array.takeWhile (fun l -> not (l.TrimStart().StartsWith "[env."))
        |> Array.tryPick (fun l -> if l.Trim().StartsWith "database_name" then extract l else None)
        |> Option.defaultValue ""
    else
        let marker = sprintf "[env.%s" site
        let mutable inSection = false
        let mutable result = ""
        for l in lines do
            let t = l.TrimStart()
            if t.StartsWith "[env." then
                inSection <- t.StartsWith(marker + "]") || t.StartsWith(marker + ".")
            if inSection && result = "" && l.Trim().StartsWith "database_name" then
                match extract l with Some v -> result <- v | None -> ()
        result

let wranglerQuery (remote: bool) (dbName: string) (sql: string) : Text.Json.JsonElement array =
    let escaped = sql.Replace("\"", "\\\"")
    let target = if remote then "--remote" else "--local"
    let args = sprintf "wrangler d1 execute %s %s --command \"%s\" --json" dbName target escaped
    let output = execProcess "npx" args
    let doc = Text.Json.JsonDocument.Parse(output)
    // wrangler's `d1 execute --json` shape varies by version/target: --local wraps
    // the statement result in an array ([{ results, success, meta }]) while --remote
    // returns the bare object ({ results, success, meta }). Accept both.
    let root = doc.RootElement
    let first =
        if root.ValueKind = Text.Json.JsonValueKind.Array then root.[0] else root
    let results = first.GetProperty("results")
    [| for i in 0 .. results.GetArrayLength() - 1 -> results.[i] |]

let getCurrentTables (remote: bool) (dbName: string) : string list =
    let rows = wranglerQuery remote dbName "SELECT name FROM sqlite_master WHERE type='table'"
    rows
    |> Array.map (fun r -> r.GetProperty("name").GetString())
    |> Array.filter (fun n ->
        not (n.StartsWith("d1_") || n.StartsWith("sqlite_") || n.StartsWith("_cf_")))
    |> Array.toList

let getTableColumns (remote: bool) (dbName: string) (tableName: string) : ColInfo list =
    let rows = wranglerQuery remote dbName (sprintf "PRAGMA table_info(%s)" tableName)
    rows
    |> Array.map (fun r ->
        { ColName = r.GetProperty("name").GetString()
          ColType = r.GetProperty("type").GetString()
          NotNull = r.GetProperty("notnull").GetInt32() = 1
          IsPk = r.GetProperty("pk").GetInt32() = 1 })
    |> Array.toList

/// Current foreign keys of a table as a set of (fromColumn, referencedTable), for
/// diffing against the model's desired FKs (a change needs a table rebuild).
let getTableForeignKeys (remote: bool) (dbName: string) (tableName: string) : Set<string * string> =
    wranglerQuery remote dbName (sprintf "PRAGMA foreign_key_list(%s)" tableName)
    |> Array.map (fun r -> r.GetProperty("from").GetString(), r.GetProperty("table").GetString())
    |> Set.ofArray

/// Current Gen-managed index names (idx_*) of a table; sqlite auto-indexes are ignored.
let getTableIndexNames (remote: bool) (dbName: string) (tableName: string) : Set<string> =
    wranglerQuery remote dbName (sprintf "PRAGMA index_list(%s)" tableName)
    |> Array.choose (fun r ->
        let name = r.GetProperty("name").GetString()
        if name.StartsWith "idx_" then Some name else None)
    |> Set.ofArray

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

/// The model's desired foreign keys as (fromColumn, referencedTable) — resolved
/// through the composition, to diff against the live table's FKs.
let desiredForeignKeys (m: TableMeta) (metasByName: Map<string, TableMeta>) : Set<string * string> =
    m.DbFields
    |> List.collect (fun f ->
        f.Attrs |> List.choose (function
            | ForeignKey typeName ->
                metasByName |> Map.tryFind typeName |> Option.map (fun t -> toSnakeCase f.Name, t.TableName)
            | _ -> None))
    |> Set.ofList

/// The Gen-managed index names the model expects (matches generateIndexes).
let desiredIndexNames (m: TableMeta) : Set<string> =
    [ for f in m.FkFields do
          yield sprintf "idx_%s_%s" m.TableName (toSnakeCase f.Name)
      for f in m.DbFields do
          if f.Attrs |> List.contains FieldAttr.Unique then
              yield sprintf "idx_%s_%s" m.TableName (toSnakeCase f.Name)
      if m.HasCreateTs then
          yield sprintf "idx_%s_created_at" m.TableName ]
    |> Set.ofList

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
    // For each schema column: copy it when the source table has it. If the schema
    // ADDS a NOT NULL column the source lacks, seed a default in the SELECT (it
    // can't be backfilled) so the populate INSERT never violates NOT NULL — the
    // old code silently omitted such columns, producing a broken migration. New
    // nullable/PK columns are omitted and default to NULL.
    let insertCols = ResizeArray<string>()
    let selectExprs = ResizeArray<string>()
    for f in m.DbFields do
        let col = toSnakeCase f.Name
        if currentColNames |> Set.contains col then
            insertCols.Add col
            selectExprs.Add col
        elif not (isNullable f) && not (isPrimaryKey f) then
            insertCols.Add col
            selectExprs.Add (if sqlType f.Type = "TEXT" then "''" else "0")

    let colList = insertCols |> String.concat ", "
    let selList = selectExprs |> String.concat ", "

    let lines = ResizeArray<string>()
    // Defer FK enforcement to the migration's (implicit) transaction commit so the
    // DROP + RENAME don't trip constraints from child tables mid-rebuild on a populated
    // DB. This is D1's documented mechanism — it runs migrations in an implicit
    // transaction and forbids toggling foreign_keys inside one
    // (https://developers.cloudflare.com/d1/sql-api/foreign-keys/). Any real violation
    // still surfaces at commit, so review + test a recreate before applying it remotely
    // (remote migrations are never auto-applied).
    lines.Add("PRAGMA defer_foreign_keys = on;")
    lines.Add(newCreateSql)
    lines.Add(sprintf "INSERT INTO %s_new (%s) SELECT %s FROM %s;" m.TableName colList selList m.TableName)
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

let runMigrate (metas: TableMeta list) (dryRun: bool) (remote: bool) =
    let metasByName = metas |> List.map (fun m -> m.DisplayName, m) |> Map.ofList
    let dbName = getDbName ()
    let envFlag =
        match System.Environment.GetEnvironmentVariable "HEDGE_SITE" with
        | null | "" -> ""
        | site -> sprintf " --env %s" site
    if dbName = "" then
        printfn "ERROR: Could not find database_name in wrangler.toml"
    else
        printfn "Diffing schema against the %s database (%s)." (if remote then "REMOTE" else "local") dbName
        let currentTables = getCurrentTables remote dbName
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
                let currentCols = getTableColumns remote dbName m.TableName
                let changes = diffTable m currentCols
                // Beyond columns, diff the model's FKs and Gen-managed indexes against the
                // live table — otherwise a FK/unique/index change silently produces no
                // migration (schema evolution weaker than generation).
                let fkChanged = getTableForeignKeys remote dbName m.TableName <> desiredForeignKeys m metasByName
                let currentIdx = getTableIndexNames remote dbName m.TableName
                let desiredIdx = desiredIndexNames m
                let idxToAdd = Set.difference desiredIdx currentIdx
                let idxToDrop = Set.difference currentIdx desiredIdx

                let hasOnlyAdds =
                    not changes.IsEmpty && (changes |> List.forall (function AddColumn _ -> true | _ -> false))
                // Column type/drop changes OR any FK change need a full rebuild (SQLite
                // can't ALTER a constraint); the rebuild also restores the right indexes.
                let needsRecreate = (not changes.IsEmpty && not hasOnlyAdds) || fkChanged

                if needsRecreate || not changes.IsEmpty || not idxToAdd.IsEmpty || not idxToDrop.IsEmpty then
                    hasChanges <- true
                    migrationParts.Add(sprintf "-- Changes to %s" m.TableName)

                    if needsRecreate then
                        // Rebuild reconciles columns, FKs, and indexes in one step.
                        migrationParts.Add(generateRecreateTableSql m currentCols metasByName)
                    else
                        // Additive-only: ADD COLUMN(s), then reconcile indexes directly.
                        for change in changes do
                            match change with
                            | AddColumn(col, typ, notNull) ->
                                migrationParts.Add(generateAddColumnSql m.TableName col typ notNull)
                            | _ -> ()
                        for idx in idxToDrop do
                            migrationParts.Add(sprintf "DROP INDEX %s;" idx)
                        let idxStmts = generateIndexes m
                        for name in idxToAdd do
                            match idxStmts |> List.tryFind (fun s -> s.Contains(sprintf " %s ON " name)) with
                            | Some stmt -> migrationParts.Add stmt
                            | None -> ()

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

            if dryRun then
                printfn "(dry-run: migration file written but not applied)"
            elif remote then
                // Remote is production — never auto-apply. Write the file, and let a
                // human review it and apply deliberately.
                printfn "Review the migration, then apply it yourself:"
                printfn "  npx wrangler d1 migrations apply %s --remote%s" dbName envFlag
            else
                let args = sprintf "wrangler d1 migrations apply %s --local%s" dbName envFlag
                let output = execProcess "npx" args
                printfn "%s" output
                printfn "Migration applied locally."

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
// Module manifest (the site's composition)
// ============================================================

/// The root module — the app's own Models. Always present; other modules are
/// mounted alongside it via gen-modules.json.
let rootModule =
    { Assembly = "Models"; Namespace = "Models"; TablePrefix = ""; RoutePrefix = ""; HandlerNs = "Server.Handlers"; NamePrefix = "" }

/// Read the site's module list from gen-modules.json (in the app dir) if present,
/// else just the root module (byte-identical to the pre-modules single-app path).
/// Each entry: { assembly, namespace, tablePrefix, routePrefix, handlerNs }.
let readModules () : GenModule list =
    // HEDGE_SITE selects a per-site manifest (gen-modules.<site>.json) when one exists,
    // so sites with different module sets (e.g. ndct = articles-only vs justat =
    // articles + blog) each generate their own Routes/Codecs/schema. Falls back to the
    // shared gen-modules.json (the superset, used by dev and by uniform apps).
    let site = System.Environment.GetEnvironmentVariable "HEDGE_SITE"
    let sitePath = if System.String.IsNullOrEmpty site then "" else sprintf "gen-modules.%s.json" site
    let path = if sitePath <> "" && File.Exists sitePath then sitePath else "gen-modules.json"
    if not (File.Exists path) then [ rootModule ]
    else
        let doc = Text.Json.JsonDocument.Parse(File.ReadAllText path)
        [ for el in doc.RootElement.EnumerateArray() ->
            let str name dflt =
                match el.TryGetProperty(name: string) with
                | true, v -> v.GetString()
                | _ -> dflt
            { Assembly = str "assembly" "Models"
              Namespace = str "namespace" "Models"
              TablePrefix = str "tablePrefix" ""
              RoutePrefix = str "routePrefix" ""
              HandlerNs = str "handlerNs" "Server.Handlers"
              NamePrefix = str "namePrefix" "" } ]

// ============================================================
// Main
// ============================================================

[<EntryPoint>]
let main (argv: string array) =
    let modules = readModules ()

    // Step 3+4: reflect each module over its own assembly, baking in its prefixes,
    // then concatenate into the combined lists the generators consume.
    let perModule =
        modules |> List.map (fun m ->
            let asm = Assembly.Load(m.Assembly)
            let domainTypes = discoverDomainTypes m.Namespace asm
            // WS types collide across modules (both have NewCommentEvent), so carry
            // each module's NamePrefix for identifier disambiguation.
            let wsTypes = discoverWsTypes m.Namespace asm |> List.map (fun t -> t, m.NamePrefix)
            let endpoints = discoverApiModules m.Namespace m.NamePrefix asm m.RoutePrefix m.HandlerNs
            let metas = domainTypes |> List.map reflectToParsedType |> List.map (computeMeta m.TablePrefix)
            domainTypes, wsTypes, endpoints, metas)

    let domainTypes = perModule |> List.collect (fun (d, _, _, _) -> d)
    let wsTypes = perModule |> List.collect (fun (_, w, _, _) -> w)
    let endpoints = perModule |> List.collect (fun (_, _, e, _) -> e)
    let metas = perModule |> List.collect (fun (_, _, _, m) -> m)

    // D5 guard: Gen keys FK + topo-sort resolution on the unprefixed domain type
    // short name (DisplayName), so two modules sharing one would silently collapse
    // their tables and emit duplicate types. Keep short names globally unique; fail
    // loudly here rather than miscompile (see notes/MODULES.md, convergence plan).
    match metas |> List.countBy (fun m -> m.DisplayName) |> List.filter (fun (_, n) -> n > 1) with
    | [] -> ()
    | dups ->
        let names = dups |> List.map (fun (name, n) -> sprintf "%s (x%d)" name n) |> String.concat ", "
        failwithf "gen-modules: domain type short-name(s) collide across modules: %s. Rename so every module's domain types are globally unique (see notes/MODULES.md)." names

    // Composing >1 module forces namespace-qualified type refs (colliding module
    // names) and per-module identifier prefixes; single-module stays byte-identical.
    let qualify = List.length modules > 1
    // When not qualifying (single module), the codec/client opens use that module's
    // own namespace — "Models" for the current apps, but any module's ns (e.g. "Blog"
    // for a standalone blog site). Harmless when qualifying (opens aren't emitted).
    let singleNs = match modules with [ m ] -> m.Namespace | _ -> "Models"

    // Generate existing files (Db, AdminGen, schema.sql)
    let admin = generateAdminFs metas
    writeIfChanged "src/Server/generated/AdminGen.fs" admin

    let db = generateDbFs metas
    writeIfChanged "src/Server/generated/Db.fs" db

    let schemaSql = generateSchemaSql metas
    writeIfChanged "schema.sql" schemaSql

    // Step 5: Generate Codecs.fs
    let codecs = generateCodecsFs domainTypes endpoints wsTypes qualify singleNs
    writeIfChanged "src/Codecs/generated/Codecs.fs" codecs

    // Step 6: Generate ClientGen.fs
    let clientGen = generateClientGenFs endpoints wsTypes qualify singleNs
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
    let hasRemote = argv |> Array.exists (fun a -> a = "--remote")

    if hasMigrate then
        runMigrate metas hasDryRun hasRemote

    0
