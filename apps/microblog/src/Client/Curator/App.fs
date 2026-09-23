module Client.Curator.App

// The dedicated curator page (its own document, served under BASE_PATH like admin.html). Drives its
// whole UI off the curation queue endpoint's status: 200 = curator (render queue), 401 = sign in,
// 403 = signed in but not a curator. Auth is the shared guest-cookie identity; framing edits reuse
// the shared rich-text editor.
//
// Wire layer: the GENERATED alerts client (Alerts.ClientGen over Client.Api.browserTransport) — no
// hand-written wire records, endpoint strings, or JSON here. The shared browserTransport already
// preserves 401/403/409 as typed Hedge.Http.ApiError (it never throws on non-2xx), which is why the
// old raw-fetch workaround is gone. Identity comes from the shared Client.GuestSession, not a bespoke
// /api/auth/me decode.

open Feliz
open Elmish
open Elmish.React
open Fable.Core
open Fable.Core.JsInterop
open Thoth.Json
open Hedge.Interface   // RichContent (unwrap the generated Queue.Item owner comment)

/// The generated typed alerts client, constructed once over the shared browser transport.
let private api = Alerts.ClientGen.createClient Client.Api.browserTransport

[<Emit("window.BASE_PATH || ''")>]
let private basePath : string = jsNative

[<Emit("window.location.assign($0)")>]
let private go (url: string) : unit = jsNative

[<Emit("encodeURIComponent($0)")>]
let private enc (s: string) : string = jsNative

[<Emit("new Date($0 * 1000).toLocaleDateString()")>]
let private fmtDate (epoch: int) : string = jsNative

// -- model --

type QueueItem =
    { Id: string; Title: string; Link: string; Snippet: string
      OwnerComment: string; PublishedAt: int; Topic: string }

/// Project the generated Queue.Item onto the view model (unwrapping the RichContent owner comment to
/// the raw TipTap-JSON string the editor seeds from / writes back).
let private toItem (it: Alerts.Api.Queue.Item) : QueueItem =
    let (RichContent oc) = it.OwnerComment
    { Id = it.Id; Title = it.Title; Link = it.Link; Snippet = it.Snippet
      OwnerComment = oc; PublishedAt = it.PublishedAt; Topic = it.Topic }

/// Draft of the editable framing for one item (owner comment comes from the rich-text editor on save).
type Draft = { Id: string; Title: string; Snippet: string }

type Access =
    | Loading
    | NeedLogin
    | NotCurator
    | Ready

type Model =
    { Access: Access
      /// The signed-in identity's display name, stored INDEPENDENTLY of Access so it survives whatever
      /// order the identity sync and the queue response arrive in.
      Me: string option
      Items: QueueItem list
      Providers: string list
      Editing: Draft option
      Busy: string option
      Notice: string option }

/// Approve/Dismiss/EditFraming responses are all { Ok } — we only care Ok-vs-typed-error, so the
/// completion messages carry Result<unit, ApiError> (the status lives in HttpFailure).
type ActionResult = Result<unit, Hedge.Http.ApiError>

type Msg =
    | GotQueue of Result<Alerts.Api.Queue.Response, Hedge.Http.ApiError>
    | GotProviders of string list
    | GotMe of string option
    | StartEdit of string
    | DraftTitle of string
    | DraftSnippet of string
    | CancelEdit
    | SaveFraming
    | Act of itemId: string * approve: bool
    | ActResult of itemId: string * ActionResult
    /// Carries the SUBMITTED snapshot so completion applies to that exact item, not whichever draft is
    /// open when the response lands (fixes the save-race that could clobber another item's draft).
    | FramingSaved of itemId: string * title: string * snippet: string * owner: string * ActionResult
    | Reload

// -- commands (generated client + shared session; no hand-rolled fetch/JSON) --

let private fetchQueueCmd : Cmd<Msg> =
    Cmd.OfPromise.either (fun () -> api.alertsQueue { Cursor = None }) ()
        GotQueue (fun ex -> GotQueue (Error (Hedge.Http.TransportFailure ex.Message)))

/// Provider list for the sign-in buttons — via the shared transport helper (a static list, not
/// session decoding; the signed-in identity itself comes from Client.GuestSession below).
let private fetchProvidersCmd : Cmd<Msg> =
    Cmd.OfPromise.perform
        (fun () -> Client.Api.fetchJson "/api/auth/providers" (Decode.field "providers" (Decode.list Decode.string)))
        ()
        (function Ok ps -> GotProviders ps | Error _ -> GotProviders [])

/// The signed-in identity from the shared guest-session client (server-authoritative sync), NOT a
/// bespoke /api/auth/me decode. None when signed in only as an anonymous guest.
let private fetchMeCmd : Cmd<Msg> =
    Cmd.OfPromise.perform (fun () -> Client.GuestSession.syncSession ()) ()
        (fun session -> GotMe (session.Identity |> Option.map (fun i -> i.Name)))

let init () =
    { Access = Loading; Me = None; Items = []; Providers = []; Editing = None; Busy = None; Notice = None },
    Cmd.batch [ fetchQueueCmd; fetchProvidersCmd; fetchMeCmd ]

/// Mount the shared rich-text editor, wired to the GUEST upload endpoint (the curator's signed cookie
/// authorizes /api/blobs/guest; the default /api/blobs is ADMIN_KEY-only and would 401 for a curator).
/// Note: the editor's upload goes through HedgeRT, not browserTransport, so the base path is applied
/// here explicitly (browserTransport's automatic prefixing does not reach it).
let private mountEditor (initial: string) =
    Cmd.ofEffect (fun _ ->
        Client.RichText.createEditorScoped
            Client.RichText.ownerCommentEditorId initial (fun _ -> ()) (fun () -> ())
            (basePath + "/api/blobs/guest"))

let private destroyEditor =
    Cmd.ofEffect (fun _ -> Client.RichText.destroyEditor Client.RichText.ownerCommentEditorId)

/// Approve/Dismiss share the { Ok } response shape but are distinct generated types; map either to
/// unit so ActResult can carry one type.
let private actCmd (id: string) (approve: bool) : Cmd<Msg> =
    // Approve/Dismiss are distinct generated types with the same { Ok } shape; map each to unit so
    // both branches share JS.Promise<ActionResult>.
    let run () : JS.Promise<ActionResult> =
        if approve then promise { let! r = api.alertsApprove { Id = id } in return Result.map ignore r }
        else promise { let! r = api.alertsDismiss { Id = id } in return Result.map ignore r }
    Cmd.OfPromise.either run ()
        (fun r -> ActResult(id, r)) (fun ex -> ActResult(id, Error (Hedge.Http.TransportFailure ex.Message)))

let private status = function
    | Hedge.Http.HttpFailure (s, _) | Hedge.Http.ValidationFailure (s, _) -> Some s
    | _ -> None

let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | GotQueue (Ok resp) ->
        { model with Access = Ready; Items = resp.Items |> List.map toItem; Notice = None }, Cmd.none
    | GotQueue (Error err) ->
        match status err with
        | Some 401 -> { model with Access = NeedLogin }, Cmd.none
        | Some 403 -> { model with Access = NotCurator }, Cmd.none
        | _ -> { model with Access = NeedLogin; Notice = Some "Couldn't reach the server." }, Cmd.none
    | GotProviders ps -> { model with Providers = ps }, Cmd.none
    | GotMe name -> { model with Me = name }, Cmd.none
    | StartEdit id ->
        match model.Items |> List.tryFind (fun i -> i.Id = id) with
        | None -> model, Cmd.none
        | Some it ->
            { model with Editing = Some { Id = id; Title = it.Title; Snippet = it.Snippet } },
            mountEditor it.OwnerComment
    | DraftTitle t -> { model with Editing = model.Editing |> Option.map (fun d -> { d with Title = t }) }, Cmd.none
    | DraftSnippet s -> { model with Editing = model.Editing |> Option.map (fun d -> { d with Snippet = s }) }, Cmd.none
    | CancelEdit -> { model with Editing = None }, destroyEditor
    | SaveFraming ->
        match model.Editing with
        | None -> model, Cmd.none
        | Some d ->
            // Snapshot the submitted content NOW (id + title + snippet + the editor's owner comment)
            // and carry it through completion, so a slow response applies to THIS item — never to
            // whichever draft happens to be open when it lands.
            let owner = Client.RichText.getEditorContent Client.RichText.ownerCommentEditorId
            let req : Alerts.Api.EditFraming.Request = { Id = d.Id; Title = d.Title; Snippet = d.Snippet; OwnerComment = owner }
            { model with Busy = Some d.Id },
            Cmd.OfPromise.either
                (fun () -> promise { let! r = api.alertsEditFraming req in return Result.map ignore r })
                ()
                (fun r -> FramingSaved(d.Id, d.Title, d.Snippet, owner, r))
                (fun ex -> FramingSaved(d.Id, d.Title, d.Snippet, owner, Error (Hedge.Http.TransportFailure ex.Message)))
    | Act(id, approve) ->
        { model with Busy = Some id }, actCmd id approve
    | ActResult(id, Ok ()) ->
        { model with Items = model.Items |> List.filter (fun i -> i.Id <> id); Busy = None; Notice = None }, Cmd.none
    | ActResult(_, Error err) ->
        match status err with
        | Some 409 -> { model with Busy = None; Notice = Some "Another curator already actioned that — reloading." }, fetchQueueCmd
        | Some 401 | Some 403 -> { model with Busy = None }, fetchQueueCmd
        | _ -> { model with Busy = None; Notice = Some "That didn't go through — try again." }, Cmd.none
    | FramingSaved(id, title, snippet, owner, result) ->
        // Apply to the item the request was FOR, using the submitted snapshot. Only close the editor /
        // clear Busy if they still belong to that item (the curator may have moved on to another).
        let editingThis = model.Editing |> Option.exists (fun d -> d.Id = id)
        let busy = if model.Busy = Some id then None else model.Busy
        match result with
        | Ok () ->
            let items = model.Items |> List.map (fun i -> if i.Id = id then { i with Title = title; Snippet = snippet; OwnerComment = owner } else i)
            { model with Items = items; Busy = busy
                         Editing = (if editingThis then None else model.Editing)
                         Notice = Some "Saved." },
            (if editingThis then destroyEditor else Cmd.none)
        | Error err when status err = Some 409 ->
            { model with Busy = busy
                         Editing = (if editingThis then None else model.Editing)
                         Notice = Some "Already actioned by another curator — reloading." },
            Cmd.batch [ (if editingThis then destroyEditor else Cmd.none); fetchQueueCmd ]
        | Error _ ->
            { model with Busy = busy; Notice = Some "Couldn't save — try again." }, Cmd.none
    | Reload -> { model with Notice = None }, fetchQueueCmd

// -- views --

let private loginView (providers: string list) dispatch =
    Html.div [
        prop.className "cx-center"
        prop.children [
            Html.h1 [ prop.text "Curate" ]
            Html.p [ prop.text "Sign in to review pending posts." ]
            Html.div [
                prop.className "cx-providers"
                prop.children [
                    if List.isEmpty providers then
                        yield Html.p [ prop.className "cx-dim"; prop.text "No sign-in providers are configured." ]
                    for p in providers do
                        yield Html.button [
                            prop.className "cx-btn"
                            prop.text (sprintf "Sign in with %s" p)
                            prop.onClick (fun _ ->
                                go (basePath + "/api/auth/" + p + "/login?returnTo=" + enc (basePath + "/curator")))
                        ]
                ]
            ]
        ]
    ]

let private itemView (model: Model) dispatch (it: QueueItem) =
    let busy = model.Busy = Some it.Id
    let editing = model.Editing |> Option.filter (fun d -> d.Id = it.Id)
    Html.div [
        prop.className "cx-item"
        prop.children [
            Html.div [
                prop.className "cx-meta"
                prop.children [
                    Html.span [ prop.className "cx-topic"; prop.text it.Topic ]
                    Html.span [ prop.className "cx-date"; prop.text (fmtDate it.PublishedAt) ]
                ]
            ]
            (match editing with
             | Some d ->
                Html.div [
                    prop.className "cx-edit"
                    prop.children [
                        Html.input [
                            prop.className "cx-input"; prop.value d.Title
                            prop.onChange (fun (v: string) -> dispatch (DraftTitle v))
                        ]
                        Html.textarea [
                            prop.className "cx-textarea"; prop.value d.Snippet; prop.rows 3
                            prop.onChange (fun (v: string) -> dispatch (DraftSnippet v))
                        ]
                        Html.label [ prop.className "cx-label"; prop.text "Owner comment" ]
                        Html.div [ prop.id Client.RichText.ownerCommentEditorId; prop.className "cx-editor" ]
                        Html.div [
                            prop.className "cx-actions"
                            prop.children [
                                Html.button [ prop.className "cx-btn"; prop.disabled busy; prop.text "Save"; prop.onClick (fun _ -> dispatch SaveFraming) ]
                                Html.button [ prop.className "cx-btn cx-ghost"; prop.text "Cancel"; prop.onClick (fun _ -> dispatch CancelEdit) ]
                            ]
                        ]
                    ]
                ]
             | None ->
                Html.div [
                    prop.className "cx-view"
                    prop.children [
                        Html.a [ prop.className "cx-title"; prop.href it.Link; prop.target "_blank"; prop.text it.Title ]
                        (if it.Snippet <> "" then Html.p [ prop.className "cx-snippet"; prop.text it.Snippet ] else Html.none)
                        Html.div [
                            prop.className "cx-actions"
                            prop.children [
                                Html.button [ prop.className "cx-btn cx-approve"; prop.disabled busy; prop.text "Approve"; prop.onClick (fun _ -> dispatch (Act(it.Id, true))) ]
                                Html.button [ prop.className "cx-btn cx-dismiss"; prop.disabled busy; prop.text "Dismiss"; prop.onClick (fun _ -> dispatch (Act(it.Id, false))) ]
                                Html.button [ prop.className "cx-btn cx-ghost"; prop.disabled busy; prop.text "Edit framing"; prop.onClick (fun _ -> dispatch (StartEdit it.Id)) ]
                            ]
                        ]
                    ]
                ])
        ]
    ]

let private queueView (model: Model) dispatch =
    Html.div [
        prop.className "cx-queue"
        prop.children [
            Html.div [
                prop.className "cx-head"
                prop.children [
                    Html.h1 [ prop.text "Curation queue" ]
                    Html.button [ prop.className "cx-btn cx-ghost"; prop.text "Reload"; prop.onClick (fun _ -> dispatch Reload) ]
                ]
            ]
            (match model.Notice with Some n -> Html.div [ prop.className "cx-notice"; prop.text n ] | None -> Html.none)
            (if List.isEmpty model.Items then
                Html.p [ prop.className "cx-dim"; prop.text "Nothing to review — the queue is empty." ]
             else
                Html.div [ prop.className "cx-list"; prop.children (model.Items |> List.map (itemView model dispatch)) ])
        ]
    ]

let view (model: Model) dispatch =
    Html.div [
        prop.className "cx-app"
        prop.children [
            match model.Access with
            | Loading -> Html.div [ prop.className "cx-center"; prop.text "Loading…" ]
            | NeedLogin -> loginView model.Providers dispatch
            | NotCurator ->
                Html.div [
                    prop.className "cx-center"
                    prop.children [
                        Html.h1 [ prop.text "Curate" ]
                        Html.p [ prop.text (match model.Me with
                                            | Some n -> sprintf "You're signed in as %s, but you don't have curator access." n
                                            | None -> "You're signed in, but you don't have curator access.") ]
                    ]
                ]
            | Ready -> queueView model dispatch
        ]
    ]

Program.mkProgram init update view
|> Program.withReactSynchronous "app"
|> Program.run
