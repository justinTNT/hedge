module Client.Images
open System
open Fable.Core
open Fable.Core.JsInterop
open Feliz
[<Import("remoteImage", "./image-client.mjs")>]
let private remote (path:string) : bool = jsNative
[<Import("imageUrl", "./image-client.mjs")>]
let private resolve (path:string) : JS.Promise<string> = jsNative
[<Import("releaseImage", "./image-client.mjs")>]
let private release (url:string) : unit = jsNative
[<ReactComponent>]
let View (path:string) (properties:IReactProperty list) =
    let current,setCurrent=React.useState(("",""))
    React.useEffect((fun () ->
        let mutable disposed=false
        let mutable loaded=""
        if remote path then
            promise {
                try
                    let! url=resolve path
                    if disposed then release url
                    else loaded<-url;setCurrent(path,url)
                with _ -> ()
            } |> ignore
        {new IDisposable with member _.Dispose()=disposed<-true;release loaded}),[|box path|])
    let source,url=current
    Html.img [
        if not(remote path) then prop.src path
        elif source=path && url<>"" then prop.src url
        yield! properties
    ]
