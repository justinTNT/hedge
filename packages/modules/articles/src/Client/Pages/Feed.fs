module Articles.Client.Pages.Feed

open Feliz
open Elmish
open Articles.Api
open Articles.Client.Types
open Articles.Client.Shared

// Install the scroll watcher (once) that drives loads as the user scrolls.
let private watchCmd : Cmd<Msg> =
    Cmd.ofEffect (fun dispatch -> watchScroll "feed-sentinel" (fun () -> dispatch LoadMoreFeed))

// "Fill to viewport": after a page renders, if the sentinel is still on-screen,
// pull another page. Stops once content pushes it below the fold.
let private fillCmd : Cmd<Msg> =
    Cmd.ofEffect (fun dispatch -> loadMoreIfSentinelVisible "feed-sentinel" (fun () -> dispatch LoadMoreFeed))

let private continueCmd (next: bool) : Cmd<Msg> =
    if next then Cmd.batch [ watchCmd; fillCmd ] else Cmd.none

let update msg model =
    match msg with
    | LoadFeed ->
        let gen = model.LoadGen + 1
        { model with IsLoading = true; FeedLoadingMore = false; LoadGen = gen },
        Cmd.OfPromise.either Articles.Client.Shared.Api.articlesGetFeed { Cursor = None }
            (fun r -> GotFeed (gen, r)) (fun ex -> GotFeed (gen, Error ex.Message))

    // CP-A: drop a completion from a superseded read generation (the correctness authority).
    | GotFeed (gen, _) when gen <> model.LoadGen -> model, Cmd.none

    | GotFeed (_, Ok response) ->
        match model.Route with
        | [] ->
            { model with Feed = Some response; IsLoading = false; Error = None },
            continueCmd response.NextCursor.IsSome
        | _ -> model, Cmd.none

    | GotFeed (_, Error err) ->
        { model with IsLoading = false; Error = Some err }, Cmd.none

    | LoadMoreFeed ->
        match model.Feed with
        | Some feed when feed.NextCursor.IsSome && not model.FeedLoadingMore ->
            let gen = model.LoadGen + 1
            { model with FeedLoadingMore = true; LoadGen = gen },
            Cmd.OfPromise.either Articles.Client.Shared.Api.articlesGetFeed { Cursor = feed.NextCursor }
                (fun r -> GotMoreFeed (gen, feed.NextCursor, r)) (fun ex -> GotMoreFeed (gen, feed.NextCursor, Error ex.Message))
        | _ -> model, Cmd.none

    | GotMoreFeed (gen, _, _) when gen <> model.LoadGen -> model, Cmd.none

    | GotMoreFeed (_, cursor, Ok response) ->
        match model.Feed with
        | Some existing when existing.NextCursor = cursor ->
            let merged = { existing with Items = existing.Items @ response.Items; NextCursor = response.NextCursor }
            { model with Feed = Some merged; FeedLoadingMore = false },
            continueCmd merged.NextCursor.IsSome
        | _ -> { model with FeedLoadingMore = false }, Cmd.none

    | GotMoreFeed (_, _, Error _) ->
        { model with FeedLoadingMore = false }, Cmd.none

    | _ -> model, Cmd.none

let view (ctx: Content.HostContext) (response: GetFeed.Response) =
    Html.div [
        prop.className "feed"
        prop.children [
            for (ts, items) in groupByDay response.Items do
                dayDivider ts
                yield! (items |> List.map (feedItem ctx))
            Html.div [ prop.key "feed-sentinel"; prop.id "feed-sentinel"; prop.className "feed-sentinel" ]
        ]
    ]
