module Calendar.Client.App

// The calendar module's HOSTED SURFACE (see notes/UNIFIED-SHELL.md §0). A pure content module: a
// host (an app's single-module host, or a multi-module shell) drives it with an immutable
// per-instance HostContext. The host owns the router, the identity authority and the chrome; this
// module owns only its content — the stream, its projection, and the own-entry editor. These
// functions read no browser location and boot no identity.

module GuestSession = Client.GuestSession
module RichText = Client.RichText

open Elmish
open Calendar.Api
open Calendar.Vocab
open Calendar.Client.Dates
open Calendar.Client.Types

// -- Hosted surface --

/// An idle content model — no fetch, no location read, no listeners. The host names the site's
/// facets (the same Facets value its server binds into Services) and seeds the session snapshot; a
/// first `enterHosted` drives the initial route.
let emptyHosted (facets: Facets) (session: GuestSession.GuestSessionData) : Model =
    let t = today ()
    { Facets = facets
      Route = []
      Today = t
      Year = yearOf t
      Month = monthOf t
      OpenDay = None
      Entries = []
      SignedIn = false
      Loading = false
      Loaded = false
      LoadGen = 0
      Error = None
      Hidden = Set.empty
      Editor = None
      GuestSession = session }

/// Pure snapshot of the host's authoritative session. Identity is host-owned; this never triggers a
/// sync. Visibility of personal entries is server-decided per read, so a session change is followed
/// by the host's reload (invalidateContent + enterHosted), not by anything here.
let withSession (session: GuestSession.GuestSessionData) (model: Model) : Model =
    { model with GuestSession = session }

/// Drop the cached window so the next enterHosted refetches (after an identity change the set of
/// visible entries differs). Bumps LoadGen so an in-flight read can't repopulate it.
let invalidateContent (model: Model) : Model =
    { model with Entries = []; Loaded = false; LoadGen = model.LoadGen + 1 }

/// Invalidate in-flight reads when the host leaves this module; cached entries stay for re-entry.
let invalidateInFlight (model: Model) : Model =
    { model with LoadGen = model.LoadGen + 1; Loading = false }

/// Synchronously dispose this instance's live resources (the rich-text editor). Idempotent.
let disposeHosted () : unit =
    RichText.destroyEditor editorElementId

let disposeHostedCmd : Cmd<Msg> =
    Cmd.ofEffect (fun _ -> disposeHosted ())

/// Module-local routes: [] = this month; [yyyy; mm] = a month; [yyyy; mm; dd] = that month with the
/// day modal open. Anything else shows this month.
let private parseRoute (todayDay: Day) (route: string list) : int * int * Day option =
    let tryInt (s: string) = match System.Int32.TryParse s with | true, n -> Some n | _ -> None
    let validMonth y m = y >= 1970 && y <= 2200 && m >= 1 && m <= 12
    let thisMonth () = yearOf todayDay, monthOf todayDay, None
    match route with
    | [] -> thisMonth ()
    | [ y; m ] ->
        match tryInt y, tryInt m with
        | Some y, Some m when validMonth y m -> y, m, None
        | _ -> thisMonth ()
    | [ y; m; d ] ->
        match tryInt y, tryInt m, tryInt d with
        | Some y, Some m, Some d when validMonth y m && d >= 1 && d <= daysInMonth y m -> y, m, Some (dayOf y m d)
        | _ -> thisMonth ()
    | _ -> thisMonth ()

/// Enter a module-local content route: refresh "today" (the edge read — a tab can outlive midnight),
/// set the viewed month + open day, close any editor, and (re)load when the window changes or a read
/// was still in flight.
let enterHosted (ctx: Content.HostContext) (route: string list) (model: Model) : Model * Cmd<Msg> =
    let t = today ()
    let year, month, openDay = parseRoute t route
    let resetTitle = Cmd.ofEffect (fun _ -> ctx.SetDocTitle "")
    let needsLoad = not model.Loaded || year <> model.Year || month <> model.Month || model.Loading
    let model' = { model with Route = route; Today = t; Year = year; Month = month; OpenDay = openDay; Editor = None }
    model', Cmd.batch [ disposeHostedCmd; resetTitle; (if needsLoad then Cmd.ofMsg Load else Cmd.none) ]

// -- Commands --

let private loadCmd (deps: Deps) (gen: int) (year: int) (month: int) : Cmd<Msg> =
    let first, last = gridRange year month
    let q : GetEntries.Query = { From = secondsOfDay first; To = secondsOfDay (last + 1) - 1 }
    Cmd.OfPromise.either
        (fun () -> deps.Api.calendarGetEntries q) ()
        (fun r -> GotEntries (gen, r))
        (fun ex -> GotEntries (gen, Error (Hedge.Http.TransportFailure ex.Message)))

let private saveCmd (deps: Deps) (req: SaveEntry.Request) : Cmd<Msg> =
    Cmd.OfPromise.either
        (fun () -> deps.Api.calendarSaveEntry req) ()
        GotSaved
        (fun ex -> GotSaved (Error (Hedge.Http.TransportFailure ex.Message)))

let private deleteCmd (deps: Deps) (id: string) : Cmd<Msg> =
    Cmd.OfPromise.either
        (fun () -> deps.Api.calendarDeleteEntry ({ Id = id } : DeleteEntry.Request)) ()
        (fun r -> GotDeleted (id, r))
        (fun ex -> GotDeleted (id, Error (Hedge.Http.TransportFailure ex.Message)))

// The rich-text editor is imperative (TipTap): mounted when the editor form appears, torn down when
// it goes. Images upload through the guest-gated endpoint the host enables (AllowGuestUploads).
let private mountEditorCmd (body: string) : Cmd<Msg> =
    Cmd.ofEffect (fun dispatch ->
        RichText.createEditorScoped editorElementId body (fun json -> dispatch (BodyChanged json)) (fun () -> ()) "/api/blobs/guest")

let private newEditor (day: Day) : EditorState =
    { Id = None; Day = day; Summary = ""; Body = ""; Repeat = Repeat.none; RepeatUntil = ""
      Requested = ""; Published = false; Saving = false; Error = None }

let private editorOf (e: GetEntries.EntryItem) : EditorState =
    { Id = Some e.Id
      Day = dayOfSeconds e.Date
      Summary = e.Summary
      Body = e.Body
      Repeat = e.Repeat
      RepeatUntil = e.RepeatUntil |> Option.map (dayOfSeconds >> isoOf) |> Option.defaultValue ""
      Requested = e.Requested |> Option.defaultValue ""
      Published = e.Category <> Category.personal
      Saving = false
      Error = None }

/// Handle a CONTENT message. Browser routing and identity are host-owned; `deps` carries the host
/// context + the host-injected typed API client.
let updateHosted (deps: Deps) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Load ->
        let gen = model.LoadGen + 1
        { model with LoadGen = gen; Loading = true; Loaded = true }, loadCmd deps gen model.Year model.Month

    | GotEntries (gen, _) when gen <> model.LoadGen ->
        model, Cmd.none   // a stale read from a window we've left
    | GotEntries (_, Ok resp) ->
        { model with Entries = resp.Entries; SignedIn = resp.SignedIn; Loading = false; Error = None }, Cmd.none
    | GotEntries (_, Error err) ->
        { model with Loading = false; Error = Some (Hedge.Http.renderError err) }, Cmd.none

    | ToggleCategory c ->
        let hidden = if model.Hidden.Contains c then model.Hidden.Remove c else model.Hidden.Add c
        { model with Hidden = hidden }, Cmd.none

    | CloseDay ->
        model, Cmd.ofEffect (fun _ -> deps.Ctx.Navigate (monthSegments model.Year model.Month))

    | StartNew ->
        match model.OpenDay with
        | Some day when model.SignedIn ->
            let swap = if model.Editor.IsSome then disposeHostedCmd else Cmd.none
            { model with Editor = Some (newEditor day) }, Cmd.batch [ swap; mountEditorCmd "" ]
        | _ -> model, Cmd.none

    | StartEdit e ->
        let swap = if model.Editor.IsSome then disposeHostedCmd else Cmd.none
        { model with Editor = Some (editorOf e) }, Cmd.batch [ swap; mountEditorCmd e.Body ]

    | EditorChanged ed ->
        { model with Editor = Some ed }, Cmd.none

    | BodyChanged json ->
        match model.Editor with
        | Some ed -> { model with Editor = Some { ed with Body = json } }, Cmd.none
        | None -> model, Cmd.none

    | CancelEditor ->
        { model with Editor = None }, disposeHostedCmd

    | SubmitEditor ->
        match model.Editor with
        | None -> model, Cmd.none
        | Some ed ->
            let fail msg = { model with Editor = Some { ed with Error = Some msg } }, Cmd.none
            let summary = ed.Summary.Trim()
            let until =
                if ed.Repeat = Repeat.none || ed.RepeatUntil = "" then Ok None
                else
                    match parseIso ed.RepeatUntil with
                    | Some d -> Ok (Some (secondsOfDay d))
                    | None -> Error "Until must be a date."
            if summary = "" then fail "A summary is required."
            else
                match until with
                | Error msg -> fail msg
                | Ok (Some u) when u < secondsOfDay ed.Day -> fail "Until can't be before the date."
                | Ok until ->
                    let req : SaveEntry.Request =
                        { Id = ed.Id; Date = secondsOfDay ed.Day; Summary = summary; Body = ed.Body
                          Repeat = ed.Repeat; RepeatUntil = until
                          Requested = (if ed.Requested = "" then None else Some ed.Requested) }
                    { model with Editor = Some { ed with Saving = true; Error = None } }, saveCmd deps req

    | GotSaved (Ok resp) ->
        let e = resp.Entry
        let entries =
            if model.Entries |> List.exists (fun x -> x.Id = e.Id)
            then model.Entries |> List.map (fun x -> if x.Id = e.Id then e else x)
            else model.Entries @ [ e ]
        // Show the entry where it landed. Its facet is unhidden, and if it no longer falls on the
        // open day (the date moved, or a repeat rule no longer reaches it) we follow it to its first
        // day: the host routes there and reloads the window when that is another month.
        let follow =
            match model.OpenDay with
            | Some day when Calendar.Client.Projection.occursOn day e -> Cmd.none
            | _ -> Cmd.ofEffect (fun _ -> deps.Ctx.Navigate (daySegments (dayOfSeconds e.Date)))
        { model with Entries = entries; Editor = None; SignedIn = true; Hidden = model.Hidden.Remove e.Category },
        Cmd.batch [ disposeHostedCmd; follow ]
    | GotSaved (Error err) ->
        let text = Hedge.Http.renderError err
        match model.Editor with
        | Some ed -> { model with Editor = Some { ed with Saving = false; Error = Some text } }, Cmd.none
        | None -> { model with Error = Some text }, Cmd.none

    | DeleteEntry id ->
        model, deleteCmd deps id
    | GotDeleted (id, Ok _) ->
        { model with Entries = model.Entries |> List.filter (fun x -> x.Id <> id) }, Cmd.none
    | GotDeleted (_, Error err) ->
        { model with Error = Some (Hedge.Http.renderError err) }, Cmd.none

    | DismissError ->
        { model with Error = None }, Cmd.none

/// Render module content only (see Views.contentView).
let contentView (ctx: Content.HostContext) (model: Model) (dispatch: Msg -> unit) =
    Calendar.Client.Views.contentView ctx model dispatch
