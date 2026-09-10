module Blog.Client.Main

// The blog module's path-mount entry (the "mounting adapter"): it runs the blog
// client COMPONENT (Blog.Client.App). blog.html imports the compiled
// /dist/client/Blog/Main.js; the SPA shell is served for GET /blog[/*] by the
// worker mount (see Server/Worker.fs).

open Elmish
open Elmish.React
#if DEBUG
open Elmish.HMR
#endif

Program.mkProgram Blog.Client.App.init Blog.Client.App.update Blog.Client.App.view
|> Program.withReactSynchronous "app"
|> Program.run
