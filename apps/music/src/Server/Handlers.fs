module Server.Handlers

open Fable.Core
open Thoth.Json
open Hedge.Workers
open Hedge.Router
open Codecs
open Models.Api
open Server.Env
open Server.Db

/// The whole library: every album with its tracks nested, albums newest-first,
/// tracks in order. The store is tiny, so one query pair covers it.
let getAlbums (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let! albumRes = (selectAlbums env.DB).all()
        let! trackRes = (selectTracks env.DB).all()

        let albums =
            albumRes.results
            |> Array.map parseAlbumRow
            |> Array.filter (fun a -> a.DeletedAt.IsNone)
            |> Array.sortByDescending (fun a -> a.ReleaseDate)

        let tracksByAlbum =
            trackRes.results
            |> Array.map parseTrackRow
            |> Array.filter (fun t -> t.DeletedAt.IsNone)
            |> Array.groupBy (fun t -> t.AlbumId)
            |> Map.ofArray

        let toTrackItem (t: TrackRow) : GetAlbums.TrackItem =
            { Id = t.Id; Title = t.Title; Url = t.Url; TrackIndex = t.TrackIndex; Plays = t.Plays }

        let toAlbumItem (a: AlbumRow) : GetAlbums.AlbumItem =
            let tracks =
                tracksByAlbum
                |> Map.tryFind a.Id
                |> Option.defaultValue [||]
                |> Array.sortBy (fun t -> t.TrackIndex)
                |> Array.map toTrackItem
                |> Array.toList
            { Id = a.Id; Title = a.Title; Slug = a.Slug; Cover = a.Cover; ReleaseDate = a.ReleaseDate; Tracks = tracks }

        let items = albums |> Array.map toAlbumItem |> Array.toList
        let body =
            Encode.object [ "albums", Encode.list (List.map Encode.albumItem items) ]
            |> Encode.toString 0
        return okJson body
    }
