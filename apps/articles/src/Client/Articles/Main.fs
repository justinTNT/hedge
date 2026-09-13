module Articles.Client.Main

// The articles module's PRIMARY client entry: articles is this app's primary module,
// so it runs at the naked URL (index.html imports /dist/client/Articles/Main.js, with
// no MOUNT_BASE => routing based at "/"). The blog module is the secondary /blog mount
// (justat only), run by Blog/Main.fs.

open Elmish
open Elmish.React
#if DEBUG
open Elmish.HMR
#endif

Program.mkProgram Articles.Client.App.init Articles.Client.App.update Articles.Client.App.view
|> Program.withReactSynchronous "app"
|> Program.run
