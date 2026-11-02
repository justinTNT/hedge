module Flare.Client.Host.Main

// The client entry: index.html imports /dist/client/Host/Main.js. Runs the single-module host.

open Elmish
open Elmish.React
#if DEBUG
open Elmish.HMR
#endif

Program.mkProgram Flare.Client.Host.App.init Flare.Client.Host.App.update Flare.Client.Host.App.view
|> Program.withReactSynchronous "app"
|> Program.run
