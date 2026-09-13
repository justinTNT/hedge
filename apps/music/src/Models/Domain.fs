module Models.Domain

open Hedge.Interface

/// A release — an album (homebrew) or a mixtape set. One taxonomy for both sites.
[<Table "albums">]
type Album = {
    Id: PrimaryKey<string>
    Title: string
    /// URL slug (the Hasura `tag`).
    Slug: string
    /// Cover image (derived from the dphon.es S3 path).
    Cover: Link option
    /// Release date, unix seconds — drives sort order. Editable in the admin as a
    /// date picker (EditableDate).
    ReleaseDate: EditableDate
    CreatedAt: CreateTimestamp
    UpdatedAt: UpdateTimestamp option
    DeletedAt: SoftDelete option
}

[<Table "tracks">]
type Track = {
    Id: PrimaryKey<string>
    AlbumId: ForeignKey<Album>
    Title: string
    /// The mp3 (a direct dphon.es S3 URL).
    Url: Link
    /// Order within the album.
    TrackIndex: int
    Plays: int
    CreatedAt: CreateTimestamp
    DeletedAt: SoftDelete option
}
