module Client.Pages.Feed

open Feliz
open Elmish
open Models.Api
open Client.Types
open Client.Shared

let update msg model =
    match msg with
    | LoadFeed ->
        { model with IsLoading = true },
        Cmd.OfPromise.either Client.ClientGen.getFeed () GotFeed (fun ex -> GotFeed (Error ex.Message))

    | GotFeed (Ok response) ->
        { model with Feed = Some response; IsLoading = false; Error = None }, Cmd.none

    | GotFeed (Error err) ->
        { model with IsLoading = false; Error = Some err }, Cmd.none

    | _ -> model, Cmd.none

let view (response: GetFeed.Response) =
    Html.div [
        prop.className "feed"
        prop.children (response.Items |> List.map feedItem)
    ]
