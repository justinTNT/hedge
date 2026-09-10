module Client.Rhyming.App

// rhyming.darwin.news — a second view over darwin.news's own items, pairing the
// articles that share a `rhyme-*` tag and listing each pair side-by-side. Its own
// tiny Elmish entry (mounted via the framework host-mount); reuses the generated
// getRhymes client + the shared FeedItem shape.

open Feliz
open Elmish
open Fable.Core
open Hedge.Interface
open Models.Api

/// Plain text from an Extract (ProseMirror JSON) — walks text nodes so we don't
/// pull the TipTap bundle onto this page just for a teaser. Falls back to the raw
/// value for legacy non-JSON content.
[<Emit("""(function (s) {
  try {
    return (function walk(n){ if(!n) return ''; if(n.type==='text') return n.text||''; if(Array.isArray(n.content)) return n.content.map(walk).join(' '); return ''; })(JSON.parse(s)).replace(/\s+/g,' ').trim();
  } catch (e) { return (s || '').replace(/\s+/g,' ').trim(); }
})($0)""")>]
let private plainText (json: string) : string = jsNative

let private teaserOf (item: GetFeed.FeedItem) : string option =
    match item.Extract with
    | Some (RichContent json) ->
        let t = plainText json
        if t = "" then None
        elif t.Length > 180 then Some (t.[..179].TrimEnd() + "…")
        else Some t
    | None -> None

type Model = { Rhymes: GetRhymes.RhymeGroup list; Loading: bool; Error: string option }
type Msg = GotRhymes of Result<GetRhymes.Response, string>

let init () =
    { Rhymes = []; Loading = true; Error = None },
    Cmd.OfPromise.either Client.ClientGen.getRhymes () GotRhymes (fun ex -> GotRhymes (Error ex.Message))

let update msg model =
    match msg with
    | GotRhymes (Ok r) -> { model with Rhymes = r.Rhymes; Loading = false; Error = None }, Cmd.none
    | GotRhymes (Error e) -> { model with Loading = false; Error = Some e }, Cmd.none

/// The full articles live on darwin.news; each card links across to its post.
let private articleUrl (item: GetFeed.FeedItem) =
    "https://darwin.news/" + (item.Slug |> Option.defaultValue item.Id)

let private card (item: GetFeed.FeedItem) =
    Html.a [
        prop.key item.Id
        prop.className "rhyme-card"
        prop.href (articleUrl item)
        prop.children [
            match item.Image with
            | Some src -> Html.img [ prop.className "rhyme-img"; prop.src src ]
            | None -> Html.none
            Html.div [
                prop.className "rhyme-body"
                prop.children [
                    Html.h3 [ prop.className "rhyme-title"; prop.text item.Title ]
                    match teaserOf item with
                    | Some t -> Html.p [ prop.className "rhyme-teaser"; prop.text t ]
                    | None -> Html.none
                ]
            ]
        ]
    ]

let private groupView (g: GetRhymes.RhymeGroup) =
    Html.div [
        prop.key g.Tag
        prop.className "rhyme-row"
        prop.children [ for item in g.Items -> card item ]
    ]

let private view model _dispatch =
    Html.div [
        prop.className "rhyme-app"
        prop.children [
            Html.header [
                prop.className "rhyme-head"
                prop.children [
                    Html.h1 "Rhyming"
                    Html.p [
                        prop.className "rhyme-tag"
                        prop.children [
                            Html.a [ prop.href "/"; prop.text "darwin.news" ]
                            Html.text ", in pairs"
                        ]
                    ]
                    Html.blockquote [
                        prop.className "rhyme-epigraph"
                        prop.children [
                            Html.span [ prop.text "“History never repeats itself, but the kaleidoscopic combinations of the pictured present often seem to be constructed out of the broken fragments of antique legends.”" ]
                            Html.cite [ prop.text "Mark Twain" ]
                        ]
                    ]
                ]
            ]
            if model.Loading then Html.div [ prop.className "loading"; prop.text "Loading…" ]
            else
                match model.Error with
                | Some e -> Html.div [ prop.className "error"; prop.text e ]
                | None when List.isEmpty model.Rhymes -> Html.p [ prop.className "empty"; prop.text "No rhymes yet." ]
                | None -> Html.div [ prop.className "rhyme-list"; prop.children [ for g in model.Rhymes -> groupView g ] ]
        ]
    ]

open Elmish.React

Program.mkProgram init update view
|> Program.withReactSynchronous "app"
|> Program.run
