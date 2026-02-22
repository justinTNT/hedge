module Gen.Program

open Fable.Core
open Hedge.Schema

// ============================================================
// File I/O (Node.js)
// ============================================================

[<Import("writeFileSync", "node:fs")>]
let writeFileSync (path: string, content: string) : unit = jsNative

[<Import("readFileSync", "node:fs")>]
let readFileSync (path: string, encoding: string) : string = jsNative

let readFile path = readFileSync(path, "utf8")

[<Import("readdirSync", "node:fs")>]
let readdirSync (path: string) : string array = jsNative

[<Import("existsSync", "node:fs")>]
let existsSync (path: string) : bool = jsNative

[<Import("mkdirSync", "node:fs")>]
let private mkdirSyncImport (path: string, options: obj) : unit = jsNative

let mkdirRecursive (path: string) : unit =
    mkdirSyncImport(path, box {| recursive = true |})

[<Import("execSync", "node:child_process")>]
let private execSyncImport (cmd: string, options: obj) : string = jsNative

let execSync (cmd: string) : string =
    execSyncImport(cmd, box {| encoding = "utf8" |})

[<Emit("process.argv")>]
let argv : string array = jsNative

[<Emit("JSON.parse($0)")>]
let jsonParse (s: string) : obj = jsNative

[<Emit("$0[$1]")>]
let jsIdx (o: obj) (i: int) : obj = jsNative

[<Emit("$0[$1]")>]
let jsGet (o: obj) (key: string) : obj = jsNative

[<Emit("($0 || []).length")>]
let jsLen (o: obj) : int = jsNative

// ============================================================
// Source parser — reads Domain.fs, extracts all type definitions
// ============================================================

/// Parse "PrimaryKey<string>" etc. into FieldType + attrs
let parseInnerType (s: string) : FieldType * FieldAttr list =
    if s.StartsWith("PrimaryKey<") then
        let inner = s.Substring(11, s.Length - 12)
        let ft = if inner.Contains("int") then FInt else FString
        ft, [PrimaryKey]
    elif s.StartsWith("ForeignKey<") then
        let inner = s.Substring(11, s.Length - 12)
        FString, [ForeignKey inner]
    elif s = "CreateTimestamp" then FInt, [CreateTimestamp]
    elif s = "UpdateTimestamp" then FInt, [UpdateTimestamp]
    elif s = "SoftDelete" then FInt, [SoftDelete]
    elif s = "RichContent" then FString, [RichContent]
    elif s = "Link" then FString, [Link]
    elif s = "string" then FString, []
    elif s = "int" then FInt, []
    elif s = "bool" then FBool, []
    else FRecord s, []

/// Parse a full field type string, handling option/list wrappers
let parseFieldType (typeStr: string) : FieldType * FieldAttr list =
    let s = typeStr.Trim()
    if s.EndsWith(" option") then
        let inner = s.Substring(0, s.Length - 7)
        let ft, attrs = parseInnerType inner
        FOption ft, attrs
    elif s.EndsWith(" list") then
        let inner = s.Substring(0, s.Length - 5)
        let ft, _ = parseInnerType inner
        FList ft, []
    else
        parseInnerType s

type ParsedType = {
    Name: string
    Table: string option
    Fields: FieldSchema list
}

/// Parse all type definitions from a Domain.fs source file
let parseDomainFile (path: string) : ParsedType list =
    let content = readFile path
    let lines = content.Split('\n')

    let results = ResizeArray<ParsedType>()
    let mutable pendingTable : string option = None
    let mutable currentName : string option = None
    let mutable currentFields = ResizeArray<FieldSchema>()

    let flush () =
        match currentName with
        | Some name ->
            results.Add({
                Name = name
                Table = pendingTable
                Fields = currentFields |> Seq.toList
            })
            currentName <- None
            currentFields <- ResizeArray<FieldSchema>()
            pendingTable <- None
        | None -> ()

    for line in lines do
        let trimmed = line.Trim()
        if trimmed.StartsWith("// @table ") then
            pendingTable <- Some (trimmed.Substring(10).Trim())
        elif trimmed.StartsWith("type ") && trimmed.Contains("= {") then
            flush ()
            let afterType = trimmed.Substring(5)
            let spaceIdx = afterType.IndexOf(' ')
            let name = if spaceIdx > 0 then afterType.Substring(0, spaceIdx) else afterType
            currentName <- Some name
        elif currentName.IsSome && trimmed = "}" then
            flush ()
        elif currentName.IsSome && trimmed.Contains(":") && not (trimmed.StartsWith("//")) then
            let colonIdx = trimmed.IndexOf(':')
            let fieldName = trimmed.Substring(0, colonIdx).Trim()
            let fieldTypeStr = trimmed.Substring(colonIdx + 1).Trim()
            let ft, attrs = parseFieldType fieldTypeStr
            currentFields.Add({ Name = fieldName; Type = ft; Attrs = attrs })

    flush ()
    results |> Seq.toList

// ============================================================
// Helpers
// ============================================================

let toSnakeCase (s: string) =
    s.ToCharArray()
    |> Array.mapi (fun i c ->
        if i > 0 && System.Char.IsUpper c then
            sprintf "_%c" (System.Char.ToLower c)
        else
            string (System.Char.ToLower c))
    |> String.concat ""

let toCamelCase (s: string) =
    (System.Char.ToLower s.[0] |> string) + s.[1..]

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
// Shared table metadata — used by both AdminGen and Db
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

    // Section header
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
// D1 introspection
// ============================================================

type ColInfo = {
    ColName: string
    ColType: string
    NotNull: bool
    IsPk: bool
}

let getDbName () : string =
    let content = readFile "wrangler.toml"
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

let wranglerQuery (dbName: string) (sql: string) : obj array =
    let escaped = sql.Replace("\"", "\\\"")
    let cmd = sprintf "npx wrangler d1 execute %s --local --command \"%s\" --json" dbName escaped
    let output = execSync cmd
    let parsed = jsonParse output
    let first = jsIdx parsed 0
    let results = jsGet first "results"
    let len = jsLen results
    Array.init len (fun i -> jsIdx results i)

let getCurrentTables (dbName: string) : string list =
    let rows = wranglerQuery dbName "SELECT name FROM sqlite_master WHERE type='table'"
    rows
    |> Array.map (fun r -> unbox<string> (jsGet r "name"))
    |> Array.filter (fun n ->
        not (n.StartsWith("d1_") || n.StartsWith("sqlite_") || n.StartsWith("_cf_")))
    |> Array.toList

let getTableColumns (dbName: string) (tableName: string) : ColInfo list =
    let rows = wranglerQuery dbName (sprintf "PRAGMA table_info(%s)" tableName)
    rows
    |> Array.map (fun r ->
        { ColName = unbox<string> (jsGet r "name")
          ColType = unbox<string> (jsGet r "type")
          NotNull = unbox<int> (jsGet r "notnull") = 1
          IsPk = unbox<int> (jsGet r "pk") = 1 })
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
    while endIdx < s.Length && System.Char.IsDigit s.[endIdx] do
        endIdx <- endIdx + 1
    if endIdx > 0 then
        Some (int (s.Substring(0, endIdx)))
    else None

let nextMigrationNumber () : int =
    if not (existsSync "migrations") then 1
    else
        let files = readdirSync "migrations"
        let numbers =
            files
            |> Array.choose extractLeadingNumber
        if numbers.Length = 0 then 1
        else (Array.max numbers) + 1

let writeMigration (sql: string) : string =
    if not (existsSync "migrations") then
        mkdirRecursive "migrations"
    let num = nextMigrationNumber ()
    let padded = sprintf "%04d" num
    let filename = sprintf "migrations/%s_auto.sql" padded
    writeFileSync(filename, sql)
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
                let cmd = sprintf "npx wrangler d1 migrations apply %s --local" dbName
                let output = execSync cmd
                printfn "%s" output
                printfn "Migration applied locally."
            else
                printfn "(dry-run: migration file written but not applied)"

// ============================================================
// Main: parse Domain.fs, generate files, optionally migrate
// ============================================================

[<EntryPoint>]
let main _ =
    let parsed = parseDomainFile "src/Models/Domain.fs"
    let metas = parsed |> List.map computeMeta

    let admin = generateAdminFs metas
    writeFileSync("src/Server/AdminGen.fs", admin)

    let db = generateDbFs metas
    writeFileSync("src/Server/Db.fs", db)

    let schemaSql = generateSchemaSql metas
    writeFileSync("schema.sql", schemaSql)

    printfn "Generated %d types -> AdminGen.fs, Db.fs, schema.sql" (List.length metas)

    let args = argv
    let hasMigrate = args |> Array.exists (fun a -> a = "migrate")
    let hasDryRun = args |> Array.exists (fun a -> a = "--dry-run")

    if hasMigrate then
        runMigrate metas hasDryRun

    0
