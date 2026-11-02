module Calendar.Services

// The capabilities the calendar module's handlers consume, supplied by the host (its
// Server.ModuleServices adapts the app's Env + identity layer). The module never sees Server.Env,
// a cookie, a secret, or the identity tables: "who is asking" arrives resolved through the shared
// Content.Server.Viewer contract. Kept small — a field exists because a handler uses it.

open Fable.Core
open Hedge.Workers
open Content.Server.Viewer
open Calendar.Vocab
open Calendar.Api

/// What a host derives for a window: entries from somewhere other than the table, and — when the source is an
/// instrument that can be off or not yet fetched — which of the window's days it observed, plus its newest day
/// anywhere (the client lands there). An authored stream reports no observation.
type Derived =
    { Entries: GetEntries.EntryItem list
      Observed: GetEntries.Observed option }

type Services =
    { /// The tenant D1 database.
      DB: D1Database
      /// Resolve the request's viewer. An identity-less host supplies `Content.Server.Viewer.guestOnly`:
      /// everyone reads the public stream and personal entries are simply off.
      Viewer: ViewerResolver
      /// This site's facet vocabulary (Calendar.Vocab.Facets.define) — the same value the host hands
      /// the client, so the editor offers exactly what the server accepts.
      Facets: Facets
      /// Entries DERIVED for a window from somewhere other than the table: a host projects another
      /// service's data into the stream (one entry per flaring period, say), computed per read, never
      /// stored. Read-only by construction (not personal, not Mine, no repeat); the client projects them
      /// like any other entry, and marks the days the source reports it did not observe. Most hosts
      /// pass `noDerived`.
      Derived: GetEntries.Query -> JS.Promise<Derived>
      /// Fresh id generator (framework `newId` by default).
      NewId: unit -> string
      /// Clock (framework `epochNow` by default).
      Now: unit -> int }

/// No derived entries: the stream is the table alone, and every day is as observed as an authored calendar is.
let noDerived : GetEntries.Query -> JS.Promise<Derived> =
    fun _ -> promise { return { Entries = []; Observed = None } }
