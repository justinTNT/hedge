module Models.Api

open Hedge.Interface

/// The whole library in one fetch — the store is small, and the player wants every
/// track available for instant playback.
module GetAlbums =
    type TrackItem = {
        Id: string
        Title: string
        Url: string
        TrackIndex: int
        Plays: int
    }

    type AlbumItem = {
        Id: string
        Title: string
        Slug: string
        Cover: string option
        ReleaseDate: int
        Tracks: TrackItem list
    }

    type Response = {
        Albums: AlbumItem list
    }

    let endpoint : Get<Response> = Get "/api/albums"
