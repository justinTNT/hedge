module Alerts.Cron

// The alerts pipeline: poll enabled feeds → ingest entries into the curation queue → promote
// approved drafts into the host's content feed. Ported from the pre-module alerts branch
// (apps/microblog/src/Server/Alerts.fs), adapted to the module architecture:
//   - takes an injected `Alerts.Services` (DB + NewId + Now + PromoteToFeed), never an app Env;
//   - promotion goes through services.PromoteToFeed (host-supplied) and records dedup state in the
//     module's own alerts_promotions table, batched with the feed insert for one atomic COMMIT —
//     so it never reads or writes the host feed's schema.
// Admin + cron only: no HTTP endpoints, no client.

open System.Text.RegularExpressions
open Fable.Core
open Fable.Core.JsInterop
open Hedge.Workers
open Alerts.Db
open Alerts.Services

// ---- console helpers ----
[<Emit("console.log($0)")>]
let private log (s: string) : unit = jsNative
[<Emit("console.warn($0)")>]
let private logWarn (s: string) : unit = jsNative
[<Emit("console.error($0, $1)")>]
let private logError (s: string) (detail: string) : unit = jsNative

/// Guard isoToEpoch's NaN return (Number.isFinite). Module-local — the framework needs it nowhere else.
[<Emit("Number.isFinite($0)")>]
let private isFinite (x: int) : bool = jsNative

/// The upstream response status, so a non-2xx (e.g. a 500 error page) is logged + skipped rather
/// than silently handed to parseFeed as if it were a feed.
[<Emit("$0.status")>]
let private respStatus (r: WorkerResponse) : int = jsNative

/// Decode HTML/XML entities, including numeric ones. `&amp;` last so an already-decoded `&` isn't
/// re-consumed. One Emit to avoid relying on Fable's regex-replace-with-evaluator for numeric cases.
[<Emit("""$0.replace(/&#x([0-9a-fA-F]+);/g, function(_, h){return String.fromCharCode(parseInt(h,16));}).replace(/&#([0-9]+);/g, function(_, d){return String.fromCharCode(parseInt(d,10));}).replace(/&lt;/g,'<').replace(/&gt;/g,'>').replace(/&quot;/g,'"').replace(/&#39;/g,"'").replace(/&apos;/g,"'").replace(/&nbsp;/g,' ').replace(/&amp;/g,'&')""")>]
let private decodeEntities (s: string) : string = jsNative

// ---- The ONE fragile piece: pure Atom extraction (regex↔parser swap point) ----
// If format drift bites or a 2nd producer arrives, replace ONLY parseFeed (e.g. fast-xml-parser).

type FeedEntry =
    { EntryKey: string
      Link: string
      Published: int   // epoch seconds; NaN/absurd sanitized in ingestEntries
      Title: string
      Snippet: string }

let private stripTags (s: string) : string = Regex.Replace(s, "<[^>]*>", "")
/// Strip twice: entity-encoded markup (`&lt;p&gt;`) only becomes real tags on the *second* decode,
/// so a single decode→strip pass leaves it as literal text that would later render as visible HTML.
let private clean (s: string) : string =
    (s |> decodeEntities |> stripTags |> decodeEntities |> stripTags).Trim()
let private truncate (n: int) (s: string) : string = if s.Length > n then s.[.. n - 1] else s

/// Wrap plain text as a TipTap document — the shape every other RichContent value in the DB has.
let private asRichText (text: string) : string =
    if text = "" then """{"type":"doc","content":[{"type":"paragraph"}]}"""
    else
        sprintf """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":%s}]}]}"""
            (JS.JSON.stringify (box text))

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

// ---- Reusable tail: feed (and a future push producer) converge here, so all durable-state
//      sanitizing lives in ingestEntries. ----

let private isHttp (url: string) = url.StartsWith("http://") || url.StartsWith("https://")

let ingestEntries (services: Services) (source: AlertSourceRow) (entries: FeedEntry list) : JS.Promise<int> =
    promise {
        let now = services.Now ()
        // clamp so a mis-parsed/absurd date can't skew promote order or display
        let clampPub (p: int) = if isFinite p && p > 0 && p <= now then p else now
        let valid = entries |> List.filter (fun e -> e.EntryKey <> "" && isHttp e.Link)
        if List.isEmpty valid then
            return 0
        else
            let stmts =
                valid
                |> List.map (fun e ->
                    bind (services.DB.prepare Sql.insertPendingPost)
                        [| box (services.NewId ()); box source.Id; box e.EntryKey
                           box e.Title; box e.Link; box e.Snippet; box (clampPub e.Published); box now |])
                |> List.toArray
            let! _ = services.DB.batch(stmts)
            return valid.Length
    }

// ---- Poll one source (blast-radius boundary), promote approved drafts ----

let pollSource (services: Services) (source: AlertSourceRow) : JS.Promise<unit> =
    promise {
        try
            // Google Alerts (the primary source) 500s the bare "hedge-alerts/1.0" agent; a
            // compatible-prefixed UA is accepted (still honestly identifies hedge-alerts).
            let opts = createObj [ "headers" ==> createObj [ "User-Agent" ==> "Mozilla/5.0 (compatible; hedge-alerts/1.0)" ] ]
            let! resp = fetchRaw source.FeedUrl opts
            let status = respStatus resp
            if status >= 400 then
                // Don't hand an error page to parseFeed (it would silently parse to 0 entries).
                logWarn (sprintf "alerts: feed HTTP %d (skipped): %s" status source.FeedUrl)
            else
                let! body = responseText resp
                if body.Length > 2_000_000 then
                    logWarn (sprintf "alerts: feed too large (%d bytes): %s" body.Length source.FeedUrl)
                else
                    let entries = parseFeed body
                    let! imported = ingestEntries services source entries
                    log (sprintf "alerts: topic=%s bytes=%d parsed=%d imported=%d" source.Topic body.Length entries.Length imported)
                    if entries.Length = 0 && body.Length > 500 then
                        logWarn (sprintf "alerts: non-empty body but 0 entries (feed drift?): %s" source.FeedUrl)
        with ex ->
            logError (sprintf "alerts: poll failed %s" source.FeedUrl) ex.Message
    }

let promoteApproved (services: Services) : JS.Promise<unit> =
    promise {
        let! result = (services.DB.prepare Sql.selectPromotable).all()
        for row in result.results do
            do! promise {
                try
                    let p = parsePendingPostRow row
                    let topic = rowStr row "topic"
                    // The host maps this onto its feed's create surface; alerts never names a content
                    // table. Snippet is plain → wrap; a written owner_comment is already rich-text,
                    // an unwritten one ('') becomes an empty doc so the feed's NOT NULL rich column is valid.
                    let input : PromotionInput =
                        { Title = p.Title
                          Link = p.Link
                          Extract = asRichText p.Snippet
                          OwnerComment = (if p.OwnerComment = "" then asRichText "" else p.OwnerComment)
                          ArticleDate = p.PublishedAt
                          Topic = topic }
                    let insertion = services.PromoteToFeed input
                    let promo =
                        bind (services.DB.prepare Sql.insertPromotion)
                            [| box (services.NewId ()); box p.EntryKey; box insertion.ItemId; box (services.Now ()) |]
                    // One batch: the feed insert(s) + the promotions row. The unique
                    // alerts_promotions.entry_key rolls back a concurrent double-promote at COMMIT.
                    let! _ = services.DB.batch(Array.append insertion.Stmts [| promo |])
                    return ()
                with ex ->
                    logError "alerts: promote failed" ex.Message
                    return ()
            }
    }

/// Cron entry point: poll enabled sources, then promote approved drafts. Wired by the host as the
/// createWorker `Scheduled` handler (idealist only).
let run (services: Services) (_ctx: ExecutionContext) : JS.Promise<unit> =
    promise {
        let! sourcesResult = (services.DB.prepare Sql.selectEnabledAlertSources).all()
        let sources = sourcesResult.results |> Array.map parseAlertSourceRow
        for s in sources do
            do! pollSource services s
        do! promoteApproved services
    }
