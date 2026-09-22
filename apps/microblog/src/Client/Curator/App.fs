module Client.Curator.App

// The dedicated curator page (its own document, served under BASE_PATH like admin.html). Drives its
// whole UI off the curation queue endpoint's status: 200 = curator (render queue), 401 = sign in,
// 403 = signed in but not a curator. Auth is the shared guest-cookie identity (login via /api/auth);
// framing edits reuse the shared rich-text editor. All API calls are base-path-prefixed.

open Feliz
open Elmish
open Elmish.React
open Fable.Core
open Fable.Core.JsInterop
open Fetch
open Thoth.Json

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

/// Draft of the editable framing for one item (owner comment comes from the rich-text editor on save).
type Draft = { Id: string; Title: string; Snippet: string }

type Access =
    | Loading
    | NeedLogin
    | NotCurator of who: string
    | Ready

type Model =
    { Access: Access
      Items: QueueItem list
      Providers: string list
      Editing: Draft option
      Busy: string option
      Notice: string option }

type Msg =
    | GotQueue of int * string
    | GotProviders of string list
    | GotMe of string option
    | StartEdit of string
    | DraftTitle of string
    | DraftSnippet of string
    | CancelEdit
    | SaveFraming
    | Act of itemId: string * action: string
    | ActResult of itemId: string * status: int
    | FramingResult of itemId: string * status: int
    | Reload

// -- wire decoders (codec is camelCase; OwnerComment RichContent rides as a string) --

let private itemDecoder : Decoder<QueueItem> =
    Decode.object (fun g ->
        { Id = g.Required.Field "id" Decode.string
          Title = g.Required.Field "title" Decode.string
          Link = g.Required.Field "link" Decode.string
          Snippet = g.Required.Field "snippet" Decode.string
          OwnerComment = g.Required.Field "ownerComment" Decode.string
          PublishedAt = g.Required.Field "publishedAt" Decode.int
          Topic = g.Required.Field "topic" Decode.string })

let private itemsDecoder : Decoder<QueueItem list> = Decode.field "items" (Decode.list itemDecoder)

// -- status-aware fetch --

let private post (path: string) (body: string) : JS.Promise<int * string> =
    promise {
        let! resp =
            fetch (basePath + path)
                [ Method HttpMethod.POST; requestHeaders [ ContentType "application/json" ]; Body (BodyInit.Case3 body) ]
        let! text = resp.text()
        return resp.Status, text
    }

let private getText (path: string) : JS.Promise<int * string> =
    promise {
        let! resp = fetch (basePath + path) []
        let! text = resp.text()
        return resp.Status, text
    }

let private fetchQueueCmd : Cmd<Msg> =
    Cmd.OfPromise.either (fun () -> post "/api/alerts/curation/queue" "{}") ()
        (fun (s, b) -> GotQueue(s, b)) (fun ex -> GotQueue(0, ex.Message))

let private fetchProvidersCmd : Cmd<Msg> =
    Cmd.OfPromise.perform (fun () -> getText "/api/auth/providers") ()
        (fun (_, b) ->
            match Decode.fromString (Decode.field "providers" (Decode.list Decode.string)) b with
            | Ok ps -> GotProviders ps
            | Error _ -> GotProviders [])

let private fetchMeCmd : Cmd<Msg> =
    Cmd.OfPromise.perform (fun () -> getText "/api/auth/me") ()
        (fun (_, b) ->
            match Decode.fromString (Decode.field "identity" (Decode.field "name" Decode.string)) b with
            | Ok n -> GotMe(Some n)
            | Error _ -> GotMe None)

let init () =
    { Access = Loading; Items = []; Providers = []; Editing = None; Busy = None; Notice = None },
    Cmd.batch [ fetchQueueCmd; fetchProvidersCmd; fetchMeCmd ]

let private mountEditor (initial: string) =
    Cmd.ofEffect (fun _ -> Client.RichText.createEditorWhenReady Client.RichText.ownerCommentEditorId initial)

let private destroyEditor =
    Cmd.ofEffect (fun _ -> Client.RichText.destroyEditor Client.RichText.ownerCommentEditorId)

let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | GotQueue(200, body) ->
        match Decode.fromString itemsDecoder body with
        | Ok items -> { model with Access = Ready; Items = items; Notice = None }, Cmd.none
        | Error e -> { model with Access = Ready; Items = []; Notice = Some("Couldn't read the queue: " + e) }, Cmd.none
    | GotQueue(401, _) -> { model with Access = NeedLogin }, Cmd.none
    | GotQueue(403, _) -> { model with Access = NotCurator "" }, Cmd.none
    | GotQueue(_, _) -> { model with Access = NeedLogin; Notice = Some "Couldn't reach the server." }, Cmd.none
    | GotProviders ps -> { model with Providers = ps }, Cmd.none
    | GotMe name ->
        match model.Access, name with
        | NotCurator _, Some n -> { model with Access = NotCurator n }, Cmd.none
        | _ -> model, Cmd.none
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
            let owner = Client.RichText.getEditorContent Client.RichText.ownerCommentEditorId
            let body =
                Encode.object
                    [ "id", Encode.string d.Id
                      "title", Encode.string d.Title
                      "snippet", Encode.string d.Snippet
                      "ownerComment", Encode.string owner ]
                |> Encode.toString 0
            { model with Busy = Some d.Id },
            Cmd.OfPromise.either (fun () -> post "/api/alerts/curation/framing" body) ()
                (fun (s, _) -> FramingResult(d.Id, s)) (fun _ -> FramingResult(d.Id, 0))
    | Act(id, action) ->
        { model with Busy = Some id },
        Cmd.OfPromise.either (fun () -> post (sprintf "/api/alerts/curation/%s" action) (sprintf "{\"id\":\"%s\"}" id)) ()
            (fun (s, _) -> ActResult(id, s)) (fun _ -> ActResult(id, 0))
    | ActResult(id, 200) ->
        { model with Items = model.Items |> List.filter (fun i -> i.Id <> id); Busy = None; Notice = None }, Cmd.none
    | ActResult(_, 409) -> { model with Busy = None; Notice = Some "Another curator already actioned that — reloading." }, fetchQueueCmd
    | ActResult(_, (401 | 403)) -> { model with Busy = None }, fetchQueueCmd
    | ActResult(_, _) -> { model with Busy = None; Notice = Some "That didn't go through — try again." }, Cmd.none
    | FramingResult(id, 200) ->
        let editing = model.Editing
        let items =
            model.Items
            |> List.map (fun i ->
                match editing with
                | Some d when d.Id = i.Id ->
                    { i with Title = d.Title; Snippet = d.Snippet
                             OwnerComment = Client.RichText.getEditorContent Client.RichText.ownerCommentEditorId }
                | _ -> i)
        { model with Items = items; Editing = None; Busy = None; Notice = Some "Saved." }, destroyEditor
    | FramingResult(_, 409) -> { model with Editing = None; Busy = None; Notice = Some "Already actioned — reloading." }, Cmd.batch [ destroyEditor; fetchQueueCmd ]
    | FramingResult(_, _) -> { model with Busy = None; Notice = Some "Couldn't save — try again." }, Cmd.none
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
                        Html.p [ prop.className "cx-dim"; prop.text "No sign-in providers are configured." ]
                    for p in providers ->
                        Html.button [
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
            match editing with
            | Some d ->
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
            | None ->
                Html.a [ prop.className "cx-title"; prop.href it.Link; prop.target "_blank"; prop.text it.Title ]
                if it.Snippet <> "" then Html.p [ prop.className "cx-snippet"; prop.text it.Snippet ]
                Html.div [
                    prop.className "cx-actions"
                    prop.children [
                        Html.button [ prop.className "cx-btn cx-approve"; prop.disabled busy; prop.text "Approve"; prop.onClick (fun _ -> dispatch (Act(it.Id, "approve"))) ]
                        Html.button [ prop.className "cx-btn cx-dismiss"; prop.disabled busy; prop.text "Dismiss"; prop.onClick (fun _ -> dispatch (Act(it.Id, "dismiss"))) ]
                        Html.button [ prop.className "cx-btn cx-ghost"; prop.disabled busy; prop.text "Edit framing"; prop.onClick (fun _ -> dispatch (StartEdit it.Id)) ]
                    ]
                ]
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
            match model.Notice with Some n -> Html.div [ prop.className "cx-notice"; prop.text n ] | None -> Html.none
            if List.isEmpty model.Items then
                Html.p [ prop.className "cx-dim"; prop.text "Nothing to review — the queue is empty." ]
            else
                for it in model.Items -> itemView model dispatch it
        ]
    ]

let view (model: Model) dispatch =
    Html.div [
        prop.className "cx-app"
        prop.children [
            match model.Access with
            | Loading -> Html.div [ prop.className "cx-center"; prop.text "Loading…" ]
            | NeedLogin -> loginView model.Providers dispatch
            | NotCurator who ->
                Html.div [
                    prop.className "cx-center"
                    prop.children [
                        Html.h1 [ prop.text "Curate" ]
                        Html.p [ prop.text (if who = "" then "You're signed in, but you don't have curator access." else sprintf "You're signed in as %s, but you don't have curator access." who) ]
                    ]
                ]
            | Ready -> queueView model dispatch
        ]
    ]

Program.mkProgram init update view
|> Program.withReactSynchronous "app"
|> Program.run
