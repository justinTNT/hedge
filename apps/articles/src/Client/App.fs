module Client.App

open Feliz
open Elmish

type Model = {
    Loading: bool
    Error: string option
}

type Msg =
    | NoOp

let init () =
    { Loading = false; Error = None }, Cmd.none

let update msg model =
    match msg with
    | NoOp -> model, Cmd.none

let view model dispatch =
    Html.div [
        prop.className "app"
        prop.children [
            Html.h1 "Articles"
            match model.Error with
            | Some err ->
                Html.div [ prop.className "error"; prop.text err ]
            | None -> Html.none
            Html.p "Edit src/Client/App.fs to get started."
        ]
    ]

open Elmish.React

Program.mkProgram init update view
|> Program.withReactSynchronous "app"
|> Program.run
