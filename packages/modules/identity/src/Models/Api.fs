namespace IdentityHttp

// Typed HTTP contracts for the identity lifecycle endpoints (/api/auth/*), generated into a module
// surface (Codecs/ClientGen/RouteContract) like a content module — but with NO Domain records, so it
// contributes zero tables (empty Db/AdminGen). Lives in the existing IdentityModels assembly under its own
// `IdentityHttp` namespace so it doesn't collide with the shared Models.Domain schema slice. Lowercase
// field names are deliberate: they are the existing wire keys (identityId/merge/ok/identities/…), so the
// generated codecs reproduce the current JSON exactly. Route prefix /api/auth comes from the manifest.

open Hedge.Interface

module Api =

    /// Public identity projection (no guest ownership, provider account id, or internal lifecycle fields).
    type IdentityListItem =
        { id: string
          provider: string
          name: string
          picture: string
          email: string option
          activatedAt: int option }

    module Activate =
        type Request = { identityId: string; merge: bool }
        type Response = { ok: bool }
        let endpoint : Post<Request, Response> = Post "/api/activate"
        let requestContext = true

    module Revert =
        type Request = { identityId: string; merge: bool }
        type Response = { ok: bool }
        let endpoint : Post<Request, Response> = Post "/api/revert"
        let requestContext = true

    module Disconnect =
        type Request = { identityId: string; name: string option }
        type Response = { ok: bool }
        let endpoint : Post<Request, Response> = Post "/api/disconnect"
        let requestContext = true

    module GetIdentities =
        type Response = { identities: IdentityListItem list }
        let endpoint : Get<Response> = Get "/api/identities"
        let requestContext = true
