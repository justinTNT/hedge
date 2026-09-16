module Articles.Client.App

// The shared guest-session accessor is host-provided (packages/hedge/src/Client);
// alias it locally, the same way Client.RichText is aliased.
module GuestSession = Client.GuestSession

open Feliz
open Elmish
open Articles.Client.Types
open Articles.Client.Pages

// ============================================================
// Hosted surface (CP-B: the ONLY surface) — see notes/UNIFIED-SHELL.md
//
// The articles module is a pure content module: it exposes hosting interfaces that a host
// (the Justat shell, or the ndct single-module host) drives with an immutable per-instance
// HostContext. The HOST owns the router, the identity authority (Content.Identity +
// Content.IdentityView), and the chrome; this module owns only its content — feed / post +
// comments. These functions do no browser-location reads, no identity boot, and (for
// emptyHosted) no effects at all.
// ============================================================

/// An idle content model — no fetch, no location read, no listeners. The host seeds the
/// session snapshot; a first `enterHosted` drives the initial route.
let emptyHosted (session: GuestSession.GuestSessionData) : Model =
    { Route = []
      Feed = None
      FeedLoadingMore = false
      CurrentItem = None
      IsLoading = false
      Error = None
      GuestSession = session
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
/// identity reattribution (merge/revert/disconnect), where a cached feed/post carries
/// now-obsolete authorship — re-entry must not silently reuse it. Bumps LoadGen so a read already
/// in flight when the cache is invalidated is dropped on arrival (CP-A finding 3).
let invalidateContent (model: Model) : Model =
    { model with Feed = None; CurrentItem = None; LoadGen = model.LoadGen + 1 }

/// CP-A finding 1: invalidate the OUTGOING module's in-flight reads on a host switch. Bumps
/// LoadGen (so a late read completing after we've left is dropped — no title write, no socket
/// reopen) and clears the loading flags so nothing lingers. Content is left intact (a switch,
/// unlike invalidateContent, keeps the cached view for a cheap return).
let invalidateInFlight (model: Model) : Model =
    { model with LoadGen = model.LoadGen + 1; IsLoading = false; FeedLoadingMore = false }

/// Synchronously dispose this instance's live resources — WebSocket + comment editor.
/// Idempotent (each teardown self-guards). A plain function so a host can dispose the
/// OUTGOING module in order, before entering the incoming one.
let disposeHosted () : unit =
    Item.disconnectEvents ()
    Item.destroyCommentEditor ()

let disposeHostedCmd : Cmd<Msg> =
    Cmd.ofEffect (fun _ -> disposeHosted ())

/// Enter a module-local content route. Returns the route's content model + load
/// commands, preceded by disposal of the outgoing route's resources. Reads no browser
/// location and touches no identity; identity-claim routes are the host's concern (the
/// host resolves them before ever calling this), so they fall through as an empty view.
let enterHosted (ctx: Content.HostContext) (route: string list) (model: Model) : Model * Cmd<Msg> =
    let cleared =
        { model with
            Route = route
            CurrentItem = None
            ReplyingTo = None
            CommentDraft = ""
            CollapsedComments = Set.empty
            // CP-A: entry is a new read generation and a new draft revision — any read/submit
            // still in flight from before we entered is dropped on arrival, and a late comment
            // success can't clear the fresh draft. The load-issuing branch below re-arms via
            // its own gen (LoadItem/LoadFeed compute LoadGen+1).
            LoadGen = model.LoadGen + 1
            DraftRev = model.DraftRev + 1
            // In-flight requests from the route we're leaving are invalidated (their results
            // are stale-dropped by generation), so their loading flags must not linger — else a
            // reused cached feed sits behind a spinner, or FeedLoadingMore=true wedges
            // pagination. The load-issuing branch below re-arms IsLoading via its message. #2.
            IsLoading = false
            FeedLoadingMore = false }
    let resetTitle = Cmd.ofEffect (fun _ -> ctx.SetDocTitle "")
    match route with
    // Feed is retained across a module switch (not in `cleared`); reuse it (loaded pages +
    // cursor) rather than refetching page 1, which would discard the appended pages. #4.
    | [] -> cleared, Cmd.batch [ disposeHostedCmd; resetTitle; (if cleared.Feed.IsSome then Cmd.none else Cmd.ofMsg LoadFeed) ]
    | [idOrSlug] -> cleared, Cmd.batch [ disposeHostedCmd; Cmd.ofMsg (LoadItem idOrSlug) ]
    | _ -> cleared, Cmd.batch [ disposeHostedCmd; resetTitle ]

/// Handle a CONTENT message. Browser routing and identity are host-owned (the host owns the
/// router + Content.Identity), so no routing/identity messages reach here.
let updateHosted (deps: Deps) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | LoadFeed | GotFeed _ | LoadMoreFeed | GotMoreFeed _ ->
        Feed.update deps msg model
    | LoadItem _ | GotItem _ | SubmitComment | GotSubmitComment _ | ToggleCollapse _ | SetReplyTo _ | SetCommentDraft _ | CancelReply
    | ConnectEvents _ | DisconnectEvents | GotEvent _ | EventError _ ->
        Item.update deps msg model
    | DismissError ->
        { model with Error = None }, Cmd.none

/// Render module content only — no router, header, identity switcher, sidebar, or outer
/// <main>. The host supplies the frame and places this inside its own <main>.
let contentView (ctx: Content.HostContext) (model: Model) dispatch =
    React.fragment [
        match model.Error with
        | Some err -> Shared.error (Hedge.Http.renderError err) dispatch
        | None -> Html.none

        if model.IsLoading then
            Shared.loading
        else
            match model.Route with
            | [_] ->
                match model.CurrentItem with
                | Some response -> Item.view response model dispatch
                | None -> Html.p [ prop.text "Post not found." ]
            | _ ->
                match model.Feed with
                | Some response -> Feed.view ctx response
                | None -> Html.p [ prop.text "No posts yet." ]
    ]
