module Client.Pages.TagItems

open Feliz
open Elmish
open Models.Api
open Client.Types
open Client.Shared

// --- Update ---

let update msg model =
    match msg with
    | LoadTagItems tag ->
        { model with IsLoading = true; TagItems = None },
        Cmd.OfPromise.either Client.ClientGen.getItemsByTag tag GotTagItems (fun ex -> GotTagItems (Error ex.Message))

    | GotTagItems (Ok response) ->
        { model with TagItems = Some response; IsLoading = false; Error = None }, Cmd.none

    | GotTagItems (Error err) ->
        { model with IsLoading = false; Error = Some err }, Cmd.none

    | _ -> model, Cmd.none

// --- View ---

let view (response: GetItemsByTag.Response) =
    Html.div [
        prop.className "feed"
        prop.children [
            Html.div [
                prop.className "tag-header"
                prop.children [ tagPill response.Tag ]
            ]
            yield! response.Items |> List.map feedItem
        ]
    ]
