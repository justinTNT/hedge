module Server.Handlers

// This app's server handlers. The identity handlers now live in the shared identity module
// (Identity.Handlers, via identity.server.props); this file just binds the host seams (Env DB/Blobs,
// the guest-write authorizer, this site's attribution policy, and the /curator return policy) and
// re-exposes them under the names Worker.fs wires. What remains genuinely app-specific is the bespoke
// darwin.news `getRhymes` route over the composed blog module's tables.

open Fable.Core
open Thoth.Json
open Hedge.Interface
open Hedge.Workers
open Hedge.Router
open Blog.Api
open Server.Env
open Blog.Codecs
open Blog.Db

// ---- Identity handlers: host seams bound to the shared Identity.Handlers ----

/// OAuth-completion seams (env-free): this site composes only blog, so its attribution policy is the
/// blog comment table/statement; a curator returns to the standalone /curator page (auto-activate +
/// document nav), which the blog SPA switcher can't render.
let private oauthDeps : Identity.Handlers.OAuthDeps =
    { ReassignStatements = Server.AttributionPolicy.reassignStatements
      CommentTables = Server.AttributionPolicy.commentTables
      ActivateOnReturn = fun returnTo -> (returnTo.TrimEnd('/')).EndsWith("/curator") }

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

// ---- darwin.news glue (app-specific) ----

let private toFeedItem (r: ItemRow) : GetFeed.FeedItem =
    { Id = r.Id
      Title = r.Title
      Slug = r.Slug
      Image = r.Image
      Extract = r.Extract |> Option.map RichContent
      OwnerComment = RichContent r.OwnerComment
      Timestamp = r.ArticleDate }



/// GET /api/rhymes — every `rhyme-*` tag with the items sharing it, for
/// rhyming.darwin.news. A bespoke darwin.news route (not a reflected endpoint): it
/// reads the composed blog module's tables (via Blog.Sql + the generated
/// Server.Db.Tables) and hand-builds the JSON with the blog FeedItem codec.
let getRhymes (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let! tagRes = env.DB.prepare(Sql.rhymeTags).all()
        let tags = tagRes.results |> Array.map (fun r -> rowStr r "name") |> Array.toList
        let groups = ResizeArray<string * GetFeed.FeedItem list>()
        for tag in tags do
            let! itemsRes = (bind (env.DB.prepare Blog.Sql.itemsByTag) [| box tag; box 12 |]).all()
            let items = itemsRes.results |> Array.map (parseItemRow >> toFeedItem) |> Array.toList
            // A rhyme needs at least a pair; skip empty/singleton tags.
            if List.length items >= 2 then groups.Add(tag, items)
        let body =
            Encode.object [
                "rhymes", Encode.list [
                    for (tag, items) in groups ->
                        Encode.object [
                            "tag", Encode.string tag
                            "items", Encode.list (List.map Encode.blogFeedItem items)
                        ]
                ]
            ] |> Encode.toString 0
        return okJson body
    }
