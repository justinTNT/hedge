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
        { model with IsLoading = true; TagItems = None; TagLoadingMore = false },
        Cmd.OfPromise.either (Blog.ClientGen.blogGetItemsByTag tag) { Cursor = None } GotTagItems (fun ex -> GotTagItems (Error ex.Message))

    | GotTagItems (Ok response) ->
        { model with TagItems = Some response; IsLoading = false; Error = None },
        continueCmd response.NextCursor.IsSome

    | GotTagItems (Error err) ->
        { model with IsLoading = false; Error = Some err }, Cmd.none

    | LoadMoreTagItems ->
        match model.TagItems with
        | Some t when t.NextCursor.IsSome && not model.TagLoadingMore ->
            { model with TagLoadingMore = true },
            Cmd.OfPromise.either (Blog.ClientGen.blogGetItemsByTag t.Tag) { Cursor = t.NextCursor } GotMoreTagItems (fun ex -> GotMoreTagItems (Error ex.Message))
        | _ -> model, Cmd.none

    | GotMoreTagItems (Ok response) ->
        // Only merge a page whose tag matches the current view — a delayed page for a tag
        // we've left must not append its items/cursor to a different tag (belt-and-suspenders
        // with the shell's activation drop).
        let merged =
            match model.TagItems with
            | Some existing when existing.Tag = response.Tag ->
                { existing with Items = existing.Items @ response.Items; NextCursor = response.NextCursor }
            | Some existing -> existing
            | None -> response
        { model with TagItems = Some merged; TagLoadingMore = false },
        continueCmd merged.NextCursor.IsSome

    | GotMoreTagItems (Error _) ->
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
