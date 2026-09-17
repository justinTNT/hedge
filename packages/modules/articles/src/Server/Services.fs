module Articles.Services

// C3 — the capabilities the articles module's handlers consume, supplied by the host (replaces
// the module's former `open Server.Env` + `module Identity = Server.Identity`). Smaller than
// blog's: articles has no image rehost (no R2) and no admin-gated authoring (no admin key) — a
// field exists only because a handler uses it.

open Hedge.Workers
open Content.Server.Author

type Services =
    { /// The tenant D1 database (env.DB).
      DB: D1Database
      /// Durable Object namespace for live comment broadcast (env.EVENTS).
      Events: DurableObjectNamespace
      /// Resolves a new comment's author from the guest identity (was Server.Identity inline).
      Author: AuthorResolver
      /// Resolves + authorizes the guest for a comment WRITE via the shared signed-cookie policy.
      /// The module calls `Guest.Require request`; it never reads the cookie, a secret, or the key.
      Guest: Hedge.GuestSession.Service
      /// Fresh id generator (framework `newId` by default; a test can supply a deterministic one).
      NewId: unit -> string
      /// Clock (framework `epochNow` by default; a test can supply a deterministic one).
      Now: unit -> int }
