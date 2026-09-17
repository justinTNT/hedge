module Blog.Services

// C3 — the capabilities the blog module's handlers consume, supplied by the host. This replaces
// the module's former `open Server.Env` + `module Identity = Server.Identity`: a handler now
// closes over a Services (via Blog.Composition.bind) rather than importing the app's Env or
// identity storage. The app builds this by adapting its environment (see Server.ModuleServices),
// which is the one place that knows both the app's Env and the blog module. Kept small — a field
// exists because a handler uses it.

open Hedge.Workers
open Content.Server.Author

type Services =
    { /// The tenant D1 database (env.DB).
      DB: D1Database
      /// R2 bucket for rehosting a submitted item's lead image (env.BLOBS).
      Blobs: R2Bucket
      /// Durable Object namespace for live comment broadcast (env.EVENTS).
      Events: DurableObjectNamespace
      /// The admin key the item-authoring gate checks against the request header (env.ADMIN_KEY).
      AdminKey: string
      /// Resolves a new comment's author from the guest identity (was Server.Identity inline).
      Author: AuthorResolver
      /// Resolves + authorizes the guest for a comment WRITE via the shared signed-cookie policy.
      /// The module calls `Guest.Require request`; it never reads the cookie, a secret, or the key.
      Guest: Hedge.GuestSession.Service
      /// Fresh id generator (framework `newId` by default; a test can supply a deterministic one).
      NewId: unit -> string
      /// Clock (framework `epochNow` by default; a test can supply a deterministic one).
      Now: unit -> int
      /// C4: whether source-snapshot capture is enabled for this host (microblog: true;
      /// Justat: false → POST /api/blog/snapshot returns 404 and writes nothing).
      CaptureEnabled: bool }
