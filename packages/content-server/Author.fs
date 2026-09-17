module Content.Server.Author

// C3 — the shared, app-independent author-resolution contract. The server analogue of
// packages/content-client: a thin contract a content module's handlers depend on instead of
// the app's Server.Identity / Server.Env. The app adapts its own identity implementation to
// this; a module resolves a comment's author through it and never sees the app's IdentityRow,
// D1 schema or environment. Compiled once at the app-Server level (like content-client at the
// app-Client level), so composing several content modules doesn't double-compile it.

open Fable.Core

/// What a content module stamps onto a new comment, resolved by the host from the request's
/// guest identity.
type ResolvedAuthor =
    { /// The identity id to store on the comment: the guest's active (claimed) identity if one
      /// exists, otherwise the freshly-ensured anonymous identity.
      IdentityId: string
      /// The active identity's avatar URL, or "" for the anonymous fallback.
      Picture: string }

/// The inputs a module hands the host to resolve a comment's author. Mirrors what the module
/// already computes inline today: the guest id (from the accepted Hedge.GuestSession credential),
/// a fresh id to create the anonymous identity with if the guest has none yet, the display name
/// for that anonymous identity, and the epoch to stamp the ensure/create with.
type AuthorRequest =
    { GuestId: string
      FallbackIdentityId: string
      AuthorName: string
      Now: int }

/// Host-provided author resolution: ensure the guest + anonymous identity exist, then return
/// the identity a new comment should be attributed to — the active one if claimed, else the
/// anonymous fallback. The app builds this by adapting its Server.Identity (ensure statements +
/// activeFor); a module calls `ResolveAuthor` and stays ignorant of the app's identity storage.
/// A single-field record (not a bare function alias) so a module's Services can hold it under an
/// obvious name and a test can supply a deterministic fake.
type AuthorResolver =
    { ResolveAuthor: AuthorRequest -> JS.Promise<ResolvedAuthor> }
