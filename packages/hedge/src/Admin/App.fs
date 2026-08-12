module Admin.App

open Feliz
open Feliz.Router
open Elmish
open Fable.Core
open Fable.Core.JsInterop
open Hedge.Schema

[<Emit("localStorage.getItem($0) || ''")>]
let private lsGet (key: string) : string = jsNative

[<Emit("localStorage.setItem($0, $1)")>]
let private lsSet (key: string) (value: string) : unit = jsNative

[<Emit("$0[$1]")>]
let private getField (record: obj) (key: string) : obj = jsNative

[<Emit("($0 == null)")>]
let private isNull (v: obj) : bool = jsNative

/// Epoch seconds -> YY-MM-DD. Falls back to the raw value for anything that
/// isn't a sensible epoch, so a mis-typed column degrades to legible text
/// rather than an exception.
[<Emit("(function(v){ var n = Number(v); return (isFinite(n) && n > 0) ? new Date(n * 1000).toISOString().slice(2, 10) : String(v); })($0)")>]
let private shortDate (raw: string) : string = jsNative

// ============================================================
// PascalCase → camelCase (matches server JSON keys)
// ============================================================

let private camelCase (s: string) =
    if s.Length = 0 then s
    else string (System.Char.ToLowerInvariant s.[0]) + s.[1..]

// ============================================================
// Model
// ============================================================

type Model = {
    Route: string list
    Key: string
    Types: Api.AdminType list option
    CurrentType: string option
    Records: obj list option
    EditingId: string option
    EditRecord: obj option
    EditFields: Map<string, string>
    IsLoading: bool
    Error: string option
}

type Msg =
    | UrlChanged of string list
    | KeyChanged of string
    | LoadTypes
    | GotTypes of Result<Api.AdminType list, string>
    | SelectType of string
    | GotRecords of Result<obj list, string>
    | EditRecord of typeName: string * id: string
    | NewRecord of typeName: string
    | GotEditRecord of Result<obj, string>
    | FieldChanged of string * string
    | Save
    | GotSave of Result<obj, string>
    | DeleteRecord of string
    | GotDelete of Result<bool, string>
    | DismissError

// ============================================================
// TipTap editor lifecycle for rich content fields
// ============================================================

let mutable private activeEditorIds : string list = []

let private initEditorsCmd (schema: TypeSchema) (record: obj) : Cmd<Msg> =
    Cmd.ofEffect (fun _dispatch ->
        schema.Fields |> List.iter (fun field ->
            if field.Attrs |> List.contains RichContent then
                let editorId = sprintf "admin-editor-%s" field.Name
                let key = camelCase field.Name
                let v = getField record key
                let content = if isNull v then "" else string v
                RichText.createEditorWhenReady editorId content
                activeEditorIds <- editorId :: activeEditorIds
        )
    )

/// Create-form variant: same editors, no record to seed them from.
let private initEmptyEditorsCmd (schema: TypeSchema) : Cmd<Msg> =
    Cmd.ofEffect (fun _dispatch ->
        schema.Fields |> List.iter (fun field ->
            if field.Attrs |> List.contains RichContent then
                let editorId = sprintf "admin-editor-%s" field.Name
                RichText.createEditorWhenReady editorId ""
                activeEditorIds <- editorId :: activeEditorIds
        )
    )

let private destroyEditorsCmd : Cmd<Msg> =
    Cmd.ofEffect (fun _dispatch ->
        activeEditorIds |> List.iter RichText.destroyEditor
        activeEditorIds <- []
    )

// ============================================================
// Helpers
// ============================================================

let private findSchema (types: Api.AdminType list option) (typeName: string) : TypeSchema option =
    types |> Option.bind (fun ts ->
        ts |> List.tryFind (fun t -> t.Name = typeName) |> Option.map (fun t -> t.Schema))

let private recordToFields (schema: TypeSchema) (record: obj) : Map<string, string> =
    schema.Fields
    |> List.map (fun field ->
        let key = camelCase field.Name
        let v = getField record key
        match field.Type with
        | FList FString ->
            // JSON array → comma-separated
            let arr : obj list = if isNull v then [] else unbox v
            let strs = arr |> List.map (fun x -> string x)
            field.Name, (strs |> String.concat ", ")
        | _ ->
            field.Name, (if isNull v then "" else string v)
    )
    |> Map.ofList

let private fieldsToJson (schema: TypeSchema) (fields: Map<string, string>) : string =
    schema.Fields
    |> List.choose (fun field ->
        let isReadOnly = field.Attrs |> List.exists (fun a ->
            match a with PrimaryKey | CreateTimestamp | UpdateTimestamp -> true | _ -> false)
        if isReadOnly then None
        else
            let key = camelCase field.Name
            let v = fields |> Map.tryFind field.Name |> Option.defaultValue ""
            let value : obj =
                match field.Type with
                | FOption _ ->
                    if v = "" then null else box v
                | FList FString ->
                    v.Split(',')
                    |> Array.map (fun s -> s.Trim())
                    |> Array.filter (fun s -> s <> "")
                    |> box
                | FInt ->
                    if v = "" then box 0 else box (int v)
                | FBool ->
                    box (v = "true")
                | _ -> box v
            Some (key, value))
    |> createObj
    |> JS.JSON.stringify

let private idField (schema: TypeSchema) : string option =
    schema.Fields
    |> List.tryFind (fun f -> f.Attrs |> List.contains PrimaryKey)
    |> Option.map (fun f -> f.Name)

// ============================================================
// Init / Update
// ============================================================

let init () : Model * Cmd<Msg> =
    let route = Router.currentUrl ()
    let key = lsGet "adminKey"
    let model =
        { Route = route
          Key = key
          Types = None
          CurrentType = None
          Records = None
          EditingId = None
          EditRecord = None
          EditFields = Map.empty
          IsLoading = false
          Error = None }
    model, Cmd.ofMsg LoadTypes

let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | UrlChanged route ->
        let cmd =
            match route with
            | [typeName; "new"] ->
                Cmd.batch [ destroyEditorsCmd; Cmd.ofMsg (NewRecord typeName) ]
            | [typeName; id] ->
                Cmd.batch [ destroyEditorsCmd; Cmd.ofMsg (EditRecord (typeName, id)) ]
            | [typeName] ->
                Cmd.batch [ destroyEditorsCmd; Cmd.ofMsg (SelectType typeName) ]
            | _ ->
                destroyEditorsCmd
        { model with Route = route; EditRecord = None; EditingId = None; EditFields = Map.empty }, cmd

    | KeyChanged key ->
        lsSet "adminKey" key
        { model with Key = key }, Cmd.none

    | LoadTypes ->
        { model with IsLoading = true },
        Cmd.OfPromise.either Api.getTypes () GotTypes (fun ex -> GotTypes (Error ex.Message))

    | GotTypes (Ok types) ->
        let cmd =
            match model.Route with
            | [typeName; "new"] -> Cmd.ofMsg (NewRecord typeName)
            | [typeName; id] -> Cmd.ofMsg (EditRecord (typeName, id))
            | [typeName] -> Cmd.ofMsg (SelectType typeName)
            | _ -> Cmd.none
        { model with Types = Some types; IsLoading = false; Error = None }, cmd

    | GotTypes (Error err) ->
        { model with IsLoading = false; Error = Some err }, Cmd.none

    | SelectType typeName ->
        { model with CurrentType = Some typeName; IsLoading = true; Records = None; EditingId = None; EditRecord = None; EditFields = Map.empty },
        Cmd.batch [
            destroyEditorsCmd
            Cmd.OfPromise.either
                (fun () -> Api.listRecords model.Key typeName)
                () GotRecords (fun ex -> GotRecords (Error ex.Message))
        ]

    | GotRecords (Ok records) ->
        { model with Records = Some records; IsLoading = false; Error = None }, Cmd.none

    | GotRecords (Error err) ->
        { model with IsLoading = false; Error = Some err }, Cmd.none

    | EditRecord (typeName, id) ->
        { model with CurrentType = Some typeName; EditingId = Some id; IsLoading = true },
        Cmd.batch [
            destroyEditorsCmd
            Cmd.OfPromise.either
                (fun () -> Api.getRecord model.Key typeName id)
                () GotEditRecord (fun ex -> GotEditRecord (Error ex.Message))
        ]

    | NewRecord typeName ->
        // No fetch: a create form starts empty. EditingId stays None, which is
        // what Save keys off to choose POST over PUT.
        let model = { model with CurrentType = Some typeName; EditingId = None; EditRecord = None; EditFields = Map.empty; IsLoading = false }
        match findSchema model.Types typeName with
        | Some schema -> model, Cmd.batch [ destroyEditorsCmd; initEmptyEditorsCmd schema ]
        | None -> model, destroyEditorsCmd

    | GotEditRecord (Ok record) ->
        match model.CurrentType |> Option.bind (fun t -> findSchema model.Types t) with
        | Some schema ->
            let fields = recordToFields schema record
            { model with EditRecord = Some record; EditFields = fields; IsLoading = false },
            initEditorsCmd schema record
        | None ->
            { model with EditRecord = Some record; IsLoading = false; Error = Some "Schema not found" }, Cmd.none

    | GotEditRecord (Error err) ->
        { model with IsLoading = false; Error = Some err }, Cmd.none

    | FieldChanged (name, value) ->
        { model with EditFields = model.EditFields |> Map.add name value }, Cmd.none

    | Save ->
        match model.CurrentType with
        | Some typeName ->
            match findSchema model.Types typeName with
            | Some schema ->
                // Grab rich content from TipTap editors
                let fields =
                    schema.Fields |> List.fold (fun acc field ->
                        if field.Attrs |> List.contains RichContent then
                            let editorId = sprintf "admin-editor-%s" field.Name
                            let content = RichText.getEditorContent editorId
                            acc |> Map.add field.Name content
                        else acc
                    ) model.EditFields
                let body = fieldsToJson schema fields
                // No EditingId means this form was opened as a create
                let call =
                    match model.EditingId with
                    | Some id -> fun () -> Api.updateRecord model.Key typeName id body
                    | None -> fun () -> Api.createRecord model.Key typeName body
                { model with IsLoading = true },
                Cmd.OfPromise.either call () GotSave (fun ex -> GotSave (Error ex.Message))
            | None ->
                model, Cmd.none
        | None -> model, Cmd.none

    | GotSave (Ok _) ->
        match model.CurrentType with
        | Some typeName ->
            // SelectType alone only changes model state — the route still points
            // at the record, so the form would stay on screen. Navigate so the
            // list is what you land on after saving.
            { model with IsLoading = false },
            Cmd.batch [
                destroyEditorsCmd
                Cmd.ofEffect (fun _ -> Router.navigate typeName)
            ]
        | None ->
            { model with IsLoading = false }, Cmd.none

    | GotSave (Error err) ->
        { model with IsLoading = false; Error = Some err }, Cmd.none

    | DeleteRecord id ->
        match model.CurrentType with
        | Some typeName ->
            { model with IsLoading = true },
            Cmd.OfPromise.either
                (fun () -> Api.deleteRecord model.Key typeName id)
                () GotDelete (fun ex -> GotDelete (Error ex.Message))
        | None -> model, Cmd.none

    | GotDelete (Ok _) ->
        match model.CurrentType with
        | Some typeName ->
            { model with IsLoading = false },
            Cmd.batch [ destroyEditorsCmd; Cmd.ofMsg (SelectType typeName) ]
        | None ->
            { model with IsLoading = false }, Cmd.none

    | GotDelete (Error err) ->
        { model with IsLoading = false; Error = Some err }, Cmd.none

    | DismissError ->
        { model with Error = None }, Cmd.none

// ============================================================
// Views
// ============================================================

module View =
    let private keyInput (model: Model) dispatch =
        Html.div [
            prop.className "admin-key-input"
            prop.children [
                Html.h2 [ prop.text "Admin" ]
                Html.input [
                    prop.placeholder "Admin Key"
                    prop.type'.password
                    prop.value model.Key
                    prop.onChange (KeyChanged >> dispatch)
                ]
            ]
        ]

    /// Home: one row per type — the name browses the list, New opens a create
    /// form. Types without a primary key can't be created (the generated INSERT
    /// is empty), and the server would reject the POST, so no button is offered.
    let private typeHome (types: Api.AdminType list) =
        Html.div [
            prop.className "admin-home"
            prop.children (types |> List.map (fun t ->
                let canCreate = t.Schema.Fields |> List.exists (fun f -> f.Attrs |> List.contains PrimaryKey)
                Html.div [
                    prop.className "admin-home-row"
                    prop.children [
                        Html.a [
                            prop.className "admin-home-name"
                            prop.text t.Name
                            prop.onClick (fun _ -> Router.navigate t.Name)
                        ]
                        if canCreate then
                            Html.button [
                                prop.className "admin-btn admin-btn-primary"
                                prop.text "New"
                                prop.onClick (fun _ -> Router.navigate (t.Name, "new"))
                            ]
                        else
                            Html.span [
                                prop.className "admin-home-note"
                                prop.title "No primary key — records can't be created from the admin"
                                prop.text "—"
                            ]
                    ]
                ]
            ))
        ]

    /// head…tail, e.g. "a1b2…9f0e". Only shortens when it actually saves space.
    let private clip (head: int) (tail: int) (s: string) =
        if s.Length <= head + tail + 1 then s
        else s.[.. head - 1] + "…" + s.[s.Length - tail ..]

    let rec private underlying (t: FieldType) =
        match t with
        | FOption inner -> underlying inner
        | t -> t

    /// One table cell, abbreviated by what the field *means* rather than by
    /// length alone. Every shortened cell carries the full value as a title,
    /// so nothing becomes unreadable — only less shouty.
    let private cell (field: FieldSchema) (raw: obj) =
        let text = if isNull raw then "" else string raw
        let hasAttr a = field.Attrs |> List.contains a
        let isForeignKey = field.Attrs |> List.exists (function ForeignKey _ -> true | _ -> false)
        let isTimestamp = hasAttr CreateTimestamp || hasAttr UpdateTimestamp || hasAttr SoftDelete
        if text = "" then
            Html.td [ prop.className "admin-cell admin-cell-empty"; prop.text "—" ]
        elif hasAttr PrimaryKey || isForeignKey then
            Html.td [
                prop.className "admin-cell admin-cell-id"
                prop.title text
                prop.text (clip 4 4 text)
            ]
        elif isTimestamp then
            Html.td [
                prop.className "admin-cell admin-cell-time"
                prop.title text
                prop.text (shortDate text)
            ]
        elif hasAttr Link then
            Html.td [
                prop.className "admin-cell admin-cell-link"
                prop.children [
                    Html.a [
                        prop.href text
                        prop.target "_blank"
                        prop.rel "noopener noreferrer"
                        prop.title text
                        // the row opens the editor; following a link shouldn't
                        prop.onClick (fun e -> e.stopPropagation())
                        prop.text (clip 30 0 text)
                    ]
                ]
            ]
        else
            match underlying field.Type with
            | FBool ->
                // Stored as 1/0 or true/false depending on write path
                let on = text = "1" || text.ToLowerInvariant() = "true"
                Html.td [
                    prop.className (if on then "admin-cell admin-cell-bool on" else "admin-cell admin-cell-bool")
                    prop.title text
                    prop.text (if on then "✓" else "·")
                ]
            | _ ->
                let display = clip 44 0 text
                Html.td [
                    prop.className "admin-cell"
                    prop.title (if display = text then "" else text)
                    prop.text display
                ]

    let private recordRow (schema: TypeSchema) dispatch (record: obj) =
        let pkField = idField schema
        let id =
            match pkField with
            | Some f -> let v = getField record (camelCase f) in if isNull v then "" else string v
            | None -> ""
        let typeName = schema.Name
        Html.tr [
            prop.className "admin-row"
            prop.onClick (fun _ -> Router.navigate (typeName, id))
            prop.children [
                // Show a few key fields as columns
                yield! schema.Fields |> List.choose (fun field ->
                    // Skip rich content and lists in the table
                    match field.Type with
                    | FList _ -> None
                    | _ when field.Attrs |> List.contains RichContent -> None
                    | _ -> Some (cell field (getField record (camelCase field.Name))))
                Html.td [
                    prop.className "admin-cell admin-cell-actions"
                    prop.children [
                        // Edit lives on the row itself, so only the destructive
                        // action needs a control — and it must not open the editor.
                        Html.button [
                            prop.className "admin-btn admin-btn-danger"
                            prop.text "Delete"
                            prop.onClick (fun e -> e.stopPropagation(); dispatch (DeleteRecord id))
                        ]
                    ]
                ]
            ]
        ]

    let private recordList (schema: TypeSchema) (records: obj list) dispatch =
        let visibleFields =
            schema.Fields |> List.filter (fun field ->
                match field.Type with
                | FList _ -> false
                | _ when field.Attrs |> List.contains RichContent -> false
                | _ -> true)
        Html.div [
            prop.className "admin-record-list"
            prop.children [
                Html.div [
                    prop.className "admin-list-header"
                    prop.children [
                        Html.h2 [ prop.text (sprintf "%ss" schema.Name) ]
                        // Same primary-key rule as the home page: no key, no create
                        if schema.Fields |> List.exists (fun f -> f.Attrs |> List.contains PrimaryKey) then
                            Html.button [
                                prop.className "admin-btn admin-btn-primary admin-btn-add"
                                prop.title (sprintf "New %s" schema.Name)
                                prop.text "+"
                                prop.onClick (fun _ -> Router.navigate (schema.Name, "new"))
                            ]
                    ]
                ]
                Html.div [
                    prop.className "admin-table-wrap"
                    prop.children [
                        Html.table [
                            prop.className "admin-table"
                            prop.children [
                                Html.thead [
                                    Html.tr [
                                        yield! visibleFields |> List.map (fun f ->
                                            Html.th [ prop.text f.Name ])
                                        Html.th [ prop.className "admin-cell-actions"; prop.text "Actions" ]
                                    ]
                                ]
                                Html.tbody (records |> List.map (recordRow schema dispatch))
                            ]
                        ]
                    ]
                ]
            ]
        ]

    let private renderSchemaField dispatch (values: Map<string, string>) (field: FieldSchema) =
        let isReadOnly = field.Attrs |> List.exists (fun a ->
            match a with PrimaryKey | CreateTimestamp | UpdateTimestamp -> true | _ -> false)
        let isRichContent = field.Attrs |> List.contains RichContent
        match field.Type with
        | _ when isRichContent ->
            Html.div [
                prop.className "admin-field"
                prop.children [
                    Html.label [ prop.text field.Name ]
                    Html.div [
                        prop.className "admin-editor"
                        prop.id (sprintf "admin-editor-%s" field.Name)
                    ]
                ]
            ]
        // Read-only timestamps are shown as dates; they're never submitted, and
        // the raw epoch stays available on hover. Editable timestamps (SoftDelete)
        // deliberately fall through to the numeric input below.
        | _ when isReadOnly
                 && field.Attrs |> List.exists (fun a ->
                        match a with CreateTimestamp | UpdateTimestamp -> true | _ -> false) ->
            let raw = values |> Map.tryFind field.Name |> Option.defaultValue ""
            Html.div [
                prop.className "admin-field"
                prop.children [
                    Html.label [ prop.text field.Name ]
                    Html.input [
                        prop.value (if raw = "" then "" else shortDate raw)
                        prop.title raw
                        prop.disabled true
                    ]
                ]
            ]
        | FString | FOption FString ->
            Html.div [
                prop.className "admin-field"
                prop.children [
                    Html.label [ prop.text field.Name ]
                    Html.input [
                        prop.value (values |> Map.tryFind field.Name |> Option.defaultValue "")
                        prop.disabled isReadOnly
                        prop.onChange (fun v -> dispatch (FieldChanged (field.Name, v)))
                    ]
                ]
            ]
        | FInt ->
            Html.div [
                prop.className "admin-field"
                prop.children [
                    Html.label [ prop.text field.Name ]
                    Html.input [
                        prop.value (values |> Map.tryFind field.Name |> Option.defaultValue "")
                        prop.disabled isReadOnly
                        prop.onChange (fun v -> dispatch (FieldChanged (field.Name, v)))
                    ]
                ]
            ]
        | FList FString ->
            Html.div [
                prop.className "admin-field"
                prop.children [
                    Html.label [ prop.text field.Name ]
                    Html.input [
                        prop.value (values |> Map.tryFind field.Name |> Option.defaultValue "")
                        prop.placeholder "comma-separated"
                        prop.disabled isReadOnly
                        prop.onChange (fun v -> dispatch (FieldChanged (field.Name, v)))
                    ]
                ]
            ]
        | _ ->
            Html.div [
                prop.className "admin-field"
                prop.children [
                    Html.label [ prop.text field.Name ]
                    Html.input [
                        prop.value (values |> Map.tryFind field.Name |> Option.defaultValue "")
                        prop.disabled isReadOnly
                        prop.onChange (fun v -> dispatch (FieldChanged (field.Name, v)))
                    ]
                ]
            ]

    let private editForm (model: Model) (schema: TypeSchema) dispatch =
        let isCreate = model.EditingId.IsNone
        // On create the server generates the id and timestamps, so showing them
        // as blank disabled boxes would just be noise.
        let fields =
            if not isCreate then schema.Fields
            else
                schema.Fields |> List.filter (fun f ->
                    f.Attrs |> List.exists (fun a ->
                        match a with
                        | PrimaryKey | CreateTimestamp | UpdateTimestamp -> true
                        | SoftDelete -> true   // nothing to un-delete on a new record
                        | _ -> false)
                    |> not)
        Html.div [
            prop.className "admin-edit-form"
            prop.children [
                Html.h2 [ prop.text (sprintf "%s %s" (if isCreate then "New" else "Edit") schema.Name) ]
                yield! fields |> List.map (renderSchemaField dispatch model.EditFields)
                Html.div [
                    prop.className "admin-form-actions"
                    prop.children [
                        Html.button [
                            prop.className "admin-btn admin-btn-primary"
                            prop.text "Save"
                            prop.disabled model.IsLoading
                            prop.onClick (fun _ -> dispatch Save)
                        ]
                        Html.button [
                            prop.className "admin-btn"
                            prop.text "Back"
                            prop.onClick (fun _ ->
                                match model.CurrentType with
                                | Some t -> Router.navigate t
                                | None -> Router.navigate "")
                        ]
                    ]
                ]
            ]
        ]

    let private nav (model: Model) =
        let current = match model.Route with t :: _ -> Some t | [] -> None
        Html.nav [
            prop.className "admin-nav"
            prop.children [
                Html.a [
                    prop.className "admin-brand"
                    prop.text "Admin"
                    prop.onClick (fun _ -> Router.navigate "")
                ]
                match model.Types with
                | Some types ->
                    yield! types |> List.map (fun t ->
                        Html.a [
                            prop.text t.Name
                            prop.className (if current = Some t.Name then "active" else "")
                            prop.onClick (fun _ -> Router.navigate t.Name)
                        ])
                | None -> ()
            ]
        ]

    let app (model: Model) dispatch =
        Html.div [
            prop.className "app admin-app"
            prop.children [
                Html.header [ nav model ]
                Html.main [
                    match model.Error with
                    | Some err ->
                        Html.div [
                            prop.className "error"
                            prop.style [ style.display.flex; style.justifyContent.spaceBetween; style.alignItems.center ]
                            prop.children [
                                Html.span [ prop.text err ]
                                Html.button [
                                    prop.text "\u00d7"
                                    prop.style [ style.custom("border", "none"); style.custom("background", "none"); style.cursor.pointer; style.fontSize 18; style.color.black; style.padding 0; style.custom("lineHeight", "1") ]
                                    prop.onClick (fun _ -> dispatch DismissError)
                                ]
                            ]
                        ]
                    | None -> Html.none

                    if model.Key = "" then
                        keyInput model dispatch
                    elif model.IsLoading then
                        Html.div [ prop.className "loading"; prop.text "Loading..." ]
                    else
                        match model.Route with
                        | [typeName; _] ->
                            match findSchema model.Types typeName with
                            | Some schema -> editForm model schema dispatch
                            | None -> Html.p [ prop.text "Unknown type." ]
                        | [typeName] ->
                            match findSchema model.Types typeName with
                            | Some schema ->
                                match model.Records with
                                | Some records -> recordList schema records dispatch
                                | None -> Html.p [ prop.text "No records loaded." ]
                            | None -> Html.p [ prop.text "Unknown type." ]
                        | _ ->
                            match model.Types with
                            | Some types -> typeHome types
                            | None -> Html.p [ prop.text "Loading types..." ]
                ]
            ]
        ]

open Elmish.React

let view model dispatch =
    React.router [
        router.onUrlChanged (UrlChanged >> dispatch)
        router.children [ View.app model dispatch ]
    ]

#if DEBUG
open Elmish.HMR
#endif

Program.mkProgram init update view
|> Program.withReactSynchronous "app"
|> Program.run
