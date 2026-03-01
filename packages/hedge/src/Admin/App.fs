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
    let pairs =
        schema.Fields |> List.choose (fun field ->
            // Skip read-only fields
            let isReadOnly = field.Attrs |> List.exists (fun a ->
                match a with PrimaryKey | CreateTimestamp | UpdateTimestamp -> true | _ -> false)
            if isReadOnly then None
            else
                let key = camelCase field.Name
                let v = fields |> Map.tryFind field.Name |> Option.defaultValue ""
                let encoded =
                    match field.Type with
                    | FOption _ ->
                        if v = "" then "null" else sprintf "\"%s\"" (v.Replace("\"", "\\\""))
                    | FList FString ->
                        let items =
                            v.Split(',')
                            |> Array.map (fun s -> s.Trim())
                            |> Array.filter (fun s -> s <> "")
                            |> Array.map (fun s -> sprintf "\"%s\"" (s.Replace("\"", "\\\"")))
                        sprintf "[%s]" (items |> String.concat ",")
                    | FInt ->
                        if v = "" then "0" else v
                    | FBool ->
                        if v = "true" then "true" else "false"
                    | _ ->
                        sprintf "\"%s\"" (v.Replace("\"", "\\\""))
                Some (sprintf "\"%s\":%s" key encoded))
    sprintf "{%s}" (pairs |> String.concat ",")

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
        match model.CurrentType, model.EditingId with
        | Some typeName, Some id ->
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
                { model with IsLoading = true },
                Cmd.OfPromise.either
                    (fun () -> Api.updateRecord model.Key typeName id body)
                    () GotSave (fun ex -> GotSave (Error ex.Message))
            | None ->
                model, Cmd.none
        | _ -> model, Cmd.none

    | GotSave (Ok _) ->
        match model.CurrentType with
        | Some typeName ->
            { model with IsLoading = false },
            Cmd.batch [ destroyEditorsCmd; Cmd.ofMsg (SelectType typeName) ]
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

    let private typeSelector (types: Api.AdminType list) (current: string option) =
        Html.div [
            prop.className "admin-types"
            prop.children (types |> List.map (fun t ->
                Html.a [
                    prop.text t.Name
                    prop.className (if current = Some t.Name then "active" else "")
                    prop.style [ style.cursor.pointer; style.marginRight 16 ]
                    prop.onClick (fun _ -> Router.navigate t.Name)
                ]
            ))
        ]

    let private recordRow (schema: TypeSchema) dispatch (record: obj) =
        let pkField = idField schema
        let id =
            match pkField with
            | Some f -> let v = getField record (camelCase f) in if isNull v then "" else string v
            | None -> ""
        let typeName = schema.Name
        Html.tr [
            prop.children [
                // Show a few key fields as columns
                yield! schema.Fields |> List.choose (fun field ->
                    // Skip rich content and lists in the table
                    match field.Type with
                    | FList _ -> None
                    | _ when field.Attrs |> List.contains RichContent -> None
                    | _ ->
                        let key = camelCase field.Name
                        let v = getField record key
                        let text = if isNull v then "" else string v
                        let display = if text.Length > 80 then text.[..77] + "..." else text
                        Some (Html.td [ prop.text display ]))
                Html.td [
                    prop.children [
                        Html.button [
                            prop.text "Edit"
                            prop.style [ style.cursor.pointer; style.marginRight 8 ]
                            prop.onClick (fun _ -> Router.navigate (typeName, id))
                        ]
                        Html.button [
                            prop.text "Delete"
                            prop.style [ style.cursor.pointer; style.color "#e74c3c" ]
                            prop.onClick (fun _ -> dispatch (DeleteRecord id))
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
                Html.h2 [ prop.text (sprintf "%ss" schema.Name) ]
                Html.table [
                    prop.children [
                        Html.thead [
                            Html.tr [
                                yield! visibleFields |> List.map (fun f ->
                                    Html.th [ prop.text f.Name ])
                                Html.th [ prop.text "Actions" ]
                            ]
                        ]
                        Html.tbody (records |> List.map (recordRow schema dispatch))
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
                    Html.div [ prop.id (sprintf "admin-editor-%s" field.Name) ]
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
        Html.div [
            prop.className "admin-edit-form"
            prop.children [
                Html.h2 [ prop.text (sprintf "Edit %s" schema.Name) ]
                yield! schema.Fields |> List.map (renderSchemaField dispatch model.EditFields)
                Html.div [
                    prop.style [ style.marginTop 16 ]
                    prop.children [
                        Html.button [
                            prop.text "Save"
                            prop.disabled model.IsLoading
                            prop.onClick (fun _ -> dispatch Save)
                        ]
                        Html.button [
                            prop.text "Back"
                            prop.style [ style.marginLeft 8 ]
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
        Html.nav [
            prop.children [
                Html.a [
                    prop.text "Admin"
                    prop.style [ style.cursor.pointer ]
                    prop.onClick (fun _ -> Router.navigate "")
                ]
                match model.Types with
                | Some types ->
                    yield! types |> List.map (fun t ->
                        Html.a [
                            prop.text t.Name
                            prop.style [ style.cursor.pointer; style.marginLeft 16 ]
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
                            | Some types -> typeSelector types model.CurrentType
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
