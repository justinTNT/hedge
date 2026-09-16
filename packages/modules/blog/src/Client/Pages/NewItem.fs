module Blog.Client.Pages.NewItem

// The shared rich-text module lives in the host's Client.RichText namespace.
module RichText = Client.RichText

open Feliz
open Elmish
open Blog.Client
open Blog.Api
open Blog.Client.Types

// --- Owner comment editor lifecycle ---

let mutable private ownerCommentEditorActive = false

let initOwnerCommentEditorCmd : Cmd<Msg> =
    Cmd.ofEffect (fun _dispatch ->
        if not ownerCommentEditorActive then
            ownerCommentEditorActive <- true
            RichText.createEditorWhenReady RichText.ownerCommentEditorId ""
    )

/// Destroy the owner-comment editor now (synchronous, idempotent). Plain function for
/// ordered host-driven disposal.
let destroyOwnerCommentEditor () : unit =
    if ownerCommentEditorActive then
        RichText.destroyEditor RichText.ownerCommentEditorId
        ownerCommentEditorActive <- false

let destroyOwnerCommentEditorCmd : Cmd<Msg> =
    Cmd.ofEffect (fun _dispatch -> destroyOwnerCommentEditor ())

// --- Update ---

let update (deps: Deps) msg model =
    match msg with
    | SetNewItemTitle title ->
        { model with ItemForm = { model.ItemForm with Title = title } }, Cmd.none

    | SetNewItemLink link ->
        { model with ItemForm = { model.ItemForm with Link = link } }, Cmd.none

    | SetNewItemTags tags ->
        { model with ItemForm = { model.ItemForm with Tags = tags } }, Cmd.none

    | SubmitItem ->
        let form = model.ItemForm
        let ownerComment = RichText.getEditorContent RichText.ownerCommentEditorId
        let tags =
            form.Tags.Split(',')
            |> Array.map (fun s -> s.Trim())
            |> Array.filter (fun s -> s <> "")
            |> Array.toList
        let req : SubmitItem.Request =
            { Title = form.Title
              Slug = None
              Link = if form.Link = "" then None else Some form.Link
              Image = None
              Extract = None
              OwnerComment = ownerComment
              Tags = tags }
        { model with ItemForm = emptyItemForm },
        Cmd.OfPromise.either deps.Api.blogSubmitItem req GotSubmitItem (fun ex -> GotSubmitItem (Error (Hedge.Http.TransportFailure ex.Message)))

    | GotSubmitItem (Ok _) ->
        // The item is created server-side regardless; post-create effects run only while still on
        // "new". CP-A finding 5: do NOT dispatch LoadFeed here — it sets the shared IsLoading the
        // "new" view renders as a spinner, and the []-guarded GotFeed then drops the response,
        // stranding the form behind that spinner. Instead invalidate the cached feed (bumping
        // LoadGen so any in-flight feed read is dropped); enterHosted's "Feed=None ⇒ LoadFeed"
        // refetches it lazily on the next feed visit.
        match model.Route with
        | [ "new" ] ->
            { model with Feed = None; LoadGen = model.LoadGen + 1 },
            Cmd.ofEffect (fun _dispatch -> RichText.clearEditor RichText.ownerCommentEditorId)
        | _ -> model, Cmd.none

    | GotSubmitItem (Error err) ->
        { model with Error = Some err }, Cmd.none

    | _ -> model, Cmd.none

// --- View ---

let view (form: ItemForm) dispatch =
    Html.div [
        prop.className "new-item-form"
        prop.children [
            Html.h2 [ prop.text "New Item" ]
            Html.input [
                prop.placeholder "Title"
                prop.value form.Title
                prop.onChange (SetNewItemTitle >> dispatch)
            ]
            Html.input [
                prop.placeholder "Link (optional)"
                prop.value form.Link
                prop.onChange (SetNewItemLink >> dispatch)
            ]
            Html.div [ prop.id RichText.ownerCommentEditorId ]
            Html.input [
                prop.placeholder "Tags (comma-separated)"
                prop.value form.Tags
                prop.onChange (SetNewItemTags >> dispatch)
            ]
            Html.button [
                prop.text "Create"
                prop.disabled (form.Title.Trim() = "")
                prop.onClick (fun _ -> dispatch SubmitItem)
            ]
        ]
    ]
