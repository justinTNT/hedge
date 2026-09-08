module Client.App

open Feliz
open Feliz.Router
open Elmish
open Fable.Core
open Fable.Core.JsInterop
open Models.Api

// -- amplitude.js interop (loaded from the CDN in index.html) --

[<Emit("(function(songs){ if(!window.Amplitude){return;} window.Amplitude.init({ songs: songs, volume: 85 }); requestAnimationFrame(function(){ try{ window.Amplitude.bindNewElements(); }catch(e){} }); })($0)")>]
let private initAmplitude (songs: obj) : unit = jsNative

[<Emit("(function(i){ if(window.Amplitude){ window.Amplitude.playSongAtIndex(i); } })($0)")>]
let private playSong (index: int) : unit = jsNative

[<Emit("(function(){ try{ if(window.Amplitude){ window.Amplitude.pause(); } }catch(e){} })()")>]
let private pauseAmplitude () : unit = jsNative

type Model = {
    Albums: GetAlbums.AlbumItem list
    Route: string list
    Loading: bool
    Error: string option
}

type Msg =
    | GotAlbums of Result<GetAlbums.Response, string>
    | UrlChanged of string list

let private findAlbum (albums: GetAlbums.AlbumItem list) (slug: string) =
    albums |> List.tryFind (fun a -> a.Slug = slug || a.Id = slug)

/// amplitude gets just the open album's tracks (so next/prev stay in the album).
let private songObjects (album: GetAlbums.AlbumItem) : obj =
    album.Tracks
    |> List.map (fun t ->
        createObj [
            "name" ==> t.Title
            "url" ==> t.Url
            "artist" ==> album.Title
            "album" ==> album.Title
            "cover_art_url" ==> (album.Cover |> Option.defaultValue "")
        ])
    |> List.toArray
    |> box

let private initForRoute (model: Model) : Cmd<Msg> =
    match model.Route with
    | [ slug ] ->
        match findAlbum model.Albums slug with
        | Some a -> Cmd.ofEffect (fun _ -> initAmplitude (songObjects a))
        | None -> Cmd.none
    | _ -> Cmd.ofEffect (fun _ -> pauseAmplitude ())

let init () =
    { Albums = []; Route = Router.currentUrl (); Loading = true; Error = None },
    Cmd.OfPromise.either Client.ClientGen.getAlbums () GotAlbums (fun ex -> GotAlbums (Error ex.Message))

let update msg model =
    match msg with
    | GotAlbums (Ok resp) ->
        let m = { model with Albums = resp.Albums; Loading = false; Error = None }
        m, initForRoute m
    | GotAlbums (Error err) -> { model with Loading = false; Error = Some err }, Cmd.none
    | UrlChanged route ->
        let m = { model with Route = route }
        m, initForRoute m

// -- views --

let private coverImg (className: string) (cover: string option) =
    match cover with
    | Some url -> Html.img [ prop.className className; prop.src url ]
    | None -> Html.div [ prop.className (className + " cover-blank") ]

let private homeView (albums: GetAlbums.AlbumItem list) =
    Html.div [
        prop.className "cover-grid"
        prop.children [
            for a in albums ->
                Html.a [
                    prop.key a.Id
                    prop.className "cover-cell"
                    prop.href (Router.format [ a.Slug ])
                    prop.children [
                        coverImg "grid-cover" a.Cover
                        Html.span [ prop.className "grid-title"; prop.text a.Title ]
                    ]
                ]
        ]
    ]

let private playerBar =
    Html.div [
        prop.className "player-bar"
        prop.children [
            Html.div [ prop.className "amplitude-prev pb-btn"; prop.text "⏮" ]
            Html.div [ prop.className "amplitude-play-pause pb-play" ]
            Html.div [ prop.className "amplitude-next pb-btn"; prop.text "⏭" ]
            Html.div [
                prop.className "pb-now"
                prop.children [
                    Html.span [ prop.className "pb-song"; prop.custom ("data-amplitude-song-info", "name") ]
                    Html.span [ prop.className "pb-album"; prop.custom ("data-amplitude-song-info", "album") ]
                ]
            ]
            Html.span [ prop.className "amplitude-current-time pb-time" ]
            Html.input [ prop.type'.range; prop.className "amplitude-song-slider pb-slider"; prop.custom ("step", "any") ]
            Html.span [ prop.className "amplitude-duration-time pb-time" ]
        ]
    ]

let private trackRow (idx: int) (t: GetAlbums.TrackItem) =
    Html.div [
        prop.key t.Id
        prop.className "track"
        prop.custom ("amplitude-song-index", string idx)
        prop.onClick (fun _ -> playSong idx)
        prop.children [
            Html.span [ prop.className "track-num"; prop.text (string t.TrackIndex) ]
            Html.span [ prop.className "track-play"; prop.text "▶" ]
            Html.span [ prop.className "track-title"; prop.text t.Title ]
        ]
    ]

let private albumView (a: GetAlbums.AlbumItem) =
    Html.div [
        prop.className "album-page"
        prop.children [
            Html.a [ prop.className "back"; prop.href (Router.format []); prop.text "← albums" ]
            Html.div [
                prop.className "album-head"
                prop.children [
                    coverImg "album-cover" a.Cover
                    Html.h1 [ prop.className "album-title"; prop.text a.Title ]
                ]
            ]
            Html.div [
                prop.className "track-list"
                prop.children [ for (i, t) in List.indexed a.Tracks -> trackRow i t ]
            ]
            playerBar
        ]
    ]

let private appView (model: Model) =
    Html.div [
        prop.className "app"
        prop.children [
            match model.Error with
            | Some err -> Html.div [ prop.className "error"; prop.text err ]
            | None -> Html.none
            if model.Loading then Html.div [ prop.className "loading"; prop.text "Loading…" ]
            else
                match model.Route with
                | [ slug ] ->
                    match findAlbum model.Albums slug with
                    | Some a -> albumView a
                    | None -> Html.p [ prop.text "Album not found." ]
                | _ -> homeView model.Albums
        ]
    ]

let view model dispatch =
    React.router [
        router.onUrlChanged (UrlChanged >> dispatch)
        router.children [ appView model ]
    ]

open Elmish.React

Program.mkProgram init update view
|> Program.withReactSynchronous "app"
|> Program.run
