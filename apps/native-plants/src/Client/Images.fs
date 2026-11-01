module Client.Images
open System
open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Hedge.Interface
[<Import("remoteImage", "./image-client.mjs")>]
let private remote (path:string) : bool = jsNative
[<Import("fetchImageBlob", "./image-client.mjs")>]
let private fetchBlob (path:string) : JS.Promise<obj> = jsNative

/// A photograph queued for upload and kept on this phone (Client.Sync), shown from the store everywhere.
let private local (path:string) = path.StartsWith "outbox/"
/// Resolve a photograph to an object URL: the kept copy first, else fetched from the API host (bundled
/// app) and kept, so what has been seen stays available offline. Catalogue media is kept for the device;
/// a person's own photographs (/api/ paths) are kept under their identity and found there alone.
let resolve (path:string) : JS.Promise<string> = promise {
    let own = path.StartsWith "/api/"
    let! kept =
        if own then Client.Offline.privateMediaBlob Client.Storage.store Client.Storage.owner path
        else Client.Offline.mediaBlob Client.Storage.store path
    match kept with
    | Some blob -> return Client.Offline.objectUrl blob
    | None when local path -> return failwith "This photograph is no longer on this phone."
    | None ->
        let! blob = fetchBlob path
        match own, Client.Storage.owner with
        | true, "" -> ()
        | true, owner -> do! Client.Offline.keepPrivateMedia Client.Storage.store owner path blob
        | false, _ -> do! Client.Offline.keepMedia Client.Storage.store path blob
        return Client.Offline.objectUrl blob
}
let release (url:string) = if not (isNull url) && url.StartsWith "blob:" then Client.Offline.releaseObjectUrl url

[<Emit("(async()=>{const image=new Image();image.src=$0;await image.decode();return [image.naturalWidth,image.naturalHeight];})()")>]
let private decode (url:string) : JS.Promise<int*int> = jsNative
/// A photograph's natural size through the same local-first resolution; (0,0) when it cannot load.
let measure (path:string) : JS.Promise<string*int*int> = promise {
    try
        let! url = if remote path || local path then resolve path else promise { return path }
        let! size = decode url
        release url
        return path,fst size,snd size
    with _ -> return path,0,0
}

[<ReactComponent>]
let View (image:Image) (properties:IReactProperty list) =
    let (Image path)=image
    let current,setCurrent=React.useState(("",""))
    React.useEffect((fun () ->
        let mutable disposed=false
        let mutable loaded=""
        if remote path || local path then
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
        if not(remote path || local path) then prop.src path
        elif source=path && url<>"" then prop.src url
        yield! properties
    ]
