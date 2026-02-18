module Client.App

open Feliz
open Feliz.Router
open Elmish
open Shared.Domain

/// Application state
type Model = {
    Route: string list
    Feed: Item list option
    CurrentItem: (Item * Comment list) option
    IsLoading: bool
    Error: string option
}

/// Messages
type Msg =
    | UrlChanged of string list
    | LoadFeed
    | GotFeed of Result<Item list, string>
    | LoadItem of ItemId
    | GotItem of Result<Client.Api.GetItem.Response, string>
    | DismissError

/// Initialize
let init () : Model * Cmd<Msg> =
    let route = Router.currentUrl ()
    { Route = route
      Feed = None
      CurrentItem = None
      IsLoading = false
      Error = None },
    Cmd.ofMsg LoadFeed

/// Update
let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | UrlChanged route ->
        { model with Route = route }, Cmd.none

    | LoadFeed ->
        { model with IsLoading = true },
        Cmd.OfPromise.either Client.Api.getFeed () GotFeed (fun ex -> GotFeed (Error ex.Message))

    | GotFeed (Ok items) ->
        { model with Feed = Some items; IsLoading = false; Error = None }, Cmd.none

    | GotFeed (Error err) ->
        { model with IsLoading = false; Error = Some err }, Cmd.none

    | LoadItem itemId ->
        { model with IsLoading = true },
        Cmd.OfPromise.either Client.Api.getItem itemId GotItem (fun ex -> GotItem (Error ex.Message))

    | GotItem (Ok response) ->
        { model with CurrentItem = Some (response.Item, response.Comments); IsLoading = false }, Cmd.none

    | GotItem (Error err) ->
        { model with IsLoading = false; Error = Some err }, Cmd.none

    | DismissError ->
        { model with Error = None }, Cmd.none

/// Views
module View =
    let loading =
        Html.div [
            prop.className "loading"
            prop.text "Loading..."
        ]

    let error (msg: string) dispatch =
        Html.div [
            prop.className "error"
            prop.children [
                Html.span [ prop.text msg ]
                Html.button [
                    prop.text "Dismiss"
                    prop.onClick (fun _ -> dispatch DismissError)
                ]
            ]
        ]

    let feedItem (item: Item) =
        Html.article [
            prop.className "feed-item"
            prop.children [
                Html.h2 [ prop.text item.Title ]
                match item.Link with
                | Some link -> Html.a [ prop.href link; prop.text link ]
                | None -> Html.none
                Html.div [
                    prop.className "tags"
                    prop.children (
                        item.Tags |> List.map (fun tag ->
                            Html.span [ prop.className "tag"; prop.text tag ]
                        )
                    )
                ]
            ]
        ]

    let feed (items: Item list) =
        Html.div [
            prop.className "feed"
            prop.children (items |> List.map feedItem)
        ]

    let app (model: Model) dispatch =
        Html.div [
            prop.className "app"
            prop.children [
                Html.header [
                    Html.h1 [ prop.text "Hedge" ]
                ]
                Html.main [
                    match model.Error with
                    | Some err -> error err dispatch
                    | None -> Html.none

                    if model.IsLoading then
                        loading
                    else
                        match model.Feed with
                        | Some items -> feed items
                        | None -> Html.p [ prop.text "No items yet." ]
                ]
            ]
        ]

/// Entry point
open Elmish.React

let view model dispatch = View.app model dispatch

// Use HMR in development
#if DEBUG
open Elmish.HMR
#endif

Program.mkProgram init update view
|> Program.withReactSynchronous "app"
|> Program.run
