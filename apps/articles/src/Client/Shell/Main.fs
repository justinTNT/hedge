module Articles.Client.Shell.Main

// Justat's client entry (unified shell, Stage 1): index.html loads this
// (/dist/client/Shell/Main.js) for every site EXCEPT ndct, which keeps the standalone
// Articles/Main.js. The shell hosts the articles content module; blog keeps its own
// /blog bundle (Blog/Main.js) until Stage 2.

open Elmish
open Elmish.React
#if DEBUG
open Elmish.HMR
#endif

Program.mkProgram Articles.Client.Shell.App.init Articles.Client.Shell.App.update Articles.Client.Shell.App.view
|> Program.withReactSynchronous "app"
|> Program.run
