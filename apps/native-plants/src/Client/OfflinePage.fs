module Client.OfflinePage

// The offline settings screen and the pack downloads it controls. The model lives in the app for the
// whole session, so a download started here carries on while the person reads the guide.

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Elmish
open Client.Packs

/// A running download: the pack, progress over the files that were missing when it started, how many
/// were already kept then, and whether the person started it by hand (which overrides the Wi-Fi rule).
type Job = { Pack: Pack; Progress: Client.Offline.MediaProgress; Before: int; Manual: bool }
type Model = {
    Settings: Settings; Plan: Plan option; Kept: Set<string>; Usage: float; Connection: Connection
    Job: Job option
    /// Packs not to restart by themselves this session: the person cancelled, or files kept failing.
    Stalled: Pack list
    Pending: int; Loading: bool; Request: int; Notice: string option
    /// The signed-in person's own photographs kept on this phone (pairs of sizes).
    Own: int
}
type Msg =
    /// Look again at what is saved, then start whatever the settings allow.
    | Check
    | Planned of int * Settings * Plan * Set<string> * float * int * int
    | Toggle of Pack * bool | WifiOnly of bool | SettingsSaved
    | Download of Pack | Cancel
    | Progress of Pack * Client.Offline.MediaProgress | Finished of Pack * Client.Offline.MediaProgress
    | Remove of Pack | RemoveAll | RemoveOwn | Removed

let mutable private cancelled = false
/// The Wi-Fi rule as currently set, read by a running download before every file.
let mutable private wifiOnly = defaults.WifiOnly
let empty = { Settings = defaults; Plan = None; Kept = Set.empty; Usage = 0.; Connection = Unknown; Job = None; Stalled = []; Pending = 0; Loading = false; Request = 0; Notice = None; Own = 0 }
let init () = empty, Cmd.ofMsg Check

let private gather request = promise {
    let! settings = loadSettings ()
    let! plan = plan ()
    let! kept = kept ()
    let! usage = Client.Offline.storageUsage ()
    let! items = Client.Sync.outbox.Items ()
    let! own = Client.Offline.keptPrivateMedia store Client.Storage.owner
    return request, settings, plan, kept, usage, items.Length, (own.Length + 1) / 2
}
let private wanted (settings: Settings) = function Base -> true | Thumbnails -> settings.Thumbnails | FullSize -> settings.FullSize
let private due (model: Model) (plan: Plan) pack = keptOf model.Kept (urls plan pack) < (urls plan pack).Length
/// The pack to download now, by itself: the base pack whenever online, the others under the settings
/// and the connection rule, none while one runs or after the person stopped it.
let private nextJob (model: Model) =
    match model.Plan, model.Job with
    | Some plan, None ->
        [ Base; Thumbnails; FullSize ] |> List.tryFind (fun pack ->
            wanted model.Settings pack && due model plan pack && not (List.contains pack model.Stalled)
            && (if pack = Base then model.Connection <> Offline else allowed model.Settings model.Connection))
    | _ -> None
/// Whether a running download may take its next file: never once stopped or offline; a download that
/// started by itself also keeps to the Wi-Fi rule as it stands now, so a move to mobile data or a change
/// of the setting pauses it; one started by hand continues on any connection.
let private mayContinue (pack: Pack) (manual: bool) () =
    not cancelled && (let current = connection () in if manual || pack = Base then current <> Offline else allowed { defaults with WifiOnly = wifiOnly } current)
let private start (plan: Plan) (model: Model) pack manual =
    cancelled <- false
    wifiOnly <- model.Settings.WifiOnly
    let all = urls plan pack
    let missing = all |> List.filter (fun url -> not (model.Kept.Contains url))
    let progress : Client.Offline.MediaProgress = { Total = missing.Length; Kept = 0; Failed = 0 }
    { model with Job = Some { Pack = pack; Progress = progress; Before = all.Length - missing.Length; Manual = manual }; Notice = None },
    Cmd.ofEffect (fun dispatch ->
        promise {
            let! result = download missing (fun p -> dispatch (Progress (pack, p))) (mayContinue pack manual)
            dispatch (Finished (pack, result))
        } |> ignore)
let private save (settings: Settings) (model: Model) =
    wifiOnly <- settings.WifiOnly
    { model with Settings = settings }, Cmd.OfPromise.either saveSettings settings (fun () -> Check) (fun _ -> Check)
let private stop (model: Model) =
    match model.Job with
    | Some job -> cancelled <- true; { model with Stalled = job.Pack :: model.Stalled |> List.distinct }
    | None -> model

let update msg (model: Model) =
    match msg with
    | Check when not (enabled ()) -> model, Cmd.none
    | Check ->
        let request = model.Request + 1
        { model with Request = request; Loading = true },
        Cmd.OfPromise.either gather request Planned (fun _ -> Planned (request, model.Settings, emptyPlan, Set.empty, 0., 0, 0))
    | Planned (request, settings, plan, kept, usage, pending, own) when request = model.Request ->
        let model = { model with Settings = settings; Plan = Some plan; Kept = kept; Usage = usage; Pending = pending; Own = own; Connection = connection (); Loading = false }
        wifiOnly <- settings.WifiOnly
        match nextJob model with
        | Some pack -> start plan model pack false
        | None -> model, Cmd.none
    | Planned _ -> model, Cmd.none
    | Toggle (pack, on) ->
        let settings =
            match pack with
            | Thumbnails -> { model.Settings with Thumbnails = on }
            | FullSize -> { model.Settings with FullSize = on }
            | Base -> model.Settings
        let model = if on then { model with Stalled = model.Stalled |> List.filter ((<>) pack) } elif model.Job |> Option.exists (fun j -> j.Pack = pack) then stop model else model
        save settings model
    | WifiOnly on -> save { model.Settings with WifiOnly = on } model
    | SettingsSaved -> model, Cmd.none
    | Download pack ->
        match model.Plan, model.Job with
        | Some plan, None when model.Connection <> Offline -> start plan { model with Stalled = model.Stalled |> List.filter ((<>) pack) } pack true
        | _ -> model, Cmd.none
    | Cancel -> stop model, Cmd.none
    | Progress (pack, progress) when model.Job |> Option.exists (fun j -> j.Pack = pack) -> { model with Job = model.Job |> Option.map (fun j -> { j with Progress = progress }) }, Cmd.none
    | Progress _ -> model, Cmd.none
    | Finished (pack, progress) ->
        let paused = not cancelled && progress.Kept + progress.Failed < progress.Total
        let stalled = if progress.Failed > 0 then pack :: model.Stalled |> List.distinct else model.Stalled
        let notice =
            if progress.Failed > 0 then Some (sprintf "%d of %d could not be fetched. Try again when the connection is better." progress.Failed progress.Total)
            elif cancelled then Some "Download stopped."
            elif paused then Some "Paused until Wi-Fi is available, or the setting allows mobile data."
            else None
        { model with Job = None; Stalled = stalled; Notice = notice }, Cmd.ofMsg Check
    | Remove pack ->
        match model.Plan with
        | Some plan ->
            let model = stop model
            let settings = match pack with Thumbnails -> { model.Settings with Thumbnails = false } | FullSize -> { model.Settings with FullSize = false } | Base -> model.Settings
            { model with Settings = settings; Loading = true },
            Cmd.OfPromise.either (fun () -> promise {
                do! saveSettings settings
                do! forget (urls plan pack) }) () (fun () -> Removed) (fun _ -> Removed)
        | None -> model, Cmd.none
    | RemoveAll ->
        match model.Plan with
        | Some plan ->
            let model = stop model
            let settings = { model.Settings with Thumbnails = false; FullSize = false }
            { model with Settings = settings; Loading = true },
            Cmd.OfPromise.either (fun () -> promise {
                do! saveSettings settings
                do! forgetAllBut plan.Base }) () (fun () -> Removed) (fun _ -> Removed)
        | None -> model, Cmd.none
    | RemoveOwn ->
        { model with Loading = true },
        Cmd.OfPromise.either (fun () -> Client.Offline.forgetPrivateMedia store Client.Storage.owner) () (fun () -> Removed) (fun _ -> Removed)
    | Removed -> { model with Loading = false; Notice = Some "Photographs removed from this phone." }, Cmd.ofMsg Check

[<Emit("new Date($0).toLocaleString()")>]
let private at (ms: float) : string = jsNative
/// The bundle's own account of itself (version, commit, build time), set by the native entry.
[<Emit("(window.PLANTS_BUILD ? window.PLANTS_BUILD.version + ' · build ' + window.PLANTS_BUILD.commit + ' · ' + new Date(window.PLANTS_BUILD.builtAt).toLocaleString() : '')")>]
let private buildStamp () : string = jsNative
let private button (label: string) disabled msg dispatch =
    Html.button [ prop.type' "button"; prop.disabled disabled; prop.text label; prop.onClick (fun _ -> dispatch msg) ]
let private check (label: string) (checked': bool) disabled (onChange: bool -> unit) =
    Html.label [ prop.className "check-field"; prop.children [
        Html.input [ prop.type' "checkbox"; prop.isChecked checked'; prop.disabled disabled; prop.onChange onChange ]
        Html.span label ] ]

let private packView (model: Model) (plan: Plan) pack (title: string) (detail: string) dispatch =
    let all = urls plan pack
    let kept = keptOf model.Kept all
    let running = model.Job |> Option.filter (fun j -> j.Pack = pack)
    let busy = model.Job.IsSome || model.Loading
    Html.section [ prop.className "pack"; prop.children [
        Html.h2 title
        Html.p detail
        match pack with
        | Base -> Html.p [ prop.className "pack-status"; prop.text (sprintf "%d of %d kept. Kept automatically with the accounts." kept all.Length) ]
        | _ ->
            check (sprintf "Keep on this phone (about %s)" (megabytes (estimate plan pack))) (wanted model.Settings pack) model.Loading (fun on -> dispatch (Toggle (pack, on)))
            Html.p [ prop.className "pack-status"; prop.text (sprintf "%d of %d kept." kept all.Length) ]
        match running with
        | Some job ->
            let done' = job.Before + job.Progress.Kept + job.Progress.Failed
            Html.progress [ prop.value done'; prop.max all.Length ]
            Html.p [ prop.role "status"; prop.text (sprintf "Fetching… %d of %d" done' all.Length) ]
        | None -> ()
        Html.div [ prop.className "pack-actions"; prop.children [
            match running with
            | Some _ -> button "Stop" false Cancel dispatch
            | None ->
                if kept < all.Length && not all.IsEmpty then button "Download now" (busy || model.Connection = Offline) (Download pack) dispatch
                if kept > 0 && pack <> Base then button "Remove from this phone" busy (Remove pack) dispatch
        ] ]
    ] ]

let view (model: Model) dispatch =
    Html.main [ prop.className "offline-page section"; prop.children [
        Html.span [ prop.className "eyebrow"; prop.text "Offline" ]
        Html.h1 "The guide on this phone"
        if not (enabled ()) then
            Html.p "Offline packs are a feature of the Android app. On the web the guide is read from the server, and pages you have opened are kept for when the connection drops."
        else
            match model.Plan with
            | None -> Html.p [ prop.role "status"; prop.text "Checking what is saved…" ]
            | Some plan ->
                Html.p [ prop.className "standfirst"
                         prop.text (sprintf "%d of %d plant accounts saved%s. Every saved account opens without signal." plan.Accounts (max plan.Total plan.Accounts)
                                        (match plan.Updated with Some t -> ", last refreshed " + at t | None -> "")) ]
                Html.p [ prop.className "build-stamp"; prop.text ((if buildStamp () = "" then "" else "App " + buildStamp () + " · ") + "guide revision " + (if plan.Revision.Length > 12 then plan.Revision.Substring(0, 12) else plan.Revision)) ]
                Html.p (sprintf "Using %s on this phone. %s%s" (megabytes model.Usage)
                            (match model.Connection with Wifi -> "On Wi-Fi." | Mobile -> "On mobile data." | Unknown -> "Connection type unknown." | Offline -> "Offline.")
                            (if model.Pending > 0 then sprintf " %d change%s waiting to send." model.Pending (if model.Pending = 1 then "" else "s") else ""))
                check "Download photographs over Wi-Fi only" model.Settings.WifiOnly model.Loading (fun on -> dispatch (WifiOnly on))
                packView model plan Base "Maps and drawings" "Distribution maps and glossary illustrations." dispatch
                packView model plan Thumbnails "Photographs at screen size" "Every photograph at 640 pixels: enough to recognise a plant in the field." dispatch
                packView model plan FullSize "Photographs at full size" "Every photograph at 1600 pixels, for leaf, bark and flower detail. Large." dispatch
                Html.div [ prop.className "pack-actions"; prop.children [
                    button "Remove all photographs from this phone" (model.Job.IsSome || model.Loading) RemoveAll dispatch ] ]
                Html.section [ prop.className "pack"; prop.children [
                    Html.h2 "Your own photographs"
                    if Client.Storage.owner = "" then
                        Html.p "Sign in and your own photographs are kept here: those you take or add on this phone, and those in your notebook as you open it. Only your account can see them."
                    else
                        Html.p (sprintf "%d kept on this phone: the photographs you took or added here, and those in your notebook as you opened it. Only your account can see them." model.Own)
                        if model.Own > 0 then
                            Html.div [ prop.className "pack-actions"; prop.children [ button "Remove your photographs from this phone" model.Loading RemoveOwn dispatch ] ]
                ] ]
                match model.Notice with
                | Some notice -> Html.p [ prop.role "status"; prop.text notice ]
                | None -> ()
    ] ]
