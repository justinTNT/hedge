module Calendar.Client.Host.Chrome

// This app's frame: the calendar module is its PRIMARY (and only) content module, so this host owns
// the chrome — a top bar (site title + the shared identity area) around one <main> for the hosted
// calendar content. The identity control is the shared Content.IdentityView; only the frame lives here.

module Identity = Content.Identity

open Feliz
open Feliz.Router

// -- Navigation (base-aware, like every hedge host) --

let basePath = Client.Api.basePath

let private baseSegments =
    basePath.Split('/') |> Array.filter (fun s -> s <> "") |> Array.toList

/// Strip the deployment base (and a trailing ?query segment) from raw router segments.
let routeOf (segments: string list) =
    let content = segments |> List.filter (fun s -> not (s.StartsWith "?"))
    let rec strip prefix rest =
        match prefix, rest with
        | [], remaining -> remaining
        | p :: ps, r :: rs when p = r -> strip ps rs
        | _ -> content
    strip baseSegments content

let navigateTo (segments: string list) =
    Router.navigatePath (List.toArray (baseSegments @ segments))

/// Idempotent w.r.t. the base: an OAuth returnTo may already carry it.
let navigateToPath (path: string) =
    navigateTo (routeOf (path.Split('/') |> Array.filter (fun s -> s <> "") |> Array.toList))

/// Intercept only an unmodified primary click; modified clicks keep the real href.
let private onPlainClick (navigate: unit -> unit) =
    prop.onClick (fun (e: Browser.Types.MouseEvent) ->
        if e.button = 0 && not e.ctrlKey && not e.metaKey && not e.shiftKey && not e.altKey then
            e.preventDefault ()
            navigate ())

/// The frame: one header (brand + identity), one <main> with the hosted content.
let frame (idModel: Identity.Model) (dispatchId: Identity.Msg -> unit) (content: ReactElement) =
    let title = if Hedge.Tenant.config.Title <> "" then Hedge.Tenant.config.Title else "Calendar"
    Html.div [
        prop.className "app"
        prop.children [
            Html.header [
                prop.className "topbar"
                prop.children [
                    Html.a [
                        prop.className "brand"; prop.href (basePath + "/"); prop.text title
                        onPlainClick (fun () -> navigateTo []) ]
                    Content.IdentityView.identityView idModel dispatchId
                ]
            ]
            Html.main [ content ]
        ]
    ]
