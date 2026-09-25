module Server.Handlers

// This app's identity handlers now live in the shared identity module (Identity.Handlers, via
// identity.server.props); this file just binds the host seams (Env DB, the guest-write authorizer,
// this site's attribution policy) and re-exposes them under the names Worker.fs wires. This app has no
// bespoke identity routes and no curator surface, so ActivateOnReturn is always false.

open Fable.Core
open Hedge.Workers
open Hedge.Router
open Server.Env

/// OAuth-completion seams (env-free): the site's attribution policy (site-selected AttributionPolicy —
/// justat = articles + blog, ndct = articles-only). No curator page here, so no special return.
let private oauthDeps : Identity.Handlers.OAuthDeps =
    { ReassignStatements = Server.AttributionPolicy.reassignStatements
      CommentTables = Server.AttributionPolicy.commentTables
      ActivateOnReturn = fun _ -> false }

/// Write-handler seams, per request env: the DB, the guest-write authorizer, and the attribution policy.
let private writeDeps (env: Env) : Identity.Handlers.WriteDeps =
    { DB = env.DB
      RequireGuest = Server.GuestConfig.require env
      ReassignStatements = Server.AttributionPolicy.reassignStatements
      CommentTables = Server.AttributionPolicy.commentTables }

/// Framework OAuthConfig hooks (signatures fixed by Hedge.Router.OAuthConfig).
let resolveIdentity = Identity.Handlers.resolveIdentity
let onOAuthComplete : D1Database -> R2Bucket -> string -> obj -> string -> JS.Promise<OAuthComplete> =
    Identity.Handlers.onOAuthComplete oauthDeps

/// Hand-wired /api/auth/* write routes (Worker.fs calls these `request env`).
let activateIdentity (request: WorkerRequest) (env: Env) = Identity.Handlers.activate (writeDeps env) request
let revertIdentity (request: WorkerRequest) (env: Env) = Identity.Handlers.revert (writeDeps env) request
let disconnectIdentity (request: WorkerRequest) (env: Env) = Identity.Handlers.disconnect (writeDeps env) request
let getIdentities (request: WorkerRequest) (env: Env) = Identity.Handlers.getIdentities (writeDeps env) request
