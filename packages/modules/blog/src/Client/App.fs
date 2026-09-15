module Blog.Client.App

// The shared guest-session accessor is host-provided (packages/hedge/src/Client);
// alias it locally, the same way Client.RichText is aliased.
module GuestSession = Client.GuestSession

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Feliz.Router
open Elmish
open Blog.Client.Types
open Blog.Client.Pages

// The standalone entry (Blog/Main.fs) runs this component with routing/navigation read
// from window globals — the default host context. Ctx-taking page helpers get this one;
// a shell supplies its own context per hosted instance (unified shell, Stage 0).
let private standaloneCtx = Content.HostContext.standalone Shared.navigateTo

[<Emit("new URLSearchParams(window.location.search).get($0)")>]
let private getQueryParam (name: string) : string = jsNative

/// OAuth return: /auth/claim?identity=...&returnTo=...
let private parseClaimFromRoute () : (string option * string) =
    let identity = getQueryParam "identity"
    let returnTo = getQueryParam "returnTo"
    let identity = if isNull identity || identity = "" then None else Some identity
    let returnTo = if isNull returnTo || returnTo = "" then "/" else returnTo
    identity, returnTo

let private revertIdentityCmd (identityId: string) (merge: bool) : Cmd<Msg> =
    let body = sprintf """{"identityId":"%s","merge":%s}""" identityId (if merge then "true" else "false")
    Cmd.OfPromise.either
        (fun () -> Client.Api.postJsonRaw "/api/auth/revert" body)
        ()
        GotRevertIdentity
        (fun ex -> GotRevertIdentity (Error ex.Message))

let private disconnectIdentityCmd (identityId: string) (fallbackName: string) : Cmd<Msg> =
    let body = sprintf """{"identityId":"%s","name":"%s"}""" identityId fallbackName
    Cmd.OfPromise.either
        (fun () -> Client.Api.postJsonRaw "/api/auth/disconnect" body)
        ()
        GotDisconnect
        (fun ex -> GotDisconnect (Error ex.Message))

let private loadProvidersCmd : Cmd<Msg> =
    Cmd.OfPromise.perform
        (fun () ->
            promise {
                let! data = Client.Api.fetchJsonRaw "/api/auth/providers"
                let arr : string array = data?providers |> unbox
                return List.ofArray arr
            })
        ()
        GotProviders

let private loadIdentitiesCmd : Cmd<Msg> =
    Cmd.OfPromise.perform
        (fun () ->
            promise {
                let! data = Client.Api.fetchJsonRaw "/api/auth/identities"
                let arr : obj array = data?identities |> unbox
                return arr |> Array.map (fun o ->
                    { Id = o?id |> unbox<string>
                      Provider = o?provider |> unbox<string>
                      Name = o?name |> unbox<string>
                      Picture = o?picture |> unbox<string>
                      ActivatedAt = let v = o?activatedAt in if isNull v then None else Some (unbox<int> v) }
                ) |> Array.toList
            })
        ()
        GotIdentities

let init () : Model * Cmd<Msg> =
    let route = Router.currentUrl ()
    let claimFocus, claimReturnTo =
        match route with
        | ["auth"; "claim"] | ["auth"; "claim"; _] -> parseClaimFromRoute ()
        | _ -> None, "/"
    let model =
        { Route = route
          Feed = None
          FeedLoadingMore = false
          CurrentItem = None
          TagItems = None
          TagLoadingMore = false
          IsLoading = false
          Error = None
          GuestSession = GuestSession.getSession ()
          ItemForm = emptyItemForm
          CollapsedComments = Set.empty
          ReplyingTo = None
          Identities = []
          AvailableProviders = []
          ShowIdentitySwitcher = false
          SelectedIdentity = None
          PendingClaimFocus = claimFocus }
    let routeCmd =
        match route with
        | ["auth"; "claim"] | ["auth"; "claim"; _] ->
            // Land on the page the user came from; UrlChanged consumes
            // PendingClaimFocus to open the switcher pre-selected
            Cmd.batch [
                loadIdentitiesCmd
                Cmd.ofEffect (fun _ -> Shared.navigateToPath claimReturnTo)
            ]
        | ["tag"; name] -> Cmd.ofMsg (LoadTagItems name)
        | ["new"] -> Cmd.batch [ Cmd.ofMsg LoadFeed; NewItem.initOwnerCommentEditorCmd ]
        | [idOrSlug] -> Cmd.ofMsg (LoadItem idOrSlug)
        | _ -> Cmd.ofMsg LoadFeed
    let syncCmd =
        Cmd.OfPromise.perform GuestSession.syncSession () GotSessionSync
    model, Cmd.batch [ routeCmd; syncCmd; loadProvidersCmd ]

let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | UrlChanged route ->
        let cleanupCmd = Cmd.batch [
            Item.disconnectEventsCmd ()
            Item.destroyCommentEditorCmd
            NewItem.destroyOwnerCommentEditorCmd
            Item.destroyAllViewersCmd
        ]
        match route with
        | ["auth"; "claim"] | ["auth"; "claim"; _] ->
            // The OAuth return lands on a path URL, which only the router
            // sees — init reads the hash — so the claim is parsed here. The
            // UrlChanged from the redirect below consumes PendingClaimFocus.
            let claimFocus, claimReturnTo = parseClaimFromRoute ()
            let updated =
                { model with
                    Route = route
                    CurrentItem = None
                    TagItems = None
                    ReplyingTo = None
                    CollapsedComments = Set.empty
                    ShowIdentitySwitcher = false
                    SelectedIdentity = None
                    PendingClaimFocus = claimFocus }
            updated,
            Cmd.batch [
                cleanupCmd
                loadIdentitiesCmd
                Cmd.ofEffect (fun _ -> Shared.navigateToPath claimReturnTo)
            ]
        | _ ->
        // An OAuth return sets PendingClaimFocus; consume it here so the
        // switcher opens pre-selected on the page we navigate back to
        let showSwitcher, selected =
            match model.PendingClaimFocus with
            | Some id -> true, Some id
            | None -> false, None
        let cmd =
            match route with
            | [] -> Cmd.batch [ cleanupCmd; Cmd.ofMsg LoadFeed ]
            | ["tag"; name] -> Cmd.batch [ cleanupCmd; Cmd.ofMsg (LoadTagItems name) ]
            | ["new"] -> Cmd.batch [ cleanupCmd; NewItem.initOwnerCommentEditorCmd ]
            | [idOrSlug] -> Cmd.batch [ cleanupCmd; Cmd.ofMsg (LoadItem idOrSlug) ]
            | _ -> cleanupCmd
        { model with Route = route; CurrentItem = None; TagItems = None; ReplyingTo = None; CollapsedComments = Set.empty; ShowIdentitySwitcher = showSwitcher; SelectedIdentity = selected; PendingClaimFocus = None }, cmd

    | DismissError ->
        { model with Error = None }, Cmd.none

    | LoadFeed | GotFeed _ | LoadMoreFeed | GotMoreFeed _ ->
        Feed.update msg model

    | LoadItem _ | GotItem _ | ConnectEvents _ | DisconnectEvents | GotEvent _ | EventError _
    | SubmitComment | GotSubmitComment _ | ToggleCollapse _ | SetReplyTo _ | CancelReply ->
        Item.update msg model

    | SetNewItemTitle _ | SetNewItemLink _ | SetNewItemTags _ | SubmitItem | GotSubmitItem _ ->
        NewItem.update msg model

    | LoadTagItems _ | GotTagItems _ | LoadMoreTagItems | GotMoreTagItems _ ->
        TagItems.update msg model

    | GotSessionSync session ->
        let synced = { model with GuestSession = session }
        // Consume a one-use claim handoff from the unified shell (Stage 1): after an OAuth
        // return the shell hands the claimed identity to this blog bundle so the switcher
        // opens pre-selected. No-op when there's no handoff (e.g. microblog, or a normal
        // sync).
        match Content.ClaimHandoff.tryTake session.GuestId with
        | Some identityId ->
            { synced with ShowIdentitySwitcher = true; SelectedIdentity = Some identityId }, loadIdentitiesCmd
        | None ->
            synced, Cmd.none

    | RevertIdentity (identityId, merge) ->
        // Deliberately not setting IsLoading: that swaps the whole view for a
        // spinner, and a switch is a background request on a page we want to
        // keep showing.
        model, revertIdentityCmd identityId merge

    | GotRevertIdentity (Ok _) ->
        // A merge rewrites comment authorship server-side, so refetch whatever
        // the current route is displaying rather than trusting the local copy.
        let reloadCmd =
            match model.Route with
            | ["tag"; name] -> Cmd.ofMsg (LoadTagItems name)
            | ["new"] -> Cmd.none
            | [idOrSlug] -> Cmd.ofMsg (LoadItem idOrSlug)
            | _ -> Cmd.ofMsg LoadFeed
        { model with IsLoading = false; ShowIdentitySwitcher = false; SelectedIdentity = None },
        Cmd.batch [
            Cmd.OfPromise.perform GuestSession.syncSession () GotSessionSync
            loadIdentitiesCmd
            reloadCmd
        ]

    | GotRevertIdentity (Error err) ->
        { model with IsLoading = false; Error = Some err }, Cmd.none

    | DisconnectIdentity identityId ->
        model, disconnectIdentityCmd identityId model.GuestSession.DisplayName

    | GotDisconnect (Ok _) ->
        // Abandoning doesn't re-attribute anything, but the active identity may
        // have changed, so refresh the page's authorship along with the session.
        let reloadCmd =
            match model.Route with
            | ["tag"; name] -> Cmd.ofMsg (LoadTagItems name)
            | ["new"] -> Cmd.none
            | [idOrSlug] -> Cmd.ofMsg (LoadItem idOrSlug)
            | _ -> Cmd.ofMsg LoadFeed
        { model with ShowIdentitySwitcher = false; SelectedIdentity = None },
        Cmd.batch [
            Cmd.OfPromise.perform GuestSession.syncSession () GotSessionSync
            loadIdentitiesCmd
            reloadCmd
        ]

    | GotDisconnect (Error err) ->
        { model with Error = Some err }, Cmd.none

    | LoadIdentities ->
        model, loadIdentitiesCmd

    | GotProviders providers ->
        { model with AvailableProviders = providers }, Cmd.none

    | GotIdentities identities ->
        { model with Identities = identities }, Cmd.none

    | ToggleIdentitySwitcher ->
        let show = not model.ShowIdentitySwitcher
        { model with ShowIdentitySwitcher = show; SelectedIdentity = None },
        if show then loadIdentitiesCmd else Cmd.none

    | SelectIdentity identityId ->
        let selected = if model.SelectedIdentity = Some identityId then None else Some identityId
        { model with SelectedIdentity = selected }, Cmd.none

let appView (model: Model) dispatch =
    Html.div [
        prop.className "app"
        prop.children [
            Html.header [ Shared.navWithSession model dispatch ]
            Html.main [
                match model.Error with
                | Some err -> Shared.error err dispatch
                | None -> Html.none

                if model.IsLoading then
                    Shared.loading
                else
                    match model.Route with
                    | ["auth"; "claim"] | ["auth"; "claim"; _] ->
                        // Transient: init immediately navigates to returnTo
                        Shared.loading
                    | ["tag"; _] ->
                        match model.TagItems with
                        | Some response -> TagItems.view standaloneCtx response
                        | None -> Html.p [ prop.text "No items for this tag." ]
                    | ["new"] ->
                        NewItem.view model.ItemForm dispatch
                    | [_] ->
                        match model.CurrentItem with
                        | Some response -> Item.view standaloneCtx response model dispatch
                        | None -> Html.p [ prop.text "Item not found." ]
                    | _ ->
                        match model.Feed with
                        | Some response -> Feed.view standaloneCtx response
                        | None -> Html.p [ prop.text "No items yet." ]
            ]
        ]
    ]

let view model dispatch =
    React.router [
        router.pathMode
        router.onUrlChanged (Shared.routeOf >> UrlChanged >> dispatch)
        router.children [ appView model dispatch ]
    ]

// ============================================================
// Hosted surface (unified shell, Stage 0) — see notes/UNIFIED-SHELL.md
//
// Compatible hosting interfaces alongside the standalone init/update/view above. A
// shell drives these with an immutable per-instance HostContext; it owns the router,
// identity, and chrome. The standalone entry keeps owning those. These functions do no
// browser-location reads, no identity boot, and (for emptyHosted) no effects at all.
// ============================================================

/// An idle content model — no fetch, no location read, no listeners, no identity boot.
/// The host seeds the session snapshot; a first `enterHosted` drives the initial route.
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
      Identities = []
      AvailableProviders = []
      ShowIdentitySwitcher = false
      SelectedIdentity = None
      PendingClaimFocus = None }

/// Pure snapshot of the host's authoritative session into the child model (the comment
/// forms read it). Identity is host-owned; this never triggers a sync/switcher flow.
let withSession (session: GuestSession.GuestSessionData) (model: Model) : Model =
    { model with GuestSession = session }

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
            CollapsedComments = Set.empty
            // Any in-flight request from the route we're leaving is invalidated (its result
            // is stale-dropped by the shell), so its loading flag must not linger — else a
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

/// Handle a CONTENT message. Browser routing (UrlChanged) and identity messages are
/// host-owned on the hosted path — they arrive through the shell, not here — so they
/// are no-ops if dispatched into a hosted child.
let updateHosted (ctx: Content.HostContext) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    ignore ctx   // blog content update needs no host context (no title/nav effects)
    match msg with
    | LoadFeed | GotFeed _ | LoadMoreFeed | GotMoreFeed _ ->
        Feed.update msg model
    | LoadItem _ | GotItem _ | ConnectEvents _ | DisconnectEvents | GotEvent _ | EventError _
    | SubmitComment | GotSubmitComment _ | ToggleCollapse _ | SetReplyTo _ | CancelReply ->
        Item.update msg model
    | SetNewItemTitle _ | SetNewItemLink _ | SetNewItemTags _ | SubmitItem | GotSubmitItem _ ->
        NewItem.update msg model
    | LoadTagItems _ | GotTagItems _ | LoadMoreTagItems | GotMoreTagItems _ ->
        TagItems.update msg model
    | DismissError ->
        { model with Error = None }, Cmd.none
    | UrlChanged _
    | GotSessionSync _ | RevertIdentity _ | GotRevertIdentity _ | DisconnectIdentity _ | GotDisconnect _
    | LoadIdentities | GotIdentities _ | GotProviders _ | ToggleIdentitySwitcher | SelectIdentity _ ->
        model, Cmd.none

/// Render module content only — no router, header, identity switcher, sidebar, or outer
/// <main>. The host supplies the frame and places this inside its own <main>.
let contentView (ctx: Content.HostContext) (model: Model) dispatch =
    React.fragment [
        match model.Error with
        | Some err -> Shared.error err dispatch
        | None -> Html.none

        if model.IsLoading then
            Shared.loading
        else
            match model.Route with
            | ["auth"; "claim"] | ["auth"; "claim"; _] ->
                Shared.loading
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

// This is a COMPONENT (init/update/view) — the standalone `/blog` entry
// (apps/articles/src/Client/Blog/Main.fs) runs it, and the unified shell hosts the same
// component via the hosted surface above (locked decision D1).
