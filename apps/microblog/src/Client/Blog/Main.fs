module Blog.Client.Main

// darwin.news's primary client entry: the blog module is this site's primary
// module, so it runs at the naked URL (index.html imports /dist/client/Blog/Main.js,
// with no MOUNT_BASE ⇒ routing based at "/").

open Elmish
open Elmish.React
#if DEBUG
open Elmish.HMR
#endif

Program.mkProgram Blog.Client.App.init Blog.Client.App.update Blog.Client.App.view
|> Program.withReactSynchronous "app"
|> Program.run
