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
        { model with IsLoading = true; FeedLoadingMore = false },
        Cmd.OfPromise.either Blog.ClientGen.blogGetFeed { Cursor = None } GotFeed (fun ex -> GotFeed (Error ex.Message))

    | GotFeed (Ok response) ->
        { model with Feed = Some response; IsLoading = false; Error = None },
        Cmd.batch [ continueCmd response.NextCursor.IsSome; fitCmd ]

    | GotFeed (Error err) ->
        { model with IsLoading = false; Error = Some err }, Cmd.none

    | LoadMoreFeed ->
        // Safe on non-feed routes without a route check: the sentinel element
        // only exists while the feed is rendered, so the watcher/fill no-op
        // elsewhere. (nextCursor guards the end; FeedLoadingMore guards overlap.)
        match model.Feed with
        | Some feed when feed.NextCursor.IsSome && not model.FeedLoadingMore ->
            { model with FeedLoadingMore = true },
            Cmd.OfPromise.either Blog.ClientGen.blogGetFeed { Cursor = feed.NextCursor } GotMoreFeed (fun ex -> GotMoreFeed (Error ex.Message))
        | _ -> model, Cmd.none

    | GotMoreFeed (Ok response) ->
        let merged =
            match model.Feed with
            | Some existing -> { existing with Items = existing.Items @ response.Items; NextCursor = response.NextCursor }
            | None -> response
        { model with Feed = Some merged; FeedLoadingMore = false },
        Cmd.batch [ continueCmd merged.NextCursor.IsSome; fitCmd ]

    | GotMoreFeed (Error _) ->
        // Leave the loaded items in place; a later scroll can retry.
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
