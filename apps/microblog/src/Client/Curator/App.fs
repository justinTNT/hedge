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
    | NotCurator
    | Ready

type Model =
    { Access: Access
      /// The signed-in identity's display name, stored INDEPENDENTLY of Access so it survives whatever
      /// order /api/auth/me and the queue response arrive in.
      Me: string option
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
    /// Carries the SUBMITTED snapshot so completion applies to that exact item, not whichever draft
    /// is open when the response lands (fixes the save-race that could clobber another item's draft).
    | FramingSaved of itemId: string * title: string * snippet: string * owner: string * status: int
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

// Fetch.fetch FAILWITHS on any non-2xx response, which would collapse our 401/403/409 handling into
// the error path. Use the raw GlobalFetch (returns the Response whatever the status) so we can branch
// on resp.Status ourselves.
let private rawFetch (url: string) (props: RequestProperties list) : JS.Promise<Response> =
    GlobalFetch.fetch(RequestInfo.Url url, requestProps props)

let private post (path: string) (body: string) : JS.Promise<int * string> =
    promise {
        let! resp =
            rawFetch (basePath + path)
                [ Method HttpMethod.POST; requestHeaders [ ContentType "application/json" ]; Body (BodyInit.Case3 body) ]
        let! text = resp.text()
        return resp.Status, text
    }

let private getText (path: string) : JS.Promise<int * string> =
    promise {
        let! resp = rawFetch (basePath + path) []
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
            // /api/auth/me shape: {"guest":{"guestId":..,"identity":{"name":..}}} or {"guest":null}.
            match Decode.fromString (Decode.field "guest" (Decode.field "identity" (Decode.field "name" Decode.string))) b with
            | Ok n -> GotMe(Some n)
            | Error _ -> GotMe None)

let init () =
    { Access = Loading; Me = None; Items = []; Providers = []; Editing = None; Busy = None; Notice = None },
    Cmd.batch [ fetchQueueCmd; fetchProvidersCmd; fetchMeCmd ]

/// Mount the shared rich-text editor, wired to the GUEST upload endpoint (the curator's signed cookie
/// authorizes /api/blobs/guest; the default /api/blobs is ADMIN_KEY-only and would 401 for a curator).
let private mountEditor (initial: string) =
    Cmd.ofEffect (fun _ ->
        Client.RichText.createEditorScoped
            Client.RichText.ownerCommentEditorId initial (fun _ -> ()) (fun () -> ())
            (basePath + "/api/blobs/guest"))

let private destroyEditor =
    Cmd.ofEffect (fun _ -> Client.RichText.destroyEditor Client.RichText.ownerCommentEditorId)

let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | GotQueue(200, body) ->
        match Decode.fromString itemsDecoder body with
        | Ok items -> { model with Access = Ready; Items = items; Notice = None }, Cmd.none
        | Error e -> { model with Access = Ready; Items = []; Notice = Some("Couldn't read the queue: " + e) }, Cmd.none
    | GotQueue(401, _) -> { model with Access = NeedLogin }, Cmd.none
    | GotQueue(403, _) -> { model with Access = NotCurator }, Cmd.none
    | GotQueue(_, _) -> { model with Access = NeedLogin; Notice = Some "Couldn't reach the server." }, Cmd.none
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
            let body =
                Encode.object
                    [ "id", Encode.string d.Id
                      "title", Encode.string d.Title
                      "snippet", Encode.string d.Snippet
                      "ownerComment", Encode.string owner ]
                |> Encode.toString 0
            { model with Busy = Some d.Id },
            Cmd.OfPromise.either (fun () -> post "/api/alerts/curation/framing" body) ()
                (fun (s, _) -> FramingSaved(d.Id, d.Title, d.Snippet, owner, s))
                (fun _ -> FramingSaved(d.Id, d.Title, d.Snippet, owner, 0))
    | Act(id, action) ->
        { model with Busy = Some id },
        Cmd.OfPromise.either (fun () -> post (sprintf "/api/alerts/curation/%s" action) (sprintf "{\"id\":\"%s\"}" id)) ()
            (fun (s, _) -> ActResult(id, s)) (fun _ -> ActResult(id, 0))
    | ActResult(id, 200) ->
        { model with Items = model.Items |> List.filter (fun i -> i.Id <> id); Busy = None; Notice = None }, Cmd.none
    | ActResult(_, 409) -> { model with Busy = None; Notice = Some "Another curator already actioned that — reloading." }, fetchQueueCmd
    | ActResult(_, (401 | 403)) -> { model with Busy = None }, fetchQueueCmd
    | ActResult(_, _) -> { model with Busy = None; Notice = Some "That didn't go through — try again." }, Cmd.none
    | FramingSaved(id, title, snippet, owner, status) ->
        // Apply to the item the request was FOR, using the submitted snapshot. Only close the editor /
        // clear Busy if they still belong to that item (the curator may have moved on to another).
        let editingThis = model.Editing |> Option.exists (fun d -> d.Id = id)
        let busy = if model.Busy = Some id then None else model.Busy
        match status with
        | 200 ->
            let items = model.Items |> List.map (fun i -> if i.Id = id then { i with Title = title; Snippet = snippet; OwnerComment = owner } else i)
            { model with Items = items; Busy = busy
                         Editing = (if editingThis then None else model.Editing)
                         Notice = Some "Saved." },
            (if editingThis then destroyEditor else Cmd.none)
        | 409 ->
            { model with Busy = busy
                         Editing = (if editingThis then None else model.Editing)
                         Notice = Some "Already actioned by another curator — reloading." },
            Cmd.batch [ (if editingThis then destroyEditor else Cmd.none); fetchQueueCmd ]
        | _ ->
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
                                Html.button [ prop.className "cx-btn cx-approve"; prop.disabled busy; prop.text "Approve"; prop.onClick (fun _ -> dispatch (Act(it.Id, "approve"))) ]
                                Html.button [ prop.className "cx-btn cx-dismiss"; prop.disabled busy; prop.text "Dismiss"; prop.onClick (fun _ -> dispatch (Act(it.Id, "dismiss"))) ]
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
