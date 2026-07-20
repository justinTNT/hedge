module Server.Alerts

open System.Text.RegularExpressions
open Fable.Core
open Fable.Core.JsInterop
open Hedge.Workers
open Server.Env
open Server.Db

// ---- console helpers ----
[<Emit("console.log($0)")>]
let private log (s: string) : unit = jsNative
[<Emit("console.warn($0)")>]
let private logWarn (s: string) : unit = jsNative
[<Emit("console.error($0, $1)")>]
let private logError (s: string) (detail: string) : unit = jsNative

/// Decode HTML/XML entities, including numeric ones. `&amp;` last so an
/// already-decoded `&` isn't re-consumed. Kept as one Emit to avoid relying on
/// Fable's regex-replace-with-evaluator for the numeric cases.
[<Emit("""$0.replace(/&#x([0-9a-fA-F]+);/g, function(_, h){return String.fromCharCode(parseInt(h,16));}).replace(/&#([0-9]+);/g, function(_, d){return String.fromCharCode(parseInt(d,10));}).replace(/&lt;/g,'<').replace(/&gt;/g,'>').replace(/&quot;/g,'"').replace(/&#39;/g,"'").replace(/&apos;/g,"'").replace(/&nbsp;/g,' ').replace(/&amp;/g,'&')""")>]
let private decodeEntities (s: string) : string = jsNative

// ---- The ONE fragile piece: pure Atom extraction (regex↔parser swap point) ----
// All patterns are u-flag-safe (Fable emits /…/gu): no redundant escapes;
// literal < and > need no backslash. If format drift bites or a 2nd producer
// arrives, replace ONLY parseFeed (e.g. bundle fast-xml-parser).

type FeedEntry =
    { EntryKey: string
      Link: string
      Published: int   // epoch seconds; NaN/absurd sanitized in ingestEntries
      Title: string
      Snippet: string }

let private stripTags (s: string) : string = Regex.Replace(s, "<[^>]*>", "")
let private clean (s: string) : string = (s |> decodeEntities |> stripTags |> decodeEntities).Trim()
let private truncate (n: int) (s: string) : string = if s.Length > n then s.[.. n - 1] else s

let private group1 (pattern: string) (input: string) : string option =
    let m = Regex.Match(input, pattern)
    if m.Success && m.Groups.Count > 1 then Some m.Groups.[1].Value else None

let parseFeed (xml: string) : FeedEntry list =
    let blocks =
        [ for m in Regex.Matches(xml, "<entry[^>]*>([\s\S]*?)</entry>") -> m.Groups.[1].Value ]
        |> List.truncate 50
    [ for block in blocks do
        match group1 "<id>([\s\S]*?)</id>" block, group1 "<link[^>]*href=\"([^\"]*)\"" block with
        | Some idRaw, Some hrefRaw ->
            let title = group1 "<title[^>]*>([\s\S]*?)</title>" block |> Option.map clean |> Option.defaultValue ""
            let snippet = group1 "<content[^>]*>([\s\S]*?)</content>" block |> Option.map clean |> Option.defaultValue ""
            let published =
                group1 "<published>([\s\S]*?)</published>" block
                |> Option.map (fun p -> isoToEpoch (p.Trim()))
                |> Option.defaultValue 0
            yield
                { EntryKey = (decodeEntities idRaw).Trim()
                  Link = (decodeEntities hrefRaw).Trim()
                  Published = published
                  Title = truncate 500 title
                  Snippet = truncate 2000 snippet }
        | _ -> ()   // required id/link missing → skip this entry
    ]

// ---- Reusable tail (Seam 2): feed and a future push producer converge here,
//      so all durable-state sanitizing lives in ingestEntries. ----

let private isHttp (url: string) = url.StartsWith("http://") || url.StartsWith("https://")

let ingestEntries (env: Env) (source: AlertSourceRow) (entries: FeedEntry list) : JS.Promise<int> =
    promise {
        let now = epochNow ()
        // clamp so a mis-parsed/absurd date can't skew promote order or display
        let clampPub (p: int) = if isFinite p && p > 0 && p <= now then p else now
        let valid = entries |> List.filter (fun e -> e.EntryKey <> "" && isHttp e.Link)
        if List.isEmpty valid then
            return 0
        else
            let stmts =
                valid
                |> List.map (fun e ->
                    bind (env.DB.prepare Sql.insertPendingPost)
                        [| box (newId ()); box source.Id; box e.EntryKey
                           box e.Title; box e.Link; box e.Snippet; box (clampPub e.Published); box now |])
                |> List.toArray
            let! _ = env.DB.batch(stmts)
            return valid.Length
    }

// ---- Poll one source (blast-radius boundary), promote approved drafts ----

let pollSource (env: Env) (source: AlertSourceRow) : JS.Promise<unit> =
    promise {
        try
            let opts = createObj [ "headers" ==> createObj [ "User-Agent" ==> "hedge-alerts/1.0" ] ]
            let! resp = fetchRaw source.FeedUrl opts
            let! body = responseText resp
            if body.Length > 2_000_000 then
                logWarn (sprintf "alerts: feed too large (%d bytes): %s" body.Length source.FeedUrl)
            else
                let entries = parseFeed body
                let! imported = ingestEntries env source entries
                log (sprintf "alerts: topic=%s bytes=%d parsed=%d imported=%d" source.Topic body.Length entries.Length imported)
                if entries.Length = 0 && body.Length > 500 then
                    logWarn (sprintf "alerts: non-empty body but 0 entries (feed drift?): %s" source.FeedUrl)
        with ex ->
            logError (sprintf "alerts: poll failed %s" source.FeedUrl) ex.Message
    }

let promoteApproved (env: Env) : JS.Promise<unit> =
    promise {
        let! result = (env.DB.prepare Sql.selectPromotable).all()
        for row in result.results do
            do! promise {
                try
                    let p = parsePendingPostRow row
                    let topic = rowStr row "topic"
                    let create : MicroblogItemCreate =
                        { Title = p.Title; Link = Some p.Link; Image = None
                          Extract = Some p.Snippet; OwnerComment = p.OwnerComment
                          Slug = None; ViewCount = 0; OriginEntryKey = Some p.EntryKey }
                    let it = Items.createItemStmts env.DB create [ topic ]
                    let! _ = env.DB.batch(it.Stmts)
                    return ()
                with ex ->
                    // unique origin_entry_key ⇒ a concurrent double-promote rolls back the loser
                    logError "alerts: promote failed" ex.Message
                    return ()
            }
    }

/// Cron entry point: poll enabled sources, then promote approved drafts.
let run (env: Env) (_ctx: ExecutionContext) : JS.Promise<unit> =
    promise {
        let! sourcesResult = (env.DB.prepare Sql.selectEnabledAlertSources).all()
        let sources = sourcesResult.results |> Array.map parseAlertSourceRow
        for s in sources do
            do! pollSource env s
        do! promoteApproved env
    }
