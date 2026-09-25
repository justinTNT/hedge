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
    // An extracted content module owns its generated surface (committed under
    // packages/modules/<m>/generated), so the site pass emits only glue for it.
    // False for the identity/root module + one-off single-apps (site emits their
    // surface app-local). Its own generated dir, for the module-emit pass.
    Owned: bool
    SurfaceDir: string    // packages/modules/<m> path (Owned only); "" otherwise
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
    elif propType = typeof<EditableDate> then FInt, [EditableDate]
    elif propType = typeof<RichContent> then FString, [RichContent]
    elif propType = typeof<Link> then FString, [Link]
    elif propType = typeof<Image> then FString, [Image]
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
    RequestContext: bool
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

                let contextProp = moduleType.GetProperty("requestContext", BindingFlags.Public ||| BindingFlags.Static)
                let requestContext =
                    if isNull contextProp then false
                    elif contextProp.PropertyType <> typeof<bool> then
                        failwithf "%s.requestContext must be a bool" moduleType.FullName
                    else unbox<bool> (contextProp.GetValue(null))
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
                    RequestContext = requestContext
                })
        |> Array.toList

// ============================================================
// ParsedType — bridge between reflection and existing generators
// ============================================================

type ParsedType = {
    Name: string
    Table: string option
    AdminList: string option
    UniqueTogether: string list option
    Fields: FieldSchema list
}

let reflectToParsedType (t: Type) : ParsedType =
    let tableName =
        match t.GetCustomAttribute<TableAttribute>() with
        | null -> None
        | attr -> Some attr.Name
    let adminList =
        match t.GetCustomAttribute<AdminListAttribute>() with
        | null -> None
        | attr -> Some attr.Query
    let uniqueTogether =
        match t.GetCustomAttribute<UniqueTogetherAttribute>() with
        | null -> None
        | attr -> Some (List.ofArray attr.Fields)
    { Name = t.Name; Table = tableName; AdminList = adminList
      UniqueTogether = uniqueTogether; Fields = getFieldSchemas t }

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
    /// Snake-cased columns of a composite-unique constraint ([<UniqueTogether>]); [] when none.
    UniqueTogether: string list
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
        match parsed.AdminList with
        // [<AdminList>] override: the type supplies the whole tail (WHERE/ORDER/LIMIT) — e.g. a
        // curation queue that filters completed rows out. Replaces the default ordering/limit
        // (and the soft-delete liveFilter — the override must include any filtering it needs).
        | Some tail -> sprintf "SELECT %s FROM %s %s" colStr tableName tail
        | None ->
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

    // Composite-unique columns ([<UniqueTogether>]): validate each named field belongs to this type
    // (fail loud on a typo) and snake_case to column names.
    let uniqueTogetherCols =
        match parsed.UniqueTogether with
        | None -> []
        | Some names ->
            let known = dbFields |> List.map (fun f -> f.Name) |> Set.ofList
            names
            |> List.map (fun n ->
                if not (Set.contains n known) then
                    failwithf "UniqueTogether on %s references unknown field '%s'" displayName n
                toSnakeCase n)

    { DisplayName = displayName; TableName = tableName; Schema = schema
      DbFields = dbFields; Cols = cols; ColStr = colStr
      PkCol = pkCol; HasPk = hasPk; HasCreateTs = hasCreateTs; HasUpdateTs = hasUpdateTs
      MutableFields = mutableFields; MutableCols = mutableCols; FkFields = fkFields
      UniqueTogether = uniqueTogetherCols
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
    | EditableDate -> "EditableDate"
    | ForeignKey table -> sprintf "ForeignKey \"%s\"" table
    | RichContent -> "RichContent"
    | Link -> "Link"
    | Image -> "Image"
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
        "      SupportedOps = [ OpList; OpRead; OpCreate; OpUpdate; OpDelete ]"
        sprintf "      MutableFields = [%s] }" mutableFieldsStr ]

/// `moduleName` is the emitted F# module (site: "Server.AdminGen"; a surface: "Blog.AdminGen").
/// `appendRegistries` are other modules' `tables` lists the site registry appends
/// (e.g. ["Blog.AdminGen.tables"]) after its own (identity) tables; [] for a module surface.
let generateAdminFs (moduleName: string) (appendRegistries: string list) (metas: TableMeta list) : string =
    let lines = ResizeArray<string>()
    let emit s = lines.Add(s)

    emit "// AUTO-GENERATED by src/Gen/Program.fs -- do not edit by hand."
    emit (sprintf "module %s" moduleName)
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

    if appendRegistries.IsEmpty then
        emit "let tables : AdminTable list = ["
        valNames |> List.iter emit
        emit "]"
    else
        // Site registry: identity tables ++ each owned module's own `tables` list,
        // in composition order (preserves the pre-split combined ordering). A
        // separate binding keeps the list's dedented `]` off the `@` continuation
        // (F# offside would reject `] @ …` after a column-0 `]`).
        emit "let private ownTables : AdminTable list = ["
        valNames |> List.iter emit
        emit "]"
        emit (sprintf "let tables : AdminTable list = ownTables @ %s" (String.concat " @ " appendRegistries))
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

        // CP-D: the row id and creation timestamp are CALLER-supplied, not read from the ambient
        // newId()/epochNow() here — so a composed Services can inject a deterministic id/clock and
        // an insert becomes a pure function of its inputs. (Updates still stamp epochNow below.)
        let idParam = if m.HasPk then " (id: string)" else ""
        let nowParam = if m.HasCreateTs then " (now: int)" else ""
        emit ""
        emit (sprintf "let insert%s (db: D1Database)%s%s (create: %sCreate) =" m.DisplayName idParam nowParam m.DisplayName)
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

/// `moduleName` is the emitted F# module (site: "Server.Db"; a module surface: "Blog.Db").
let generateDbFs (moduleName: string) (metas: TableMeta list) : string =
    let lines = ResizeArray<string>()
    let emit s = lines.Add(s)

    emit "// AUTO-GENERATED by src/Gen/Program.fs -- do not edit by hand."
    emit (sprintf "module %s" moduleName)
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

    if not (List.isEmpty m.UniqueTogether) then
        let name = String.concat "_" m.UniqueTogether
        let cols = String.concat ", " m.UniqueTogether
        indexes.Add(sprintf "CREATE UNIQUE INDEX idx_%s_%s ON %s(%s);" m.TableName name m.TableName cols)

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

/// `moduleName` is the emitted F# module (site: "Codecs"; a module surface: "Blog.Codecs").
/// `emitHelpers` emits the unwrap helpers (pk/ct/…) — only in the site "Codecs", never in a
/// module surface, so opening both doesn't double-define them.
let generateCodecsFs (domainTypes: Type list) (endpoints: ParsedEndpoint list) (wsTypes: (Type * string) list) (qualify: bool) (singleNs: string) (moduleName: string) (emitHelpers: bool) : string =
    let domainNames = domainTypes |> List.map (fun t -> toCamelCase t.Name) |> Set.ofList
    let lines = ResizeArray<string>()
    let emit s = lines.Add(s)

    emit "// AUTO-GENERATED by src/Gen/Program.fs -- do not edit by hand."
    emit (sprintf "module %s" moduleName)
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
    if emitHelpers then
        emit "/// Unwrap helpers — terse pattern matches used in Handlers.fs."
        emit "let inline pk (PrimaryKey v) = v"
        emit "let inline ct (CreateTimestamp v) = v"
        emit "let inline ut (UpdateTimestamp v) = v"
        emit "let inline sd (SoftDelete v) = v"
        emit "let inline ed (EditableDate v) = v"
        emit "let inline fk (ForeignKey v) = v"
        emit "let inline rc (RichContent v) = v"
        emit "let inline lk (Link v) = v"
        emit "let inline uq (Unique v) = v"
        emit ""
    emit "module Encode ="
    // A read-only API module can have no domain, request or event encoders.
    // Keep its empty namespace valid F# without inventing a wire contract.
    if domainTypes.IsEmpty && wsTypes.IsEmpty && (endpoints |> List.forall (fun ep -> ep.RequestType.IsNone && ep.ViewTypes.IsEmpty)) then
        emit "    do ()"
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

/// `moduleName` is the emitted F# module (site: "Client.ClientGen"; a surface: "Blog.ClientGen").
/// `codecsModule` is where its decoders live (site: "Codecs"; a surface: "Blog.Codecs").
let generateClientGenFs (endpoints: ParsedEndpoint list) (wsTypes: (Type * string) list) (qualify: bool) (singleNs: string) (moduleName: string) (codecsModule: string) : string =
    let lines = ResizeArray<string>()
    let emit s = lines.Add(s)

    // The (key, value option) expressions for a query record's fields — the transport-neutral
    // Client passes the raw pairs as Request.Query for the adapter to percent-encode.
    let queryItems (recVar: string) (qt: Type) : string list =
        queryFields qt |> List.map (fun f ->
            match f.Kind with
            | QOptString -> sprintf "(match %s.%s with Some v -> Some (\"%s\", v) | None -> None)" recVar f.Name f.Key
            | QReqString -> sprintf "Some (\"%s\", %s.%s)" f.Key recVar f.Name
            | QOptInt    -> sprintf "(match %s.%s with Some v -> Some (\"%s\", string v) | None -> None)" recVar f.Name f.Key
            | QReqInt    -> sprintf "Some (\"%s\", string %s.%s)" f.Key recVar f.Name)

    emit "// AUTO-GENERATED by src/Gen/Program.fs -- do not edit by hand."
    emit (sprintf "module %s" moduleName)
    emit ""
    emit "open Fable.Core"
    emit "open Thoth.Json"
    // Composed modules qualify type refs; single-module opens its own namespace
    // (`singleNs`) for byte-identical output.
    if not qualify then
        emit (sprintf "open %s.Api" singleNs)
        emit (sprintf "open %s.Ws" singleNs)
    emit (sprintf "open %s" codecsModule)
    emit ""

    // Transport-neutral client (C2/C5): a typed record (one field per endpoint) built by
    // `createClient` over an injected Hedge.Http.Transport. Each field constructs a
    // Hedge.Http.Request (relative path, raw query pairs, JSON body) and runs it through
    // Http.sendDecode — no ambient Client.Api, window global or Chrome dependency. This is now
    // the ONLY generated HTTP client surface: the former bare fetchJson/postJson functions (and
    // the `open Client.Api` they needed) were removed in C5 once every consumer — content
    // modules, extension, and the music/basewatch one-off apps — moved onto this record.
    // Guarded on a non-empty endpoint set (an empty F# record is invalid syntax). The
    // Hedge.Http.Request annotation resolves its fields even when a consumer namespace also
    // opens an `Api` with same-named record fields.
    if not (List.isEmpty endpoints) then
        emit ""
        emit "// --- Transport-neutral client (C2) ---"
        emit ""
        emit "type Client = {"
        for ep in endpoints do
            let funcName = genName ep.NamePrefix ep.ModuleName
            let resultTy = sprintf "JS.Promise<Result<%s, Hedge.Http.ApiError>>" (apiTypeRef qualify ep "Response")
            let argTy =
                match ep.Method with
                | EGet -> "unit"
                | EGetBy -> "string"
                | EGetQuery -> apiTypeRef qualify ep "Query"
                | EGetByQuery -> sprintf "string -> %s" (apiTypeRef qualify ep "Query")
                | EPost -> apiTypeRef qualify ep "Request"
            emit (sprintf "    %s: %s -> %s" funcName argTy resultTy)
        emit "}"
        emit ""
        emit "let createClient (transport: Hedge.Http.Transport) : Client = {"
        for ep in endpoints do
            let funcName = genName ep.NamePrefix ep.ModuleName
            let respName = genName ep.NamePrefix ep.ModuleName + "Response"
            let reqName = genName ep.NamePrefix ep.ModuleName + "Req"
            match ep.Method with
            | EGet ->
                emit (sprintf "    %s = fun () -> Hedge.Http.sendDecode transport ({ Method = \"GET\"; Path = \"%s\"; Query = []; Headers = []; Body = None }: Hedge.Http.Request) Decode.%s" funcName ep.Path respName)
            | EGetBy ->
                let pathTemplate = ep.Path.Replace(":id", "%s")
                emit (sprintf "    %s = fun id -> Hedge.Http.sendDecode transport ({ Method = \"GET\"; Path = (sprintf \"%s\" id); Query = []; Headers = []; Body = None }: Hedge.Http.Request) Decode.%s" funcName pathTemplate respName)
            | EGetQuery ->
                let pairs = "List.choose (fun p -> p) [ " + String.concat "; " (queryItems "query" ep.QueryType.Value) + " ]"
                emit (sprintf "    %s = fun query -> Hedge.Http.sendDecode transport ({ Method = \"GET\"; Path = \"%s\"; Query = (%s); Headers = []; Body = None }: Hedge.Http.Request) Decode.%s" funcName ep.Path pairs respName)
            | EGetByQuery ->
                let pathTemplate = ep.Path.Replace(":id", "%s")
                let pairs = "List.choose (fun p -> p) [ " + String.concat "; " (queryItems "query" ep.QueryType.Value) + " ]"
                emit (sprintf "    %s = fun id query -> Hedge.Http.sendDecode transport ({ Method = \"GET\"; Path = (sprintf \"%s\" id); Query = (%s); Headers = []; Body = None }: Hedge.Http.Request) Decode.%s" funcName pathTemplate pairs respName)
            | EPost ->
                emit (sprintf "    %s = fun req -> Hedge.Http.sendDecode transport ({ Method = \"POST\"; Path = \"%s\"; Query = []; Headers = []; Body = Some (Encode.%s req |> Encode.toString 0) }: Hedge.Http.Request) Decode.%s" funcName ep.Path reqName respName)
        emit "}"

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

/// `extraCodecOpens` are the owned modules' Codecs modules (e.g. ["Blog.Codecs"]) —
/// the site dispatch references their POST-body decoders, which now live in the module
/// surface rather than the combined site Codecs.
/// Construct a `'query` record from the request's query string. Optional fields map an
/// absent/empty param to None; required fields to a sensible default. Shared by the one-off
/// site dispatch and the per-module RouteContract dispatch.
let private queryRecordExpr (qt: Type) (typeRef: string) =
    let fields =
        queryFields qt |> List.map (fun f ->
            let raw = sprintf "(getQueryParam request.url \"%s\")" f.Key
            match f.Kind with
            | QOptString -> sprintf "%s = (let v = %s in if isNull v || v = \"\" then None else Some v)" f.Name raw
            | QReqString -> sprintf "%s = (let v = %s in if isNull v then \"\" else v)" f.Name raw
            | QOptInt    -> sprintf "%s = (let v = %s in if isNull v || v = \"\" then None else Some (int v))" f.Name raw
            | QReqInt    -> sprintf "%s = (let v = %s in if isNull v || v = \"\" then 0 else int v)" f.Name raw)
    sprintf "({ %s } : %s)" (String.concat "; " fields) typeRef
let private queryTypeRef (ep: ParsedEndpoint) = sprintf "%s.Api.%s.Query" ep.Namespace ep.ModuleName

/// C3 — a module's route contract: a `Handlers` record (one delegate per endpoint, each closed
/// over the module's Services by an authored Composition.bind) and a `dispatch` that decodes
/// path/query/body and invokes those delegates. Imports framework + module types only, never
/// Server.Env — so any host (or the HostProbe) can drive the module's routes with its own
/// environment. Emitted per module into its generated surface; the site Routes composes these.
let generateRouteContractFs (ns: string) (endpoints: ParsedEndpoint list) : string =
    let lines = ResizeArray<string>()
    let emit s = lines.Add(s)

    emit "// AUTO-GENERATED by src/Gen/Program.fs -- do not edit by hand."
    emit (sprintf "module %s.RouteContract" ns)
    emit ""
    emit "open Fable.Core"
    emit "open Thoth.Json"
    emit "open Hedge.Workers"
    emit "open Hedge.Router"
    emit (sprintf "open %s.Codecs" ns)
    emit ""
    emit "// The decoded-argument delegates for each endpoint, bound by Composition.bind over the"
    emit "// module's Services. No Server.Env dependency: the host supplies behaviour via Services."
    emit "type Handlers = {"
    // An API-less module (admin+cron only, e.g. alerts) has no endpoints. F# has no empty record,
    // so emit a single unit placeholder; dispatch's `| _ -> None` below already covers it.
    if List.isEmpty endpoints then
        emit "    /// No HTTP endpoints — this module contributes no routes (dispatch always returns"
        emit "    /// None). The field exists only because F# has no empty record."
        emit "    NoEndpoints: unit"
    for ep in endpoints do
        let name = toCamelCase ep.ModuleName
        let resp = if ep.RequestContext && ep.Method <> EPost then "WorkerRequest -> ExecutionContext -> JS.Promise<WorkerResponse>" else "JS.Promise<WorkerResponse>"
        let sigStr =
            match ep.Method with
            | EGet -> sprintf "unit -> %s" resp
            | EGetBy -> sprintf "string -> %s" resp
            | EGetQuery -> sprintf "%s -> %s" (apiTypeRef true ep "Query") resp
            | EGetByQuery -> sprintf "string -> %s -> %s" (apiTypeRef true ep "Query") resp
            | EPost -> sprintf "%s -> WorkerRequest -> ExecutionContext -> %s" (apiTypeRef true ep "Request") resp
        emit (sprintf "    %s: %s" name sigStr)
    emit "}"
    emit ""
    emit "let dispatch (handlers: Handlers) (request: WorkerRequest) (ctx: ExecutionContext)"
    emit "    : JS.Promise<WorkerResponse> option ="
    emit "    let route = parseRoute request"
    emit "    match route with"

    for ep in endpoints |> List.filter (fun ep -> ep.Method = EGet) do
        let name = toCamelCase ep.ModuleName
        emit (sprintf "    | GET path when matchPath \"%s\" path = Some (Exact \"%s\") ->" ep.Path ep.Path)
        emit (sprintf "        Some (handlers.%s ()%s)" name (if ep.RequestContext then " request ctx" else ""))
        emit ""

    for ep in endpoints |> List.filter (fun ep -> ep.Method = EGetQuery) do
        let name = toCamelCase ep.ModuleName
        emit (sprintf "    | GET path when matchPath \"%s\" path = Some (Exact \"%s\") ->" ep.Path ep.Path)
        emit (sprintf "        let query = %s" (queryRecordExpr ep.QueryType.Value (queryTypeRef ep)))
        emit (sprintf "        Some (handlers.%s query%s)" name (if ep.RequestContext then " request ctx" else ""))
        emit ""

    let getOnes = endpoints |> List.filter (fun ep -> ep.Method = EGetBy || ep.Method = EGetByQuery)
    if not getOnes.IsEmpty then
        emit "    | GET path ->"
        for ep in getOnes do
            let name = toCamelCase ep.ModuleName
            emit (sprintf "        match matchPath \"%s\" path with" ep.Path)
            match ep.Method with
            | EGetByQuery ->
                emit "        | Some (WithParam (_, id)) ->"
                emit (sprintf "            let query = %s" (queryRecordExpr ep.QueryType.Value (queryTypeRef ep)))
                emit (sprintf "            Some (handlers.%s id query%s)" name (if ep.RequestContext then " request ctx" else ""))
            | _ ->
                emit (sprintf "        | Some (WithParam (_, id)) -> Some (handlers.%s id%s)" name (if ep.RequestContext then " request ctx" else ""))
            emit "        | _ ->"
        emit "        None"
        emit ""

    for ep in endpoints |> List.filter (fun ep -> ep.Method = EPost) do
        let name = toCamelCase ep.ModuleName
        let decoderName = genName ep.NamePrefix ep.ModuleName + "Req"
        emit (sprintf "    | POST path when matchPath \"%s\" path = Some (Exact \"%s\") ->" ep.Path ep.Path)
        emit "        Some (promise {"
        emit "            let! bodyText = request.text()"
        emit (sprintf "            match Decode.fromString Decode.%s bodyText with" decoderName)
        emit "            | Error err -> return badRequest err"
        emit "            | Ok req ->"
        emit (sprintf "                return! handlers.%s req request ctx" name)
        emit "        })"
        emit ""

    emit "    | _ -> None"
    emit ""
    lines |> String.concat "\n"

/// The site's combined dispatch. For a content app (`ownedModuleNamespaces` non-empty) it is a
/// thin composer over each owned module's generated RouteContract.dispatch, taking the handler
/// records the app binds from its environment — no Server.Env here. For a one-off app (no owned
/// modules) it keeps the pre-C3 env-based inline dispatch, byte-identical to before.
let generateRoutesFs (extraCodecOpens: string list) (ownedModuleNamespaces: string list) (endpoints: ParsedEndpoint list) : string =
    let lines = ResizeArray<string>()
    let emit s = lines.Add(s)

    emit "// AUTO-GENERATED by src/Gen/Program.fs -- do not edit by hand."
    emit "module Server.Routes"
    emit ""

    match ownedModuleNamespaces with
    | [] ->
        emit "open Fable.Core"
        emit "open Thoth.Json"
        emit "open Hedge.Workers"
        emit "open Hedge.Router"
        emit "open Codecs"
        for op in extraCodecOpens do emit (sprintf "open %s" op)
        emit "open Server.Env"
        emit ""
        emit "let dispatch (request: WorkerRequest) (env: Env) (ctx: ExecutionContext)"
        emit "    : JS.Promise<WorkerResponse> option ="
        emit "    let route = parseRoute request"
        emit "    match route with"

        // GET exact routes (no query)
        let getExacts = endpoints |> List.filter (fun ep -> ep.Method = EGet)
        for ep in getExacts do
            let handlerName = toCamelCase ep.ModuleName
            emit (sprintf "    | GET path when matchPath \"%s\" path = Some (Exact \"%s\") ->" ep.Path ep.Path)
            emit (sprintf "        Some (%s.%s %s)" ep.HandlerNs handlerName (if ep.RequestContext then "request env ctx" else "env"))
            emit ""

        // GET exact routes with a typed query (parse the query record, pass it in)
        let getQueries = endpoints |> List.filter (fun ep -> ep.Method = EGetQuery)
        for ep in getQueries do
            let handlerName = toCamelCase ep.ModuleName
            emit (sprintf "    | GET path when matchPath \"%s\" path = Some (Exact \"%s\") ->" ep.Path ep.Path)
            emit (sprintf "        let query = %s" (queryRecordExpr ep.QueryType.Value (queryTypeRef ep)))
            emit (sprintf "        Some (%s.%s query %s)" ep.HandlerNs handlerName (if ep.RequestContext then "request env ctx" else "env"))
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
                    emit (sprintf "            Some (%s.%s id query %s)" ep.HandlerNs handlerName (if ep.RequestContext then "request env ctx" else "env"))
                | _ ->
                    emit (sprintf "        | Some (WithParam (_, id)) -> Some (%s.%s id %s)" ep.HandlerNs handlerName (if ep.RequestContext then "request env ctx" else "env"))
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
    | owned ->
        emit "open Fable.Core"
        emit "open Hedge.Workers"
        emit ""
        let paramName ns = (toCamelCase ns) + "Handlers"
        let sigParams = owned |> List.map (fun ns -> sprintf "(%s: %s.RouteContract.Handlers)" (paramName ns) ns) |> String.concat " "
        emit (sprintf "let dispatch %s (request: WorkerRequest) (ctx: ExecutionContext)" sigParams)
        emit "    : JS.Promise<WorkerResponse> option ="
        match owned with
        | first :: rest ->
            emit (sprintf "    %s.RouteContract.dispatch %s request ctx" first (paramName first))
            for ns in rest do
                emit (sprintf "    |> Option.orElseWith (fun () -> %s.RouteContract.dispatch %s request ctx)" ns (paramName ns))
        | [] -> emit "    None"
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
            emit (sprintf "let %s %s : JS.Promise<WorkerResponse> =" handlerName (if ep.RequestContext then "(request: WorkerRequest) (env: Env) (ctx: ExecutionContext)" else "(env: Env)"))
            emit "    promise {"
            emit "        // TODO: implement"
            emit "        return notFound ()"
            emit "    }"
            emit ""
        | EGetBy ->
            emit (sprintf "let %s (id: string) %s : JS.Promise<WorkerResponse> =" handlerName (if ep.RequestContext then "(request: WorkerRequest) (env: Env) (ctx: ExecutionContext)" else "(env: Env)"))
            emit "    promise {"
            emit "        // TODO: implement"
            emit "        return notFound ()"
            emit "    }"
            emit ""
        | EGetQuery ->
            emit (sprintf "let %s (query: %s.Query) %s : JS.Promise<WorkerResponse> =" handlerName ep.ModuleName (if ep.RequestContext then "(request: WorkerRequest) (env: Env) (ctx: ExecutionContext)" else "(env: Env)"))
            emit "    promise {"
            emit "        // TODO: implement"
            emit "        return notFound ()"
            emit "    }"
            emit ""
        | EGetByQuery ->
            emit (sprintf "let %s (id: string) (query: %s.Query) %s : JS.Promise<WorkerResponse> =" handlerName ep.ModuleName (if ep.RequestContext then "(request: WorkerRequest) (env: Env) (ctx: ExecutionContext)" else "(env: Env)"))
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

let private hedgeSite () = System.Environment.GetEnvironmentVariable "HEDGE_SITE"

/// " --env <site>" for every wrangler invocation (introspection AND apply) when
/// HEDGE_SITE is set, else "". A site's DB lives under [env.<site>], so the flag is
/// required for wrangler to resolve it — omitting it on introspection is exactly the
/// ndct "Couldn't find a D1 DB 'ndct-db'" failure.
let siteEnvFlag () =
    match hedgeSite () with
    | s when System.String.IsNullOrEmpty s -> ""
    | s -> sprintf " --env %s" s

/// True when wrangler.toml declares [env.<site>] (a real deployment target).
let private wranglerEnvExists (site: string) : bool =
    File.Exists "wrangler.toml"
    && (File.ReadAllLines "wrangler.toml"
        |> Array.exists (fun l ->
            let t = l.TrimStart()
            t.StartsWith(sprintf "[env.%s]" site) || t.StartsWith(sprintf "[env.%s." site)))

/// Fail early on a HEDGE_SITE that names no real target: a typo (e.g. `ndtc`) would
/// otherwise silently fall back to the default module set + database. A site is known
/// if it has a gen-modules.<site>.json (its own composition) or an [env.<site>] target.
let validateSite () =
    match hedgeSite () with
    | s when System.String.IsNullOrEmpty s -> ()
    | s ->
        if not (File.Exists(sprintf "gen-modules.%s.json" s)) && not (wranglerEnvExists s) then
            failwithf "Unknown HEDGE_SITE '%s': no gen-modules.%s.json and no [env.%s] in wrangler.toml (a typo would silently use the default module set + database)." s s s

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
    // --env <site> is required for wrangler to resolve a per-[env.<site>] database.
    let args = sprintf "wrangler d1 execute %s%s %s --command \"%s\" --json" dbName (siteEnvFlag ()) target escaped
    let output = execProcess "npx" args
    // npx/wrangler can wrap the JSON in banners or update notices on stdout — notably on
    // the FIRST (cold) invocation in a run, which used to crash the diff with a
    // KeyNotFoundException. Isolate the JSON payload: from the first '[' or '{' to the
    // matching last bracket, dropping any leading/trailing noise before parsing.
    let jsonText =
        let starts = [ output.IndexOf('['); output.IndexOf('{') ] |> List.filter (fun i -> i >= 0)
        match starts with
        | [] -> failwithf "wrangler returned no JSON (is it authenticated?). Raw output:\n%s" output
        | _ ->
            let s = List.min starts
            let closeCh = if output.[s] = '[' then ']' else '}'
            let e = output.LastIndexOf(closeCh)
            if e > s then output.Substring(s, e - s + 1) else output.Substring(s)
    let doc = Text.Json.JsonDocument.Parse(jsonText)
    // wrangler's `d1 execute --json` shape varies by version/target: --local wraps the
    // statement result in an array ([{ results, … }]), --remote has returned the bare
    // object ({ results, … }), and some versions nest under { result: [ … ] }. Find the
    // `results` array wherever it sits rather than assuming a fixed shape.
    let rec findResults (el: Text.Json.JsonElement) : Text.Json.JsonElement option =
        match el.ValueKind with
        | Text.Json.JsonValueKind.Object ->
            match el.TryGetProperty("results") with
            | true, r when r.ValueKind = Text.Json.JsonValueKind.Array -> Some r
            | _ -> el.EnumerateObject() |> Seq.tryPick (fun p -> findResults p.Value)
        | Text.Json.JsonValueKind.Array ->
            el.EnumerateArray() |> Seq.tryPick findResults
        | _ -> None
    match findResults doc.RootElement with
    | Some results -> [| for i in 0 .. results.GetArrayLength() - 1 -> results.[i] |]
    | None -> failwithf "wrangler JSON had no 'results' array. Payload:\n%s" jsonText

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
      if not (List.isEmpty m.UniqueTogether) then
          yield sprintf "idx_%s_%s" m.TableName (String.concat "_" m.UniqueTogether)
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
    // Point a table's OWN self-referential FK at its <t>_new during the rebuild window.
    // generateCreateTable emits self-FKs as `REFERENCES <table>(...)`; the trailing "("
    // guards against prefix collisions (REFERENCES item( never matches REFERENCES items().
    // Without this the self-FK still names the live table, and DROPping it below leaves
    // <t>_new pointing at a table that's about to vanish — the deferred check then fails
    // at COMMIT even though PRAGMA foreign_key_check is clean (SQLite counts the drop as a
    // pending violation that the rename does not clear). Cross-table FKs name other tables
    // and are left untouched.
    let rewriteSelfRef (tableName: string) (ddl: string) =
        ddl.Replace(sprintf "REFERENCES %s(" tableName, sprintf "REFERENCES %s_new(" tableName)

    // Emit <t>_new + populate + drop-source + rename + indexes for one table, copying from
    // `sourceTable` (the live table for the parent; a *_bak snapshot for a child).
    let rebuildOne (t: TableMeta) (sourceTable: string) (insertColList: string) (selectList: string) : string list =
        let newDdl =
            (generateCreateTable t metasByName)
                .Replace(sprintf "CREATE TABLE %s" t.TableName, sprintf "CREATE TABLE %s_new" t.TableName)
            |> rewriteSelfRef t.TableName
        [ yield newDdl
          yield sprintf "INSERT INTO %s_new (%s) SELECT %s FROM %s;" t.TableName insertColList selectList sourceTable
          yield sprintf "DROP TABLE %s;" sourceTable
          yield sprintf "ALTER TABLE %s_new RENAME TO %s;" t.TableName t.TableName
          yield! generateIndexes t ]

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

    // Tables that reference m: DROPping a populated parent orphans their rows, which the
    // deferred check catches at COMMIT (a clean foreign_key_check notwithstanding). Rebuild
    // the whole cluster in one pass — stash + drop the children first so nothing references
    // the parent while it's rebuilt, then recreate the children from their snapshots. A
    // self-FK on m alone (the estate's real trigger: comments.parent_id) needs no children,
    // just the rewrite above.
    let allMetas = metasByName |> Map.toList |> List.map snd |> List.distinctBy (fun t -> t.TableName)
    let referencesTable (t: TableMeta) (target: string) =
        desiredForeignKeys t metasByName |> Set.exists (fun (_, r) -> r = target)
    let children =
        allMetas |> List.filter (fun c -> c.TableName <> m.TableName && referencesTable c m.TableName)

    // The single-level cluster (parent + its direct children) covers the estate. A referencer
    // OUTSIDE the cluster pointing into it (a grandchild) or a child referencing another child
    // (not the parent) would need a wider/ordered rebuild this doesn't do — such SQL would
    // fail at COMMIT, so flag it loudly rather than ship a migration that silently breaks.
    let clusterNames = Set.ofList (m.TableName :: (children |> List.map (fun c -> c.TableName)))
    let externalReferencers =
        allMetas
        |> List.filter (fun t -> not (clusterNames.Contains t.TableName))
        |> List.filter (fun t -> desiredForeignKeys t metasByName |> Set.exists (fun (_, r) -> clusterNames.Contains r))
        |> List.map (fun t -> t.TableName)
    let interChildRefs =
        children
        |> List.filter (fun c ->
            desiredForeignKeys c metasByName
            |> Set.exists (fun (_, r) -> clusterNames.Contains r && r <> m.TableName && r <> c.TableName))
        |> List.map (fun c -> c.TableName)
    let unhandled = (externalReferencers @ interChildRefs) |> List.distinct

    let lines = ResizeArray<string>()
    if not unhandled.IsEmpty then
        lines.Add(sprintf "-- !!! WARNING: %s has referencing table(s) this auto-migration cannot" m.TableName)
        lines.Add(sprintf "-- !!! rebuild safely in one transaction: %s." (String.concat ", " unhandled))
        lines.Add("-- !!! (a table outside the parent+direct-children cluster references into it, or")
        lines.Add("-- !!!  two children reference each other). The SQL below WILL fail at COMMIT on a")
        lines.Add("-- !!!  populated DB. Rebuild by hand: snapshot every affected table, drop them")
        lines.Add("-- !!!  children-first, recreate parents-first, restore data, then verify")
        lines.Add("-- !!!  PRAGMA foreign_key_check is clean before applying.")
        lines.Add("")
    // Defer FK enforcement to the migration's (implicit) transaction commit so the
    // DROP + RENAME don't trip constraints mid-rebuild on a populated DB. This is D1's
    // documented mechanism — it runs migrations in an implicit transaction and forbids
    // toggling foreign_keys inside one
    // (https://developers.cloudflare.com/d1/sql-api/foreign-keys/). Any real violation
    // still surfaces at commit, so review + test a recreate before applying it remotely
    // (remote migrations are never auto-applied).
    lines.Add("PRAGMA defer_foreign_keys = on;")

    // Stash + drop children (children-first) so nothing references the parent as it rebuilds.
    for c in children do
        lines.Add(sprintf "CREATE TABLE %s_bak AS SELECT * FROM %s;" c.TableName c.TableName)
        lines.Add(sprintf "DROP TABLE %s;" c.TableName)

    // Rebuild the parent from its live table (with the NOT-NULL-seeding column mapping).
    lines.AddRange(rebuildOne m m.TableName colList selList)

    // Recreate each child from its snapshot (children are assumed in sync with their model;
    // their own schema drift is handled by their own recreate, not here).
    for c in children do
        let childCols = c.DbFields |> List.map (fun f -> toSnakeCase f.Name) |> String.concat ", "
        lines.AddRange(rebuildOne c (sprintf "%s_bak" c.TableName) childCols childCols)

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
    let envFlag = siteEnvFlag ()   // introspection + apply target the same site
    let siteLabel = match hedgeSite () with s when System.String.IsNullOrEmpty s -> "default" | s -> s
    if dbName = "" then
        printfn "ERROR: Could not find database_name for site '%s' in wrangler.toml" siteLabel
    else
        printfn "Diffing schema against the %s database (%s, site: %s)." (if remote then "REMOTE" else "local") dbName siteLabel
        let currentTables = getCurrentTables remote dbName
        let currentTableSet = currentTables |> Set.ofList

        let migrationParts = ResizeArray<string>()
        // Label the target so a generated migration is self-evidently for one site/db,
        // not silently anonymous (the migrations/ dir is shared across envs today).
        migrationParts.Add(sprintf "-- Auto-generated migration for site: %s (db: %s)" siteLabel dbName)
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
    { Assembly = "Models"; Namespace = "Models"; TablePrefix = ""; RoutePrefix = ""
      HandlerNs = "Server.Handlers"; NamePrefix = ""; Owned = false; SurfaceDir = "" }

/// Read a content module's fixed identity from its own module.json — the single
/// source of truth for its prefixes, so its generated surface is host-invariant
/// (no host can supply different prefixes). `dir` is the module path relative to
/// the app dir (e.g. "../../packages/modules/blog").
let readModuleManifest (dir: string) : GenModule =
    let doc = Text.Json.JsonDocument.Parse(File.ReadAllText (Path.Combine(dir, "module.json")))
    let el = doc.RootElement
    let str name dflt = match el.TryGetProperty(name: string) with true, v -> v.GetString() | _ -> dflt
    { Assembly = str "assembly" "Models"
      Namespace = str "namespace" "Models"
      TablePrefix = str "tablePrefix" ""
      RoutePrefix = str "routePrefix" ""
      HandlerNs = str "handlerNs" "Server.Handlers"
      NamePrefix = str "namePrefix" ""
      Owned = true
      SurfaceDir = dir }

/// Read the site's composition from gen-modules.json (in the app dir) if present,
/// else just the root module (byte-identical to the pre-modules single-app path).
/// New composition-list format — each entry is one of:
///   { "identity": true[, "assembly", "namespace"] }  -> the shared identity/root base
///   { "module": "../../packages/modules/blog"[, "primary": true] } -> a module ref,
///        expanded from that module's module.json (its fixed prefixes)
/// The legacy explicit-field format ({ assembly, namespace, tablePrefix, ... }) is
/// still accepted for one-off apps that inline their module.
let readModules () : GenModule list =
    // HEDGE_SITE selects a per-site manifest (gen-modules.<site>.json) when one exists,
    // so sites with different module sets (e.g. ndct = articles-only vs justat =
    // articles + blog) each generate their own Routes/schema. Falls back to the
    // shared gen-modules.json (the superset, used by dev and by uniform apps).
    let site = System.Environment.GetEnvironmentVariable "HEDGE_SITE"
    let sitePath = if System.String.IsNullOrEmpty site then "" else sprintf "gen-modules.%s.json" site
    let path = if sitePath <> "" && File.Exists sitePath then sitePath else "gen-modules.json"
    if not (File.Exists path) then [ rootModule ]
    else
        let doc = Text.Json.JsonDocument.Parse(File.ReadAllText path)
        [ for el in doc.RootElement.EnumerateArray() ->
            let str name dflt = match el.TryGetProperty(name: string) with true, v -> v.GetString() | _ -> dflt
            let has name = match el.TryGetProperty(name: string) with true, _ -> true | _ -> false
            if has "module" then
                readModuleManifest (str "module" "")
            elif has "identity" then
                { rootModule with Assembly = str "assembly" "Models"; Namespace = str "namespace" "Models" }
            else
                // Legacy explicit-field entry (inline module, e.g. a one-off app).
                { Assembly = str "assembly" "Models"
                  Namespace = str "namespace" "Models"
                  TablePrefix = str "tablePrefix" ""
                  RoutePrefix = str "routePrefix" ""
                  HandlerNs = str "handlerNs" "Server.Handlers"
                  NamePrefix = str "namePrefix" ""
                  Owned = false
                  SurfaceDir = "" } ]

// ============================================================
// Reflect one module (shared by the module-emit and site-emit passes)
// ============================================================

/// Reflect a module over its Models assembly, baking in its prefixes.
let reflectModule (m: GenModule) =
    let asm = Assembly.Load(m.Assembly)
    let domainTypes = discoverDomainTypes m.Namespace asm
    // WS types collide across modules (both have NewCommentEvent), so carry
    // each module's NamePrefix for identifier disambiguation.
    let wsTypes = discoverWsTypes m.Namespace asm |> List.map (fun t -> t, m.NamePrefix)
    let endpoints = discoverApiModules m.Namespace m.NamePrefix asm m.RoutePrefix m.HandlerNs
    let metas = domainTypes |> List.map reflectToParsedType |> List.map (computeMeta m.TablePrefix)
    domainTypes, wsTypes, endpoints, metas

/// Module-emit pass: write a content module's host-invariant generated surface
/// (Blog.Codecs / Blog.Db / Blog.ClientGen / Blog.AdminGen) into its own
/// packages/modules/<m>/generated/. Always `qualify=true` (a surface is fully
/// qualified regardless of composition) and no unwrap helpers (those live once in
/// the site "Codecs"). Run from an app dir that composes the module (so its Models
/// assembly loads): `dotnet run --project src/Gen/Gen.fsproj -- module <path>`.
let emitModuleSurface (m: GenModule) =
    let domainTypes, wsTypes, endpoints, metas = reflectModule m
    let ns = m.Namespace          // e.g. "Blog"
    let dir = m.SurfaceDir
    writeIfChanged (Path.Combine(dir, "generated/Codecs.fs"))
        (generateCodecsFs domainTypes endpoints wsTypes true ns (ns + ".Codecs") false)
    writeIfChanged (Path.Combine(dir, "generated/Db.fs"))
        (generateDbFs (ns + ".Db") metas)
    writeIfChanged (Path.Combine(dir, "generated/ClientGen.fs"))
        (generateClientGenFs endpoints wsTypes true ns (ns + ".ClientGen") (ns + ".Codecs"))
    writeIfChanged (Path.Combine(dir, "generated/AdminGen.fs"))
        (generateAdminFs (ns + ".AdminGen") [] metas)
    // C3: the module's route contract (Handlers record + dispatch), decoupled from Server.Env.
    writeIfChanged (Path.Combine(dir, "generated/RouteContract.fs"))
        (generateRouteContractFs ns endpoints)
    printfn "Module surface: %s -> %s/generated (%d types, %d endpoints)" ns dir (List.length domainTypes) (List.length endpoints)

// ============================================================
// Main
// ============================================================

let private runSite (argv: string array) =
    validateSite ()   // reject an unknown HEDGE_SITE before generating anything
    let modules = readModules ()

    // Step 3+4: reflect each module, then concatenate into the combined lists.
    let perModule = modules |> List.map reflectModule

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

    // Split: an OWNED content module emits its own surface (compiled from
    // packages/modules/<m>/generated), so the site emits Codecs/Db/ClientGen for the
    // identity/root slice only + a thin AdminGen registry that appends each owned
    // module's `tables`. schema.sql + Routes stay COMBINED (cross-module FKs, one
    // dispatch). One-off single-apps have no owned modules -> identity slice = the
    // whole app, so their output is byte-identical to before.
    let ownedModules = modules |> List.filter (fun m -> m.Owned)
    let idPer = modules |> List.filter (fun m -> not m.Owned) |> List.map reflectModule
    let idDomainTypes = idPer |> List.collect (fun (d, _, _, _) -> d)
    let idWsTypes = idPer |> List.collect (fun (_, w, _, _) -> w)
    let idEndpoints = idPer |> List.collect (fun (_, _, e, _) -> e)
    let idMetas = idPer |> List.collect (fun (_, _, _, m) -> m)
    let ownedCodecs = ownedModules |> List.map (fun m -> m.Namespace + ".Codecs")
    let ownedRegistries = ownedModules |> List.map (fun m -> m.Namespace + ".AdminGen.tables")

    // Per-composition glue (Routes / AdminGen registry / schema.sql) differs by which
    // modules a site composes, so when a site-specific manifest (gen-modules.<site>.json)
    // is in use, write it to a <name>.<site> committed path. The default site (no such
    // manifest, e.g. justat = the superset) keeps the plain path. This makes each site's
    // deployed glue a committed, gen-stable artifact and lets `HEDGE_SITE=ndct gen` avoid
    // clobbering the default — no regen-swap-restore dance. The identity slice
    // (Codecs/Db/ClientGen) is site-invariant, so it always keeps its default path.
    let site = System.Environment.GetEnvironmentVariable "HEDGE_SITE"
    let siteSuffix =
        if not (System.String.IsNullOrEmpty site) && File.Exists (sprintf "gen-modules.%s.json" site)
        then "." + site else ""

    // AdminGen — identity tables + each owned module's registry (composition order).
    let admin = generateAdminFs "Server.AdminGen" ownedRegistries idMetas
    writeIfChanged (sprintf "src/Server/generated/AdminGen%s.fs" siteSuffix) admin

    // Db — identity slice (site-invariant; owned modules ship Blog.Db etc.).
    let db = generateDbFs "Server.Db" idMetas
    writeIfChanged "src/Server/generated/Db.fs" db

    // schema.sql — combined (cross-module FKs + topo span all modules).
    let schemaSql = generateSchemaSql metas
    writeIfChanged (sprintf "schema%s.sql" siteSuffix) schemaSql

    // Codecs — identity slice (site-invariant) + the shared unwrap helpers.
    let codecs = generateCodecsFs idDomainTypes idEndpoints idWsTypes qualify singleNs "Codecs" true
    writeIfChanged "src/Codecs/generated/Codecs.fs" codecs

    // ClientGen — identity slice (site-invariant; owned modules ship Blog.ClientGen etc.).
    let clientGen = generateClientGenFs idEndpoints idWsTypes qualify singleNs "Client.ClientGen" "Codecs"
    writeIfChanged "src/Client/generated/ClientGen.fs" clientGen

    // Routes — content apps: a thin composer over each owned module's RouteContract.dispatch
    // (Server.Env-free); one-off apps: the pre-C3 env-based inline dispatch (empty owned list).
    let routes = generateRoutesFs ownedCodecs (ownedModules |> List.map (fun m -> m.Namespace)) endpoints
    writeIfChanged (sprintf "src/Server/generated/Routes%s.fs" siteSuffix) routes

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

/// Print the recreate migration SQL for one domain type without touching D1 — the
/// live columns are synthesized from the model (a FK-only change, the estate's real
/// recreate trigger, leaves columns identical). Lets a local SQLite test apply the
/// generator's ACTUAL output for a populated parent+children rebuild (test/recreate).
let private runEmitRecreate (displayName: string) =
    validateSite ()
    let metas = readModules () |> List.collect (fun mo -> let _, _, _, ms = reflectModule mo in ms)
    let metasByName = metas |> List.map (fun m -> m.DisplayName, m) |> Map.ofList
    match metasByName |> Map.tryFind displayName with
    | None ->
        eprintfn "emit-recreate: no domain type '%s' in this composition (have: %s)"
            displayName (metas |> List.map (fun m -> m.DisplayName) |> String.concat ", ")
        1
    | Some m ->
        let currentCols =
            m.DbFields
            |> List.map (fun f ->
                { ColName = toSnakeCase f.Name
                  ColType = sqlType f.Type
                  NotNull = not (isNullable f) && not (isPrimaryKey f)
                  IsPk = isPrimaryKey f })
        printfn "%s" (generateRecreateTableSql m currentCols metasByName)
        0

/// Read-only deploy guardrail: diff the live (remote/local) schema for the current
/// HEDGE_SITE against the model and return a non-zero exit code if any change is
/// pending — WITHOUT writing a migration file or regenerating source. `predeploy`
/// runs this so a deploy can't ship code whose schema isn't live yet (e.g. a tenant
/// missing the grants table → /api/admin/Grant 500). Blocking only; the actual
/// migration stays a deliberate human step (see estate-migration-rollout). The
/// per-table diff conditions mirror runMigrate's — keep the two in sync.
let private runCheck (remote: bool) : int =
    validateSite ()
    let metas = readModules () |> List.collect (fun mo -> let _, _, _, ms = reflectModule mo in ms)
    let metasByName = metas |> List.map (fun m -> m.DisplayName, m) |> Map.ofList
    let dbName = getDbName ()
    let siteLabel = match hedgeSite () with s when System.String.IsNullOrEmpty s -> "default" | s -> s
    if dbName = "" then
        eprintfn "check: could not find database_name for site '%s' in wrangler.toml" siteLabel
        1
    else
        printfn "Schema check: %s database %s (site: %s)." (if remote then "REMOTE" else "local") dbName siteLabel
        let currentTableSet = getCurrentTables remote dbName |> Set.ofList
        let pending = ResizeArray<string>()
        for m in topoSort metas metasByName do
            if not (currentTableSet |> Set.contains m.TableName) then
                pending.Add(sprintf "missing table '%s'" m.TableName)
            else
                let changes = diffTable m (getTableColumns remote dbName m.TableName)
                let fkChanged = getTableForeignKeys remote dbName m.TableName <> desiredForeignKeys m metasByName
                let currentIdx = getTableIndexNames remote dbName m.TableName
                let desiredIdx = desiredIndexNames m
                let idxDelta = Set.union (Set.difference desiredIdx currentIdx) (Set.difference currentIdx desiredIdx)
                if not changes.IsEmpty || fkChanged || not idxDelta.IsEmpty then
                    let bits =
                        [ if not changes.IsEmpty then sprintf "%d column change(s)" (List.length changes)
                          if fkChanged then "FK change"
                          if not idxDelta.IsEmpty then sprintf "%d index change(s)" idxDelta.Count ]
                    pending.Add(sprintf "table '%s': %s" m.TableName (String.concat ", " bits))
        if pending.Count = 0 then
            printfn "OK: %s matches the model — no pending schema changes." dbName
            0
        else
            eprintfn "DEPLOY BLOCKED: %s (site: %s) has %d pending schema change(s):" dbName siteLabel pending.Count
            for p in pending do eprintfn "  - %s" p
            eprintfn "Migrate this site before deploying (npm run migrate:remote:dry to generate the diff, review, then apply). See estate-migration-rollout."
            1

[<EntryPoint>]
let main (argv: string array) =
    // Module-emit pass: `-- module <path>` writes that module's own generated surface.
    // Otherwise the site-emit pass runs (per app, the default `npm run gen`).
    match argv |> Array.tryFindIndex ((=) "module") with
    | Some i when i + 1 < argv.Length ->
        emitModuleSurface (readModuleManifest argv.[i + 1])
        0
    | _ ->
        match argv |> Array.tryFindIndex ((=) "emit-recreate") with
        | Some i when i + 1 < argv.Length -> runEmitRecreate argv.[i + 1]
        | _ ->
            if argv |> Array.exists ((=) "check") then
                runCheck (argv |> Array.exists ((=) "--remote"))
            else runSite argv
