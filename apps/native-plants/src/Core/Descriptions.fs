module NativePlants.Descriptions

// Descriptive search over published account text: a person searches for a feature they can see, such as
// "colourful bark". Pure, so it runs on the phone over the saved accounts (and could run on the server
// over its snapshot). Names and taxonomy are matched elsewhere (Catalogue.score) and always come first.
// This finds what an account says; it does not certify an identification.

open System
open Models.Api

/// Sections that describe the plant. The bibliography is not a claim about it.
let searchable (label: string) = label <> "References"
/// Recognition and morphology first, then season and place, then cultivation, uses and notes.
let weight (label: string) =
    match label with
    | "Recognise this plant" | "Habit" | "Bark" | "Leaves" | "Phyllodes" | "Flowers" | "Fruit" -> 0
    | "Flowering season" | "Fruiting season" | "Habitat" | "Distribution" -> 1
    | _ -> 2

/// Conservative spelling equivalents folded to the accounts' British spelling, and one explicit word
/// form. No stemming and no botanical synonymy: `red` must not find `coloured`, and a glossary term is
/// not a claim about a plant.
let private equivalents =
    [ "color", "colour"; "colorful", "colourful"; "colored", "coloured"; "colors", "colours"; "gray", "grey"; "grayish", "greyish"
      "fiber", "fibre"; "center", "centre"; "meter", "metre"; "sulfur", "sulphur"; "mold", "mould"
      "odor", "odour"; "flavor", "flavour"; "vigor", "vigour"; "harbor", "harbour"; "leaves", "leaf" ]
    |> Map.ofList
let private stopWords = set [ "the"; "a"; "an"; "and"; "or"; "of"; "with"; "in"; "on"; "at"; "to"; "is"; "are"; "has"; "have"; "its"; "it"; "this"; "that" ]

/// The canonical form of a word: lower case, equivalents folded, a simple plural reduced (flowers → flower),
/// equivalents folded again so `colors` meets `colour`.
let canonical (word: string) =
    let fold w = match Map.tryFind w equivalents with Some e -> e | None -> w
    let w = fold (word.ToLowerInvariant())
    let w = if w.Length > 3 && w.EndsWith "s" && not (w.EndsWith "ss") then w.Substring(0, w.Length - 1) else w
    fold w

type Word = { Text: string; Start: int; End: int; Canonical: string }
/// The words of a text with their positions. Letters and digits make a word; everything else separates,
/// so hyphens and punctuation never glue two words together.
let words (text: string) : Word[] =
    let result = ResizeArray<Word>()
    let mutable start = -1
    for i in 0 .. text.Length do
        let inWord = i < text.Length && Char.IsLetterOrDigit text.[i]
        if inWord && start < 0 then start <- i
        elif not inWord && start >= 0 then
            let w = text.Substring(start, i - start)
            result.Add { Text = w; Start = start; End = i; Canonical = canonical w }
            start <- -1
    result.ToArray()

/// The meaningful words of a query, canonical and distinct, at most eight. Empty means no descriptive
/// search: blank or punctuation-only queries keep ordinary browsing.
let tokens (query: string) : string[] =
    let q = if query.Length > 160 then query.Substring(0, 160) else query
    words q |> Array.map (fun w -> w.Canonical) |> Array.filter (fun w -> w <> "" && not (stopWords.Contains w)) |> Array.distinct |> Array.truncate 8

type Match = { PlantId: string; Label: string; Excerpt: string; Phrase: bool; Weight: int }

let private phraseAt (canon: string[]) (tokens: string[]) =
    let n = tokens.Length
    let rec go i =
        if n = 0 || i + n > canon.Length then None
        elif Array.forall2 (=) canon.[i .. i + n - 1] tokens then Some i
        else go (i + 1)
    go 0
/// A verbatim window around the match, wide enough to keep qualifications and negation in view.
let private excerpt (text: string) (ws: Word[]) (first: int) (count: int) =
    let a = max 0 (first - 10)
    let b = min (ws.Length - 1) (first + count + 16)
    let body = text.Substring(ws.[a].Start, ws.[b].End - ws.[a].Start)
    (if a > 0 then "… " else "") + body + (if b < ws.Length - 1 then " …" else "")
let private matchSection (tokens: string[]) (section: Section) =
    let ws = words section.Text
    let canon = ws |> Array.map (fun w -> w.Canonical)
    if not (tokens |> Array.forall (fun t -> Array.contains t canon)) then None
    else
        match phraseAt canon tokens with
        | Some i -> Some (true, excerpt section.Text ws i tokens.Length)
        | None ->
            let first = tokens |> Array.map (fun t -> Array.findIndex ((=) t) canon) |> Array.min
            Some (false, excerpt section.Text ws first 1)

/// Every account carrying all the query's words within one section (never assembled across sections):
/// the best section per plant, phrase matches before scattered words, recognition before notes. Within a
/// tier the callers' order stands, so pass accounts in scientific-name order for deterministic ties.
let search (query: string) (accounts: (string * Section list) list) : Match list =
    let tokens = tokens query
    if tokens.Length = 0 then []
    else
        accounts
        |> List.choose (fun (id, sections) ->
            sections
            |> List.filter (fun s -> searchable s.Label)
            |> List.choose (fun s -> matchSection tokens s |> Option.map (fun (phrase, text) -> { PlantId = id; Label = s.Label; Excerpt = text; Phrase = phrase; Weight = weight s.Label }))
            |> List.sortBy (fun m -> (if m.Phrase then 0 else 1), m.Weight)
            |> List.tryHead)
        |> List.sortBy (fun m -> (if m.Phrase then 0 else 1), m.Weight)

/// An excerpt as runs of text, each marked when it is one of the query's words, for highlighting as text.
let fragments (tokens: string[]) (excerpt: string) : (string * bool) list =
    let ws = words excerpt
    let parts = ResizeArray<string * bool>()
    let mutable cursor = 0
    for w in ws do
        if w.Start > cursor then parts.Add (excerpt.Substring(cursor, w.Start - cursor), false)
        parts.Add (w.Text, Array.contains w.Canonical tokens)
        cursor <- w.End
    if cursor < excerpt.Length then parts.Add (excerpt.Substring cursor, false)
    List.ofSeq parts
