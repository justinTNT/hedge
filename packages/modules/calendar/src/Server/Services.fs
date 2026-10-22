module Calendar.Services

// The capabilities the calendar module's handlers consume, supplied by the host (its
// Server.ModuleServices adapts the app's Env + identity layer). The module never sees Server.Env,
// a cookie, a secret, or the identity tables: "who is asking" arrives resolved through the shared
// Content.Server.Viewer contract. Kept small — a field exists because a handler uses it.

open Fable.Core
open Hedge.Workers
open Content.Server.Viewer
open Calendar.Vocab

type Services =
    { /// The tenant D1 database.
      DB: D1Database
      /// Resolve the request's viewer. An identity-less host supplies `Content.Server.Viewer.guestOnly`:
      /// everyone reads the public stream and personal entries are simply off.
      Viewer: ViewerResolver
      /// This site's facet vocabulary (Calendar.Vocab.Facets.define) — the same value the host hands
      /// the client, so the editor offers exactly what the server accepts.
      Facets: Facets
      /// Fresh id generator (framework `newId` by default).
      NewId: unit -> string
      /// Clock (framework `epochNow` by default).
      Now: unit -> int }
