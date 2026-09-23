module Client.Main

open Elmish
open Elmish.React

Program.mkProgram Client.App.init Client.App.update Client.App.view
|> Program.withReactSynchronous "app"
|> Program.run
