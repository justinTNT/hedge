module Client.Packs

// Media packs for the bundled app: what to keep on the phone beyond the accounts themselves. A pack is
// derived from the accounts the read cache already holds, so it is exactly the photographs of the guide
// as saved, and no server manifest is needed. Sizes shown are estimates from the media set's measured
// averages; the browser's storage estimate gives the true total.

open Fable.Core
open Fable.Core.JsInterop
open Thoth.Json
open Hedge.Interface
open Models.Api

/// Base: distribution maps and glossary drawings, kept automatically (about 3 MB). Thumbnails: every
/// photograph at 640 pixels, on by default (about 105 MB). FullSize: every photograph at 1600 pixels,
/// opt in (about 450 MB).
type Pack = Base | Thumbnails | FullSize
type Settings = { Thumbnails: bool; FullSize: bool; WifiOnly: bool }
let defaults = { Thumbnails = true; FullSize = false; WifiOnly = true }

/// The guide's media as the cache holds it: accounts saved of the catalogue's total, when the text was
/// last refreshed, and each pack's URLs (distinct, in catalogue order).
type Plan = { Accounts: int; Total: int; Updated: float option; Base: string list; Thumbnails: string list; FullSize: string list }
let emptyPlan = { Accounts = 0; Total = 0; Updated = None; Base = []; Thumbnails = []; FullSize = [] }

let store = Client.Storage.store
/// Packs are for the bundled app: on the web, photographs are ordinary image requests.
let enabled () = Client.Api.apiOrigin <> ""

let private settingsKey = "settings/offline"
let private flag (value: obj) (fallback: bool) = if isNull value then fallback else unbox<bool> value
let loadSettings () : JS.Promise<Settings> = promise {
    try
        let! stored = store.Get settingsKey
        match stored with
        | Some v -> return { Thumbnails = flag v?thumbnails defaults.Thumbnails; FullSize = flag v?fullSize defaults.FullSize; WifiOnly = flag v?wifiOnly defaults.WifiOnly }
        | None -> return defaults
    with _ -> return defaults
}
let saveSettings (settings: Settings) : JS.Promise<unit> = promise {
    try do! store.Put settingsKey (createObj [ "thumbnails" ==> settings.Thumbnails; "fullSize" ==> settings.FullSize; "wifiOnly" ==> settings.WifiOnly ])
    with _ -> ()
}

let private path (Image value) = value
let private decodeAll (decoder: Decoder<'T>) (responses: (string * Hedge.Http.Response * float) list) =
    responses |> List.choose (fun (_, response, _) -> Decode.fromString decoder response.Body |> Result.toOption)
/// What the cache holds now. Only accounts already saved contribute, so a pack grows with the warm-up.
let plan () : JS.Promise<Plan> = promise {
    let! accounts = Client.Offline.cachedResponses store "" "/api/plants/plant/"
    let! catalogues = Client.Offline.cachedResponses store "" "/api/plants/catalogue"
    let! updated = promise { try return! store.Get "warm/updated" with _ -> return None }
    let details = decodeAll Codecs.Decode.getPlantResponse accounts |> List.map (fun r -> r.Plant)
    let catalogue = decodeAll Codecs.Decode.getCatalogueResponse catalogues |> List.tryHead
    let distinct urls = urls |> List.filter (fun url -> url <> "") |> List.distinct
    return
        { Accounts = details.Length
          Total = catalogue |> Option.map (fun c -> c.Plants.Length) |> Option.defaultValue 0
          Updated = updated |> Option.map unbox<float>
          Base = distinct ([ for d in details do for m in d.DistributionMaps -> path m.Image ]
                           @ [ for c in Option.toList catalogue do for g in c.Glossary -> path g.Illustration ])
          Thumbnails = distinct [ for d in details do for p in d.Photos -> path p.Thumbnail ]
          FullSize = distinct [ for d in details do for p in d.Photos -> path p.Image ] }
}

let urls (plan: Plan) = function Base -> plan.Base | Thumbnails -> plan.Thumbnails | FullSize -> plan.FullSize
/// Measured averages of the current media set (640px WebP thumbnails, 1600px large, maps and drawings).
let private averageBytes = function Base -> 5400.0 | Thumbnails -> 54000.0 | FullSize -> 231000.0
let estimate (plan: Plan) (pack: Pack) = float (urls plan pack).Length * averageBytes pack
[<Emit("Math.round($0)")>]
let private round (value: float) : int = jsNative
let megabytes (bytes: float) = if bytes < 1000000.0 then "under 1 MB" else string (round (bytes / 1000000.0)) + " MB"

let kept () : JS.Promise<Set<string>> = promise { let! urls = Client.Offline.keptMedia store in return Set.ofList urls }
let keptOf (kept: Set<string>) (urls: string list) = urls |> List.filter (fun url -> kept.Contains url) |> List.length

type Connection = Wifi | Mobile | Unknown | Offline
let connection () =
    if not (Client.Offline.isOnline ()) then Offline
    else
        match Client.Offline.connectionType () with
        | "wifi" | "ethernet" -> Wifi
        | "cellular" | "bluetooth" | "wimax" -> Mobile
        | "none" -> Offline
        | _ -> Unknown
/// Whether a pack may download by itself now: never offline; on Wi-Fi always; on mobile data, or when the
/// platform cannot tell, only when the person has allowed mobile data.
let allowed (settings: Settings) (connection: Connection) =
    match connection with
    | Offline -> false
    | Wifi -> true
    | Mobile | Unknown -> not settings.WifiOnly

[<Import("fetchImageBlob", "./image-client.mjs")>]
let private fetchBlob (path: string) : JS.Promise<obj> = jsNative
/// Fetch and keep a pack's missing URLs, three at a time, until done or `proceed` says stop.
let download (urls: string list) (onProgress: Client.Offline.MediaProgress -> unit) (proceed: unit -> bool) =
    Client.Offline.fetchMedia store fetchBlob urls 3 onProgress proceed
let forget (urls: string list) = promise { for url in urls do do! Client.Offline.forgetMedia store url }
/// Everything kept except the base pack and photographs queued for upload.
let forgetAllBut (keep: string list) = promise {
    let keep = Set.ofList keep
    let! kept = Client.Offline.keptMedia store
    for url in kept do
        if not (keep.Contains url) && not (url.StartsWith "outbox/") then do! Client.Offline.forgetMedia store url
}
