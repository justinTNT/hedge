module Client.Pages.NewItem

open Feliz
open Elmish
open Client
open Models.Api
open Client.Types

// --- Owner comment editor lifecycle ---

let mutable private ownerCommentEditorActive = false

let initOwnerCommentEditorCmd : Cmd<Msg> =
    Cmd.ofEffect (fun _dispatch ->
        if not ownerCommentEditorActive then
            ownerCommentEditorActive <- true
            RichText.createEditorWhenReady RichText.ownerCommentEditorId ""
    )

let destroyOwnerCommentEditorCmd : Cmd<Msg> =
    Cmd.ofEffect (fun _dispatch ->
        if ownerCommentEditorActive then
            RichText.destroyEditor RichText.ownerCommentEditorId
            ownerCommentEditorActive <- false
    )

// --- Update ---

let update msg model =
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
              Link = if form.Link = "" then None else Some form.Link
              Image = None
              Extract = None
              OwnerComment = ownerComment
              Tags = tags }
        { model with ItemForm = emptyItemForm },
        Cmd.OfPromise.either Client.ClientGen.submitItem req GotSubmitItem (fun ex -> GotSubmitItem (Error ex.Message))

    | GotSubmitItem (Ok _) ->
        model, Cmd.batch [
            Cmd.ofMsg LoadFeed
            Cmd.ofEffect (fun _dispatch -> RichText.clearEditor RichText.ownerCommentEditorId)
        ]

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
