module Client.Shared

open Feliz
open Feliz.Router
open Fable.Core
open Hedge.Interface
open Models.Api
open Client.Types

// -- Base path support (for deploying under a subpath, e.g. /st) --

[<Emit("window.BASE_PATH || ''")>]
let private basePath : string = jsNative

let private baseSegments =
    if basePath = "" then []
    else basePath.TrimStart('/').Split('/') |> Array.filter (fun s -> s <> "") |> Array.toList

let currentRoute () =
    let url = Router.currentPath ()
    let rec strip ps rs =
        match ps, rs with
        | [], r -> r
        | p :: pt, r :: rt when p = r -> strip pt rt
        | _ -> rs
    strip baseSegments url

let navigateTo (segments: string list) =
    let path = basePath.TrimEnd('/') + "/" + (segments |> String.concat "/")
    Router.navigatePath path

let private tagColors = [| "#e74c3c"; "#3498db"; "#2ecc71"; "#9b59b6"; "#f39c12"; "#1abc9c"; "#e91e63"; "#00bcd4" |]

let private tagColor (name: string) =
    let hash = name.ToCharArray() |> Array.fold (fun acc c -> int c + acc * 31) 0
    tagColors.[abs hash % tagColors.Length]

let tagPill (tag: string) =
    Html.span [
        prop.className "tag"
        prop.style [
            style.backgroundColor (tagColor tag)
            style.color "#fff"
            style.cursor.pointer
        ]
        prop.text tag
        prop.onClick (fun e ->
            e.stopPropagation ()
            navigateTo ["tag"; tag]
        )
    ]

let loading =
    Html.div [
        prop.className "loading"
        prop.text "Loading..."
    ]

let error (msg: string) dispatch =
    Html.div [
        prop.className "error"
        prop.children [
            Html.span [ prop.text msg ]
            Html.button [
                prop.text "Dismiss"
                prop.onClick (fun _ -> dispatch DismissError)
            ]
        ]
    ]

let feedItem (item: GetFeed.FeedItem) =
    let itemPath = item.Slug |> Option.defaultValue item.Id
    Html.article [
        prop.className "feed-item"
        prop.style [ style.cursor.pointer ]
        prop.onClick (fun _ -> navigateTo [item.Slug |> Option.defaultValue item.Id])
        prop.children [
            Html.h2 [ prop.text item.Title ]
            match item.Extract with
            | Some (RichContent text) ->
                Html.p [ prop.className "extract"; 
                         prop.children [
                                   Html.span [ prop.text (RichText.extractPlainText text) ];
                                   match item.Image with
                                   | Some url ->
                                       Html.img [ prop.src url ]
                                   | None -> Html.none
                               ]
                ]
            | None -> Html.none
        ]
    ]

let avatar (url: string) =
    Html.img [
        prop.className "avatar"
        prop.src url
    ]

let nav =
    Html.nav [
        prop.children [
            Html.a [
                prop.text "id-ea.li/st"
                prop.style [ style.cursor.pointer ]
                prop.onClick (fun _ -> navigateTo [])
                prop.children [
                  Html.img [
                    prop.src (basePath + "/public/idealist.jpg")
                  ]
                ]
            ]
        ]
    ]
