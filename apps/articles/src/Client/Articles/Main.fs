module Articles.Client.Main

// The articles module's PRIMARY client entry (ndct standalone): articles is this app's primary
// module, so it runs at the naked URL (index.html imports /dist/client/Articles/Main.js, with no
// MOUNT_BASE => routing based at "/"). CP-B: the entry now runs the single-module HOST
// (Articles.Client.Host.App), which owns the router + shared identity + chrome and drives the
// articles module via its hosted surface — mirroring the Justat shell. Justat uses the Shell
// (Shell/Main.fs) instead; this entry is only built for ndct.

open Elmish
open Elmish.React
#if DEBUG
open Elmish.HMR
#endif

Program.mkProgram Articles.Client.Host.App.init Articles.Client.Host.App.update Articles.Client.Host.App.view
|> Program.withReactSynchronous "app"
|> Program.run
