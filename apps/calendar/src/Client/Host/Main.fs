module Calendar.Client.Host.Main

// The client entry: index.html imports /dist/client/Host/Main.js. Runs the single-module host.

open Elmish
open Elmish.React
#if DEBUG
open Elmish.HMR
#endif

Program.mkProgram Calendar.Client.Host.App.init Calendar.Client.Host.App.update Calendar.Client.Host.App.view
|> Program.withReactSynchronous "app"
|> Program.run
