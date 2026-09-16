module Blog.Client.Main

// darwin.news's primary client entry: the blog module is this site's primary module, so it runs
// at the naked URL (index.html imports /dist/client/Blog/Main.js, with no MOUNT_BASE ⇒ routing
// based at "/"). CP-B: the entry now runs the single-module HOST (Blog.Client.Host.App), which
// owns the router + shared identity + chrome and drives the blog module via its hosted surface —
// mirroring the Justat shell. The module's old standalone App is no longer an entry point.

open Elmish
open Elmish.React
#if DEBUG
open Elmish.HMR
#endif

Program.mkProgram Blog.Client.Host.App.init Blog.Client.Host.App.update Blog.Client.Host.App.view
|> Program.withReactSynchronous "app"
|> Program.run
