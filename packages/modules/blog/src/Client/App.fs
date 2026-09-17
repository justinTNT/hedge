module Blog.Client.App

// The shared guest-session accessor is host-provided (packages/hedge/src/Client);
// alias it locally, the same way Client.RichText is aliased.
module GuestSession = Client.GuestSession

open Feliz
open Elmish
open Blog.Client.Types
open Blog.Client.Pages

// ============================================================
// Hosted surface (CP-B: the ONLY surface) — see notes/UNIFIED-SHELL.md
//
// The blog module is a pure content module: it exposes hosting interfaces that a host
// (the Justat shell, or the darwin.news single-module host) drives with an immutable
// per-instance HostContext. The HOST owns the router, the identity authority
// (Content.Identity + Content.IdentityView), and the chrome; this module owns only its
// content — feed / item / tag / new-item + comments. These functions do no browser-location
// reads, no identity boot, and (for emptyHosted) no effects at all.
// ============================================================

/// An idle content model — no fetch, no location read, no listeners. The host seeds the
/// session snapshot; a first `enterHosted` drives the initial route.
let emptyHosted (session: GuestSession.GuestSessionData) : Model =
    { Route = []
      Feed = None
      FeedLoadingMore = false
      CurrentItem = None
      TagItems = None
      TagLoadingMore = false
      IsLoading = false
      Error = None
      GuestSession = session
      ItemForm = emptyItemForm
      CollapsedComments = Set.empty
      ReplyingTo = None
      CommentDraft = ""
      LoadGen = 0
      DraftRev = 0 }

/// Pure snapshot of the host's authoritative session into the child model (the comment
/// forms read it). Identity is host-owned; this never triggers a sync/switcher flow.
let withSession (session: GuestSession.GuestSessionData) (model: Model) : Model =
    { model with GuestSession = session }

/// C1: drop cached content so the next enterHosted refetches. A host calls this after an
/// identity reattribution (merge/revert/disconnect), where a cached feed/item/tag carries
/// now-obsolete authorship — re-entry must not silently reuse it. Bumps LoadGen so a read already
/// in flight against the pre-invalidation content can't repopulate the cache (CP-A finding 3).
let invalidateContent (model: Model) : Model =
    { model with Feed = None; CurrentItem = None; TagItems = None; LoadGen = model.LoadGen + 1 }

/// CP-A: invalidate this module's in-flight reads when the HOST leaves it — a stale read arriving
/// after we've left must not reopen a socket or set the tab title (finding 1). Bumps LoadGen and
/// settles the loading flags; retained caches (Feed etc.) are untouched so a later re-entry can
/// still reuse them. The host calls this on the OUTGOING module during navigation.
let invalidateInFlight (model: Model) : Model =
    { model with LoadGen = model.LoadGen + 1; IsLoading = false; FeedLoadingMore = false; TagLoadingMore = false }

/// Synchronously dispose this instance's live resources — WebSocket + comment/owner
/// editors. Idempotent (each teardown self-guards). A plain function so a host can
/// dispose the OUTGOING module in order, before entering the incoming one.
let disposeHosted () : unit =
    Item.disconnectEvents ()
    Item.destroyCommentEditor ()
    NewItem.destroyOwnerCommentEditor ()

let disposeHostedCmd : Cmd<Msg> =
    Cmd.ofEffect (fun _ -> disposeHosted ())

/// Enter a module-local content route. Returns the route's content model + load
/// commands, preceded by disposal of the outgoing route's resources. Reads no browser
/// location and touches no identity; identity-claim routes are the host's concern (the
/// host resolves them before ever calling this), so they fall through as an empty view.
let enterHosted (ctx: Content.HostContext) (route: string list) (model: Model) : Model * Cmd<Msg> =
    // Reset the tab title on entering blog: blog views don't set their own, so without this a
    // prior article's title would linger in the tab (mirrors articles' enterHosted). #6.
    let resetTitle = Cmd.ofEffect (fun _ -> ctx.SetDocTitle "")
    let cleared =
        { model with
            Route = route
            CurrentItem = None
            TagItems = None
            ReplyingTo = None
            CommentDraft = ""
            // CP-A: entry supersedes any read/draft issued before it.
            LoadGen = model.LoadGen + 1
            DraftRev = model.DraftRev + 1
            CollapsedComments = Set.empty
            // Any in-flight request from the route we're leaving is invalidated (its result
            // is stale-dropped by generation), so its loading flag must not linger — else a
            // reused cached feed sits behind a spinner, or FeedLoadingMore=true wedges
            // pagination. The load-issuing branches below re-arm IsLoading via their message. #2.
            IsLoading = false
            FeedLoadingMore = false
            TagLoadingMore = false }
    match route with
    // Feed is retained across a module switch (not in `cleared`); reuse it (with its loaded
    // pages + cursor) instead of refetching page 1, which would discard appended pages. #4.
    | [] -> cleared, Cmd.batch [ disposeHostedCmd; resetTitle; (if cleared.Feed.IsSome then Cmd.none else Cmd.ofMsg LoadFeed) ]
    | ["tag"; name] -> cleared, Cmd.batch [ disposeHostedCmd; resetTitle; Cmd.ofMsg (LoadTagItems name) ]
    // "new" issues no load; the cleared spinner (above) keeps a prior route's spinner from masking the form.
    | ["new"] -> cleared, Cmd.batch [ disposeHostedCmd; resetTitle; NewItem.initOwnerCommentEditorCmd ]
    | [idOrSlug] -> cleared, Cmd.batch [ disposeHostedCmd; resetTitle; Cmd.ofMsg (LoadItem idOrSlug) ]
    | _ -> cleared, Cmd.batch [ disposeHostedCmd; resetTitle ]

/// Handle a CONTENT message. Browser routing and identity are host-owned (the host owns the
/// router + Content.Identity), so no routing/identity messages reach here. `deps` carries the
/// host context + the host-injected typed API client (CP-C), threaded into the page updates.
let updateHosted (deps: Deps) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | LoadFeed | GotFeed _ | LoadMoreFeed | GotMoreFeed _ ->
        Feed.update deps msg model
    | LoadItem _ | GotItem _ | ConnectEvents _ | DisconnectEvents | GotEvent _ | EventError _
    | SubmitComment | GotSubmitComment _ | ToggleCollapse _ | SetReplyTo _ | SetCommentDraft _ | CancelReply ->
        Item.update deps msg model
    | SetNewItemTitle _ | SetNewItemLink _ | SetNewItemTags _ | SubmitItem | GotSubmitItem _ ->
        NewItem.update deps msg model
    | LoadTagItems _ | GotTagItems _ | LoadMoreTagItems | GotMoreTagItems _ ->
        TagItems.update deps msg model
    | DismissError ->
        { model with Error = None }, Cmd.none

/// Render module content only — no router, header, identity switcher, sidebar, or outer
/// <main>. The host supplies the frame and places this inside its own <main>.
let contentView (ctx: Content.HostContext) (model: Model) dispatch =
    // Module root: scopes this module's component CSS (.blog-content …) so it never
    // collides with Articles' identically-named classes in the unified shell.
    Html.div [
        prop.className "blog-content"
        prop.children [
            match model.Error with
            | Some err -> Shared.error (Hedge.Http.renderError err) dispatch
            | None -> Html.none

            if model.IsLoading then
                Shared.loading
            else
                match model.Route with
                | ["tag"; _] ->
                    match model.TagItems with
                    | Some response -> TagItems.view ctx response
                    | None -> Html.p [ prop.text "No items for this tag." ]
                | ["new"] ->
                    NewItem.view model.ItemForm dispatch
                | [_] ->
                    match model.CurrentItem with
                    | Some response -> Item.view ctx response model dispatch
                    | None -> Html.p [ prop.text "Item not found." ]
                | _ ->
                    match model.Feed with
                    | Some response -> Feed.view ctx response
                    | None -> Html.p [ prop.text "No items yet." ]
        ]
    ]
