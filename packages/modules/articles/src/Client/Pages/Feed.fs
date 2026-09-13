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
        { model with IsLoading = true; FeedLoadingMore = false },
        Cmd.OfPromise.either Articles.ClientGen.articlesGetFeed { Cursor = None } GotFeed (fun ex -> GotFeed (Error ex.Message))

    | GotFeed (Ok response) ->
        { model with Feed = Some response; IsLoading = false; Error = None },
        continueCmd response.NextCursor.IsSome

    | GotFeed (Error err) ->
        { model with IsLoading = false; Error = Some err }, Cmd.none

    | LoadMoreFeed ->
        match model.Feed with
        | Some feed when feed.NextCursor.IsSome && not model.FeedLoadingMore ->
            { model with FeedLoadingMore = true },
            Cmd.OfPromise.either Articles.ClientGen.articlesGetFeed { Cursor = feed.NextCursor } GotMoreFeed (fun ex -> GotMoreFeed (Error ex.Message))
        | _ -> model, Cmd.none

    | GotMoreFeed (Ok response) ->
        let merged =
            match model.Feed with
            | Some existing -> { existing with Items = existing.Items @ response.Items; NextCursor = response.NextCursor }
            | None -> response
        { model with Feed = Some merged; FeedLoadingMore = false },
        continueCmd merged.NextCursor.IsSome

    | GotMoreFeed (Error _) ->
        { model with FeedLoadingMore = false }, Cmd.none

    | _ -> model, Cmd.none

let view (response: GetFeed.Response) =
    Html.div [
        prop.className "feed"
        prop.children [
            for (ts, items) in groupByDay response.Items do
                dayDivider ts
                yield! (items |> List.map feedItem)
            Html.div [ prop.key "feed-sentinel"; prop.id "feed-sentinel"; prop.className "feed-sentinel" ]
        ]
    ]
