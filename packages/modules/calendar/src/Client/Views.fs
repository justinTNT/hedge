module Calendar.Client.Views

// Flat, forward-facing presentation of the projection: the month grid (summaries in position),
// the facet toggles, and the day modal (the full rich text of everything on that day, plus the
// own-entry editor). Content only — the host supplies the frame; navigation goes through the host
// context so this renders the same under any chrome or mount.

module RichText = Client.RichText

open Feliz
open Calendar.Api
open Calendar.Vocab
open Calendar.Client.Dates
open Calendar.Client.Types
open Calendar.Client.Projection

let private repeatLabel (r: string) =
    if r = Repeat.none then "once"
    elif r = Repeat.daily then "daily"
    elif r = Repeat.weekly then "weekly"
    elif r = Repeat.fortnightly then "fortnightly"
    elif r = Repeat.monthly then "monthly"
    elif r = Repeat.yearly then "yearly"
    else r

let private classes (xs: string list) = xs |> List.filter (fun s -> s <> "") |> String.concat " "

let private monthNav (ctx: Content.HostContext) (model: Model) =
    let py, pm = addMonths model.Year model.Month -1
    let ny, nm = addMonths model.Year model.Month 1
    Html.div [
        prop.className "month-nav"
        prop.children [
            Html.button [
                prop.className "btn btn-ghost"; prop.ariaLabel "Previous month"; prop.text "‹"
                prop.onClick (fun _ -> ctx.Navigate (monthSegments py pm)) ]
            Html.h1 [ prop.className "month-title"; prop.text (monthLabel locale model.Year model.Month) ]
            Html.button [
                prop.className "btn btn-ghost"; prop.ariaLabel "Next month"; prop.text "›"
                prop.onClick (fun _ -> ctx.Navigate (monthSegments ny nm)) ]
            Html.button [
                prop.className "btn btn-ghost btn-today"; prop.text "Today"
                prop.onClick (fun _ -> ctx.Navigate []) ]
        ]
    ]

let private facets (model: Model) (dispatch: Msg -> unit) =
    // A guest has no personal facet: nothing of theirs is in the stream.
    let shown = Category.all |> List.filter (fun c -> Category.isPublic c || model.SignedIn)
    Html.div [
        prop.className "facets"
        prop.children [
            for c in shown do
                let on = not (model.Hidden.Contains c)
                Html.button [
                    prop.className (classes [ "facet"; "cat-" + c; (if on then "facet-on" else "facet-off") ])
                    prop.ariaPressed on
                    prop.text c
                    prop.onClick (fun _ -> dispatch (ToggleCategory c))
                ]
        ]
    ]

// -- The grid --

let private weekdayRow (first: Day) =
    Html.div [
        prop.className "cal-weekdays"
        prop.children [ for i in 0 .. 6 do Html.div [ prop.className "cal-weekday"; prop.text (weekdayLabel locale (first + i)) ] ]
    ]

let [<Literal>] private CellMax = 3

let private cell (ctx: Content.HostContext) (model: Model) (todayDay: Day) (day: Day) (occ: Occurrence list) =
    let inMonth = yearOf day = model.Year && monthOf day = model.Month
    let shown = occ |> List.truncate CellMax
    let more = occ.Length - shown.Length
    Html.div [
        prop.className (classes [
            "cal-cell"
            (if inMonth then "" else "cal-cell-outside")
            (if day = todayDay then "cal-cell-today" else "")
            (if List.isEmpty occ then "" else "cal-cell-has") ])
        prop.onClick (fun _ -> ctx.Navigate (daySegments day))
        prop.children [
            Html.div [ prop.className "cal-daynum"; prop.text (string (dayOfMonthOf day)) ]
            Html.div [
                prop.className "cal-cell-entries"
                prop.children [
                    for o in shown do
                        Html.div [
                            prop.className ("cal-entry cat-" + o.Entry.Category)
                            prop.title o.Entry.Summary
                            prop.text o.Entry.Summary ]
                    if more > 0 then
                        Html.div [ prop.className "cal-more"; prop.text (sprintf "+%d more" more) ]
                ]
            ]
        ]
    ]

let private grid (ctx: Content.HostContext) (model: Model) (first: Day) (last: Day) (projected: Map<Day, Occurrence list>) =
    let todayDay = today ()
    Html.div [
        prop.className "cal-grid"
        prop.children [
            for day in first .. last do
                cell ctx model todayDay day (projected |> Map.tryFind day |> Option.defaultValue [])
        ]
    ]

// -- The day modal --

let private occurrenceView (dispatch: Msg -> unit) (o: Occurrence) =
    let e = o.Entry
    Html.article [
        prop.className ("day-entry cat-" + e.Category)
        prop.children [
            Html.div [
                prop.className "day-entry-head"
                prop.children [
                    Html.span [ prop.className ("cat-pill cat-" + e.Category); prop.text e.Category ]
                    Html.h3 [ prop.className "day-entry-summary"; prop.text e.Summary ]
                    if e.Repeat <> Repeat.none then
                        Html.span [ prop.className "day-entry-repeat"; prop.text (repeatLabel e.Repeat) ]
                    // The owner's view of where their entry stands.
                    match e.Mine, e.Category = Category.personal, e.Requested with
                    | true, true, Some f ->
                        Html.span [ prop.className "day-entry-status"; prop.text (sprintf "awaiting approval · %s" f) ]
                    | true, false, _ ->
                        Html.span [ prop.className "day-entry-status"; prop.text "yours · published" ]
                    | _ -> Html.none
                ]
            ]
            if e.Body <> "" then
                Html.div [
                    prop.className "day-entry-body rich-content"
                    prop.dangerouslySetInnerHTML (RichText.toHtml e.Body) ]
            if e.Mine then
                Html.div [
                    prop.className "day-entry-actions"
                    prop.children [
                        Html.button [ prop.className "btn btn-ghost"; prop.text "Edit"; prop.onClick (fun _ -> dispatch (StartEdit e)) ]
                        Html.button [ prop.className "btn btn-ghost btn-danger"; prop.text "Delete"; prop.onClick (fun _ -> dispatch (DeleteEntry e.Id)) ]
                    ]
                ]
        ]
    ]

let private editorView (ed: EditorState) (dispatch: Msg -> unit) =
    let set (ed': EditorState) = dispatch (EditorChanged ed')
    Html.form [
        prop.className "entry-editor"
        prop.onSubmit (fun e -> e.preventDefault (); dispatch SubmitEditor)
        prop.children [
            Html.h3 [ prop.text (if ed.Id.IsSome then "Edit entry" else "New entry") ]
            Html.label [
                prop.className "field"
                prop.children [
                    Html.span [ prop.text "Summary" ]
                    Html.input [
                        prop.type' "text"; prop.value ed.Summary; prop.maxLength 200; prop.autoFocus true
                        prop.placeholder "What's on"
                        prop.onChange (fun (v: string) -> set { ed with Summary = v }) ]
                ]
            ]
            Html.div [
                prop.className "field-row"
                prop.children [
                    Html.label [
                        prop.className "field"
                        prop.children [
                            Html.span [ prop.text "Date" ]
                            Html.input [
                                prop.type' "date"; prop.value (isoOf ed.Day)
                                prop.onChange (fun (v: string) ->
                                    match parseIso v with
                                    | Some d -> set { ed with Day = d }
                                    | None -> ()) ]
                        ]
                    ]
                    Html.label [
                        prop.className "field"
                        prop.children [
                            Html.span [ prop.text "Repeat" ]
                            Html.select [
                                prop.value ed.Repeat
                                prop.onChange (fun (v: string) -> set { ed with Repeat = v })
                                prop.children [ for r in Repeat.all do Html.option [ prop.value r; prop.text (repeatLabel r) ] ] ]
                        ]
                    ]
                    if ed.Repeat <> Repeat.none then
                        Html.label [
                            prop.className "field"
                            prop.children [
                                Html.span [ prop.text "Until" ]
                                Html.input [
                                    prop.type' "date"; prop.value ed.RepeatUntil
                                    prop.onChange (fun (v: string) -> set { ed with RepeatUntil = v }) ]
                            ]
                        ]
                    Html.label [
                        prop.className "field"
                        prop.children [
                            Html.span [ prop.text "Share as" ]
                            Html.select [
                                prop.value ed.Requested
                                prop.onChange (fun (v: string) -> set { ed with Requested = v })
                                prop.children [
                                    Html.option [ prop.value ""; prop.text "just for me" ]
                                    for c in Category.requestable do
                                        Html.option [ prop.value c; prop.text (c + " (needs approval)") ]
                                ] ]
                        ]
                    ]
                ]
            ]
            if ed.Published then
                Html.p [ prop.className "editor-note"; prop.text "This entry is public. Saving takes it back to personal until it is approved again." ]
            elif ed.Requested <> "" then
                Html.p [ prop.className "editor-note"; prop.text "Stays personal until an admin approves it for the shared grid." ]
            Html.div [
                prop.className "field"
                prop.children [
                    Html.span [ prop.text "Details" ]
                    Html.div [ prop.id editorElementId; prop.className "entry-editor-rich" ]
                ]
            ]
            match ed.Error with
            | Some err -> Html.div [ prop.className "error"; prop.text err ]
            | None -> Html.none
            Html.div [
                prop.className "editor-actions"
                prop.children [
                    Html.button [
                        prop.type' "submit"; prop.className "btn btn-primary"; prop.disabled ed.Saving
                        prop.text (if ed.Saving then "Saving…" else "Save") ]
                    Html.button [
                        prop.type' "button"; prop.className "btn btn-ghost"; prop.text "Cancel"
                        prop.onClick (fun _ -> dispatch CancelEditor) ]
                ]
            ]
        ]
    ]

let private dayModal (model: Model) (dispatch: Msg -> unit) (day: Day) (occ: Occurrence list) =
    Html.div [
        prop.className "modal-backdrop"
        prop.onClick (fun _ -> dispatch CloseDay)
        prop.children [
            Html.div [
                prop.className "modal"
                prop.role "dialog"
                prop.custom ("aria-modal", "true")
                prop.onClick (fun e -> e.stopPropagation ())
                prop.children [
                    Html.header [
                        prop.className "modal-head"
                        prop.children [
                            Html.h2 [ prop.text (longDayLabel locale day) ]
                            Html.button [
                                prop.className "modal-close"; prop.ariaLabel "Close"; prop.text "×"
                                prop.onClick (fun _ -> dispatch CloseDay) ]
                        ]
                    ]
                    Html.div [
                        prop.className "modal-body"
                        prop.children [
                            if List.isEmpty occ && model.Editor.IsNone then
                                Html.p [ prop.className "day-empty"; prop.text "Nothing on this day." ]
                            for o in occ do occurrenceView dispatch o
                            match model.Editor with
                            | Some ed -> editorView ed dispatch
                            | None when model.SignedIn ->
                                Html.button [
                                    prop.className "btn btn-primary add-entry"; prop.text "+ Add an entry"
                                    prop.onClick (fun _ -> dispatch StartNew) ]
                            | None -> Html.none
                        ]
                    ]
                ]
            ]
        ]
    ]

/// Render module content only — no router, header, identity switcher, or outer <main>. The host
/// supplies the frame and places this inside its own <main>. Scoped under .calendar-content.
let contentView (ctx: Content.HostContext) (model: Model) (dispatch: Msg -> unit) =
    let first, last = gridRange model.Year model.Month
    let projected = project first last model.Hidden model.Entries
    Html.div [
        prop.className "calendar-content"
        prop.children [
            Html.div [
                prop.className "cal-toolbar"
                prop.children [ monthNav ctx model; facets model dispatch ]
            ]
            match model.Error with
            | Some err ->
                Html.div [
                    prop.className "error"
                    prop.children [
                        Html.span [ prop.text err ]
                        Html.button [ prop.className "btn btn-ghost"; prop.text "dismiss"; prop.onClick (fun _ -> dispatch DismissError) ]
                    ]
                ]
            | None -> Html.none
            Html.div [
                prop.className (classes [ "cal"; (if model.Loading then "cal-loading" else "") ])
                prop.children [ weekdayRow first; grid ctx model first last projected ]
            ]
            match model.OpenDay with
            | Some d -> dayModal model dispatch d (projected |> Map.tryFind d |> Option.defaultValue [])
            | None -> Html.none
        ]
    ]
