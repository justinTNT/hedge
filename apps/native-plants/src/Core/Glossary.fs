module NativePlants.Glossary

open System.Text.RegularExpressions
open Models.Api

type Note = { Key: string; Heading: string; Body: string; Image: string; Source: string }
type Chunk = { Text: string; Note: Note option }
type Matcher = { Pattern: Regex option; Entries: Map<string,Note> }

let private key (value:string) = value.ToLowerInvariant()
let glossaryNote (g:GlossaryEntry) =
    {Key=g.Id;Heading=g.Term;Body=g.Definition;Image=g.Illustration;Source=g.SourceLabel}
let referenceNote (r:ReferenceEntry) =
    {Key=r.Id;Heading=(if r.Kind="usage" then "Reference "+string r.Number else "Source reference")
     Body=r.Citation;Image="";Source=r.SourceLabel}
let private matcher (entries:(string*Note) list) =
    let entries = entries |> List.filter(fun (label,_) -> label.Trim()<>"") |> List.map(fun (label,note) -> key label,note) |> Map.ofList
    let terms = entries |> Map.toList |> List.map fst |> List.sortBy(fun s -> -s.Length,s) |> List.map(fun term -> (Regex.Escape term).Replace(@"\-", "-"))
    {Entries=entries;Pattern=if terms.IsEmpty then None else Some(Regex("(?<![A-Za-z0-9_])("+String.concat "|" terms+")(?![A-Za-z0-9_])",RegexOptions.IgnoreCase))}
let glossaryMatcher (entries:GlossaryEntry list) =
    entries |> List.collect(fun g -> g.Term::g.Aliases |> List.collect(fun term ->
        [term;term.Replace("-","–");term.Replace("-","‑")] |> List.distinct |> List.map(fun label -> label,glossaryNote g))) |> matcher
let referenceMatcher (entries:ReferenceEntry list) =
    entries |> List.collect(fun r -> r.Aliases |> List.map(fun label -> label,referenceNote r)) |> matcher

/// Preserve every source character. Longest matches win; no stemming or substring guesses.
let annotate matcher (text:string) =
    match matcher.Pattern with
    | None -> [{Text=text;Note=None}]
    | Some pattern ->
        let mutable cursor=0
        [ for m in pattern.Matches(text) do
              if m.Index>cursor then yield {Text=text.Substring(cursor,m.Index-cursor);Note=None}
              yield {Text=m.Value;Note=Map.tryFind (key m.Value) matcher.Entries}
              cursor<-m.Index+m.Length
          if cursor<text.Length then yield {Text=text.Substring(cursor);Note=None} ]

/// Only call in the numbered Aboriginal-use citation namespace, never on measurements/dates.
let usageCitations glossary (references:ReferenceEntry list) (text:string) =
    let numbered=references |> List.filter(fun r->r.Kind="usage") |> List.map(fun r->r.Number,r) |> Map.ofList
    let groups=Regex(@"\(\s*\d{1,2}(?:\s*,\s*\d{1,2})*\s*\)")
    let digits=Regex(@"\d+")
    let mutable cursor=0
    [ for group in groups.Matches(text) do
          if group.Index>cursor then yield! annotate glossary (text.Substring(cursor,group.Index-cursor))
          let mutable offset=0
          for number in digits.Matches(group.Value) do
              if number.Index>offset then yield {Text=group.Value.Substring(offset,number.Index-offset);Note=None}
              let n=int number.Value
              let note=Map.tryFind n numbered |> Option.map referenceNote |> Option.defaultValue
                           {Key="missing-reference-"+string n;Heading="Reference "+string n;Body="This reference is cited in the account, but its entry is missing from the supplied reference list.";Image="";Source="Unresolved source citation"}
              yield {Text=number.Value;Note=Some note}
              offset<-number.Index+number.Length
          yield {Text=group.Value.Substring(offset);Note=None}
          cursor<-group.Index+group.Length
      if cursor<text.Length then yield! annotate glossary (text.Substring(cursor)) ]
