module Alerts.Api

// The alerts module's curator API — the dedicated curation surface (NOT the owner admin). Paths are
// module-relative ("/api/curation/..."); Gen applies the module route prefix -> "/api/alerts/...".
// All endpoints are POST so each handler receives the WorkerRequest for cookie-based curator
// authorization (GET handlers don't get the request). Authorization + state gating live in the
// server handlers; these are just the wire shapes.

open Hedge.Interface

/// The undecided curation queue (approved=0 AND rejected=0), oldest-first. POST so the handler gets
/// the request for auth; Cursor is reserved for future pagination.
module Queue =
    type Request = { Cursor: string option }
    type Item =
        { Id: string
          Title: string
          Link: string
          Snippet: string
          OwnerComment: RichContent
          PublishedAt: int
          Topic: string }
    type Response = { Items: Item list }
    let endpoint : Post<Request, Response> = Post "/api/curation/queue"

/// Approve an undecided post for publication (the hourly cron publishes it later).
module Approve =
    type Request = { Id: string }
    type Response = { Ok: bool }
    let endpoint : Post<Request, Response> = Post "/api/curation/approve"

/// Dismiss (reject) an undecided post — a tombstone; it is never published and never re-imported.
module Dismiss =
    type Request = { Id: string }
    type Response = { Ok: bool }
    let endpoint : Post<Request, Response> = Post "/api/curation/dismiss"

/// Edit the framing (title / snippet / owner comment) of an undecided post before approving. Frozen
/// once approved (the cron publishes from a snapshot).
module EditFraming =
    type Request =
        { Id: string
          Title: string
          Snippet: string
          OwnerComment: string }   // TipTap-doc JSON
    type Response = { Ok: bool }
    let endpoint : Post<Request, Response> = Post "/api/curation/framing"
