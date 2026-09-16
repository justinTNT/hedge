module Blog.Client.Pages.TagItems

open Feliz
open Elmish
open Blog.Api
open Blog.Client.Types
open Blog.Client.Shared

// Infinite scroll for tag pages, mirroring the main feed (own "tag-sentinel").
let private watchCmd : Cmd<Msg> =
    Cmd.ofEffect (fun dispatch -> watchScroll "tag-sentinel" (fun () -> dispatch LoadMoreTagItems))
let private fillCmd : Cmd<Msg> =
    Cmd.ofEffect (fun dispatch -> loadMoreIfSentinelVisible "tag-sentinel" (fun () -> dispatch LoadMoreTagItems))
let private fitCmd : Cmd<Msg> =
    Cmd.ofEffect (fun _ -> if Hedge.Tenant.hasFeature "bigText" then fitHeadlines ())
let private continueCmd (next: bool) : Cmd<Msg> =
    if next then Cmd.batch [ watchCmd; fillCmd; fitCmd ] else fitCmd

// --- Update ---

let update msg model =
    match msg with
    | LoadTagItems tag ->
        let gen = model.LoadGen + 1
        { model with IsLoading = true; TagItems = None; TagLoadingMore = false; LoadGen = gen },
        Cmd.OfPromise.either (Blog.Client.Shared.Api.blogGetItemsByTag tag) { Cursor = None }
            (fun r -> GotTagItems (gen, r)) (fun ex -> GotTagItems (gen, Error ex.Message))

    | GotTagItems (gen, _) when gen <> model.LoadGen -> model, Cmd.none

    | GotTagItems (_, Ok response) ->
        // Current gen: apply only if this is still the tag the route wants (secondary to gen).
        match model.Route with
        | [ "tag"; name ] when name = response.Tag ->
            { model with TagItems = Some response; IsLoading = false; Error = None },
            continueCmd response.NextCursor.IsSome
        | _ -> model, Cmd.none

    | GotTagItems (_, Error err) ->
        { model with IsLoading = false; Error = Some err }, Cmd.none

    | LoadMoreTagItems ->
        match model.TagItems with
        | Some t when t.NextCursor.IsSome && not model.TagLoadingMore ->
            let gen = model.LoadGen + 1
            { model with TagLoadingMore = true; LoadGen = gen },
            Cmd.OfPromise.either (Blog.Client.Shared.Api.blogGetItemsByTag t.Tag) { Cursor = t.NextCursor }
                (fun r -> GotMoreTagItems (gen, t.NextCursor, r)) (fun ex -> GotMoreTagItems (gen, t.NextCursor, Error ex.Message))
        | _ -> model, Cmd.none

    | GotMoreTagItems (gen, _, _) when gen <> model.LoadGen -> model, Cmd.none

    | GotMoreTagItems (_, cursor, Ok response) ->
        // Merge only into the matching tag's cache, at the cursor we actually requested from.
        match model.TagItems with
        | Some existing when existing.Tag = response.Tag && existing.NextCursor = cursor ->
            let merged = { existing with Items = existing.Items @ response.Items; NextCursor = response.NextCursor }
            { model with TagItems = Some merged; TagLoadingMore = false },
            continueCmd merged.NextCursor.IsSome
        | _ -> { model with TagLoadingMore = false }, Cmd.none

    | GotMoreTagItems (_, _, Error _) ->
        { model with TagLoadingMore = false }, Cmd.none

    | _ -> model, Cmd.none

// --- View ---

let view (ctx: Content.HostContext) (response: GetItemsByTag.Response) =
    Html.div [
        prop.className "feed"
        prop.children [
            Html.div [
                prop.className "tag-header"
                prop.children [ tagPill ctx response.Tag ]
            ]
            for (day, items) in groupByDay response.Items do
                dayDivider day
                yield! (items |> List.map (feedItem ctx))
            Html.div [ prop.key "tag-sentinel"; prop.id "tag-sentinel"; prop.className "feed-sentinel" ]
        ]
    ]
