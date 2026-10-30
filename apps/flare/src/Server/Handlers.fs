module Server.Handlers

// This host's identity handlers live in the shared identity module (Identity.Handlers, via
// identity.server.props); this file binds the host seams (Env DB, the guest-write authorizer, this
// site's attribution policy) and re-exposes them under the names Worker.fs wires. Content handlers
// belong to the composed calendar module (bound in Server.ModuleServices).

open Fable.Core
open Hedge.Workers
open Hedge.Router
open Server.Env

/// OAuth-completion seams (env-free): this app's attribution policy; no curator page, so no special
/// return route.
let private oauthDeps : Identity.Handlers.OAuthDeps =
    { ReassignStatements = Server.AttributionPolicy.reassignStatements
      CommentTables = Server.AttributionPolicy.commentTables
      ActivateOnReturn = fun _ -> false }

/// Write-handler seams, per request env. Server.ModuleServices.identityHttp builds the IdentityHttp
/// dispatch over these.
let writeDeps (env: Env) : Identity.Handlers.WriteDeps =
    { DB = env.DB
      RequireGuest = Server.GuestConfig.require env
      ReassignStatements = Server.AttributionPolicy.reassignStatements
      CommentTables = Server.AttributionPolicy.commentTables }

/// Framework OAuthConfig hooks (signatures fixed by Hedge.Router.OAuthConfig).
let resolveIdentity = Identity.Handlers.resolveIdentity
let onOAuthComplete : D1Database -> R2Bucket -> string -> obj -> string -> JS.Promise<OAuthComplete> =
    Identity.Handlers.onOAuthComplete oauthDeps
