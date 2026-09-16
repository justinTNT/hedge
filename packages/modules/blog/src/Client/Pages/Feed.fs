module Blog.Client.Pages.Feed

open Feliz
open Elmish
open Blog.Api
open Blog.Client.Types
open Blog.Client.Shared

// Install the scroll watcher (once) that drives loads as the user scrolls. The id is
// module-distinct ("blog-feed-sentinel") so that, when a unified shell hosts blog and
// articles in one document, the two feeds' global scroll watchers never collide (each
// watcher fires into its own module's dispatch).
let private watchCmd : Cmd<Msg> =
    Cmd.ofEffect (fun dispatch -> watchScroll "blog-feed-sentinel" (fun () -> dispatch LoadMoreFeed))

// "Fill to viewport": after a page renders, if the sentinel is still on-screen,
// pull another page. Stops once content pushes the sentinel below the fold, at
// which point the scroll watcher takes over.
let private fillCmd : Cmd<Msg> =
    Cmd.ofEffect (fun dispatch -> loadMoreIfSentinelVisible "blog-feed-sentinel" (fun () -> dispatch LoadMoreFeed))

let private continueCmd (next: bool) : Cmd<Msg> =
    if next then Cmd.batch [ watchCmd; fillCmd ] else Cmd.none

// Fit headlines to column width — the BigText gimmick, gated on the tenant's
// `bigText` feature flag rather than a hardcoded slug list.
let private fitCmd : Cmd<Msg> =
    Cmd.ofEffect (fun _ -> if Hedge.Tenant.hasFeature "bigText" then fitHeadlines ())

let update msg model =
    match msg with
    | LoadFeed ->
        let gen = model.LoadGen + 1
        { model with IsLoading = true; FeedLoadingMore = false; LoadGen = gen },
        Cmd.OfPromise.either Blog.Client.Shared.Api.blogGetFeed { Cursor = None }
            (fun r -> GotFeed (gen, r)) (fun ex -> GotFeed (gen, Error ex.Message))

    // CP-A: a completion from a superseded read generation (newer load, navigation, or
    // invalidation bumped LoadGen) is dropped — this is the correctness authority, not Route.
    | GotFeed (gen, _) when gen <> model.LoadGen -> model, Cmd.none

    | GotFeed (_, Ok response) ->
        // Current gen: apply only while the feed is the current view ([]) (Route is secondary
        // belt-and-suspenders to the gen check above).
        match model.Route with
        | [] ->
            { model with Feed = Some response; IsLoading = false; Error = None },
            Cmd.batch [ continueCmd response.NextCursor.IsSome; fitCmd ]
        | _ -> model, Cmd.none

    | GotFeed (_, Error err) ->
        { model with IsLoading = false; Error = Some err }, Cmd.none

    | LoadMoreFeed ->
        // The sentinel exists only while the feed is rendered, so the watcher/fill no-op
        // elsewhere. (nextCursor guards the end; FeedLoadingMore guards overlap.)
        match model.Feed with
        | Some feed when feed.NextCursor.IsSome && not model.FeedLoadingMore ->
            let gen = model.LoadGen + 1
            { model with FeedLoadingMore = true; LoadGen = gen },
            Cmd.OfPromise.either Blog.Client.Shared.Api.blogGetFeed { Cursor = feed.NextCursor }
                (fun r -> GotMoreFeed (gen, feed.NextCursor, r)) (fun ex -> GotMoreFeed (gen, feed.NextCursor, Error ex.Message))
        | _ -> model, Cmd.none

    | GotMoreFeed (gen, _, _) when gen <> model.LoadGen -> model, Cmd.none

    | GotMoreFeed (_, cursor, Ok response) ->
        // Merge only into the cache whose cursor we actually requested from — never an
        // obsolete page onto a feed that has since moved on.
        match model.Feed with
        | Some existing when existing.NextCursor = cursor ->
            let merged = { existing with Items = existing.Items @ response.Items; NextCursor = response.NextCursor }
            { model with Feed = Some merged; FeedLoadingMore = false },
            Cmd.batch [ continueCmd merged.NextCursor.IsSome; fitCmd ]
        | _ -> { model with FeedLoadingMore = false }, Cmd.none

    | GotMoreFeed (_, _, Error _) ->
        { model with FeedLoadingMore = false }, Cmd.none

    | _ -> model, Cmd.none

let view (ctx: Content.HostContext) (response: GetFeed.Response) =
    Html.div [
        prop.className "feed"
        prop.children [
            for (day, items) in groupByDay response.Items do
                dayDivider day
                yield! (items |> List.map (feedItem ctx))
            // Infinite-scroll sentinel: keyed so React preserves the same node
            // across appends, keeping its IntersectionObserver alive. Always
            // rendered; LoadMoreFeed self-guards on NextCursor once exhausted.
            Html.div [ prop.key "blog-feed-sentinel"; prop.id "blog-feed-sentinel"; prop.className "feed-sentinel" ]
        ]
    ]
