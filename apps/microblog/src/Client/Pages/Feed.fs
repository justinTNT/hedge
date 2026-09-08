module Client.Pages.Feed

open Feliz
open Elmish
open Models.Api
open Client.Types
open Client.Shared

// Install the scroll watcher (once) that drives loads as the user scrolls.
let private watchCmd : Cmd<Msg> =
    Cmd.ofEffect (fun dispatch -> watchScroll "feed-sentinel" (fun () -> dispatch LoadMoreFeed))

// "Fill to viewport": after a page renders, if the sentinel is still on-screen,
// pull another page. Stops once content pushes the sentinel below the fold, at
// which point the scroll watcher takes over.
let private fillCmd : Cmd<Msg> =
    Cmd.ofEffect (fun dispatch -> loadMoreIfSentinelVisible "feed-sentinel" (fun () -> dispatch LoadMoreFeed))

let private continueCmd (next: bool) : Cmd<Msg> =
    if next then Cmd.batch [ watchCmd; fillCmd ] else Cmd.none

// Fit headlines to column width (usba.se BigText gimmick; no-op for other tenants).
let private fitCmd : Cmd<Msg> =
    Cmd.ofEffect (fun _ -> fitHeadlines ())

let update msg model =
    match msg with
    | LoadFeed ->
        { model with IsLoading = true; FeedLoadingMore = false },
        Cmd.OfPromise.either Client.ClientGen.getFeed "start" GotFeed (fun ex -> GotFeed (Error ex.Message))

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
            Cmd.OfPromise.either Client.ClientGen.getFeed feed.NextCursor.Value GotMoreFeed (fun ex -> GotMoreFeed (Error ex.Message))
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

let view (response: GetFeed.Response) =
    Html.div [
        prop.className "feed"
        prop.children [
            for (day, items) in groupByDay response.Items do
                dayDivider day
                yield! (items |> List.map feedItem)
            // Infinite-scroll sentinel: keyed so React preserves the same node
            // across appends, keeping its IntersectionObserver alive. Always
            // rendered; LoadMoreFeed self-guards on NextCursor once exhausted.
            Html.div [ prop.key "feed-sentinel"; prop.id "feed-sentinel"; prop.className "feed-sentinel" ]
        ]
    ]
