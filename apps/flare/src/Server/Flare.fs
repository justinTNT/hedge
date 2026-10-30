module Server.Flare

// The flaring log, projected into the calendar stream: one read-only entry per flaring period, derived
// when a month is viewed from the flarecam2 Worker's /api/log (about 10 KB for a six-week grid, half a
// second). Nothing is stored here — the Worker's D1 is the record and the rule version is its, so a
// re-scored history or a period still running simply reads differently next time. The Worker's days are
// the camera's local dates; the module keys days by UTC midnight and never shifts them by the viewer's
// timezone, so a camera date maps straight onto a grid cell. A period that crosses midnight sits on the
// day it started, as the Worker's own log does.

open Fable.Core
open Fable.Core.JsInterop
open Thoth.Json
open Hedge.Workers
open Calendar.Api
open Server.Env

/// The facet the derived entries wear (named in Models.Site, coloured in public/styles.css).
let [<Literal>] facet = "flaring"

/// One flaring period as the Worker reports it (worker/src/intervals.ts). Times are camera-local,
/// "yyyy-mm-ddThh:mm:ss"; `StartKey` is the first on-frame's picture in the bucket.
type Period =
    { Start: string
      End: string
      StartKey: string
      Frames: int
      Peak: int
      Minutes: float }

let private period : Decoder<Period> =
    Decode.object (fun get ->
        { Start = get.Required.Field "start" Decode.string
          End = get.Required.Field "end" Decode.string
          StartKey = get.Required.Field "start_key" Decode.string
          Frames = get.Required.Field "frames" Decode.int
          Peak = get.Required.Field "peak" Decode.int
          Minutes = get.Required.Field "minutes" Decode.float })

let private log : Decoder<Period list> = Decode.field "periods" (Decode.list period)

// -- Days: epoch seconds <-> "yyyy-mm-dd", both at UTC midnight (the module's day key) --

[<Emit("new Date($0 * 1000).toISOString().slice(0, 10)")>]
let private isoDate (secs: int) : string = jsNative

[<Emit("Math.floor(Date.parse($0 + 'T00:00:00Z') / 1000)")>]
let private midnight (iso: string) : int = jsNative

let private dateOf (ts: string) = ts.Substring(0, 10)
let private timeOf (ts: string) = ts.Substring(11, 5)

// -- Words --

/// "2 h 12 min" / "5 min"; never "0 min" (a single on-frame is still a hundred seconds).
let private duration (minutes: float) =
    let m = int (System.Math.Round minutes)
    if m >= 60 then sprintf "%d h %02d min" (m / 60) (m % 60) else sprintf "%d min" (max m 1)

/// "04:10 to 06:20", or across midnight "10:49 to 06:36 next day" / "... on 2026-09-09".
let private span (p: Period) =
    let d0, d1 = dateOf p.Start, dateOf p.End
    if d0 = d1 then sprintf "%s to %s" (timeOf p.Start) (timeOf p.End)
    elif midnight d1 - midnight d0 = 86400 then sprintf "%s to %s next day" (timeOf p.Start) (timeOf p.End)
    else sprintf "%s to %s on %s" (timeOf p.Start) (timeOf p.End) d1

// -- The body: a rich-text document the module's viewer renders (TipTap JSON; plain HTML would show
//    as text). The first on-frame as an image, then the numbers and a link to the full-size picture. --

let private text (s: string) = Encode.object [ "type", Encode.string "text"; "text", Encode.string s ]
let private link (href: string) (s: string) =
    Encode.object [
        "type", Encode.string "text"
        "text", Encode.string s
        "marks", Encode.list [ Encode.object [ "type", Encode.string "link"; "attrs", Encode.object [ "href", Encode.string href ] ] ] ]
let private paragraph (inlines: JsonValue list) =
    Encode.object [ "type", Encode.string "paragraph"; "content", Encode.list inlines ]
let private image (src: string) (alt: string) =
    Encode.object [ "type", Encode.string "image"; "attrs", Encode.object [ "src", Encode.string src; "alt", Encode.string alt ] ]
let private doc (blocks: JsonValue list) =
    Encode.object [ "type", Encode.string "doc"; "content", Encode.list blocks ] |> Encode.toString 0

let private body (pictures: string) (p: Period) =
    let picture = pictures + p.StartKey
    doc [
        image picture (sprintf "The flare at %s" (timeOf p.Start))
        paragraph [
            text (sprintf "%d frames over %s, peak score %d. " p.Frames (duration p.Minutes) p.Peak)
            link picture "Open the picture" ]
    ]

/// A period as a calendar entry: dated on its start day, under the flaring facet, keyed by its start so the
/// id is stable across reads while a running period grows.
let entry (pictures: string) (p: Period) : GetEntries.EntryItem =
    { Id = "flare-" + p.Start
      Date = midnight (dateOf p.Start)
      Category = facet
      Summary = sprintf "%s, %s" (span p) (duration p.Minutes)
      Body = body pictures p
      Repeat = Calendar.Vocab.Repeat.none
      RepeatUntil = None
      Requested = None
      Mine = false }

/// The window's periods from the Worker, as entries. A failure fails the read (the module reports it in
/// the calendar's error banner) rather than quietly showing a month with no flaring in it. The Worker is
/// called over its service binding; the URL's host is nominal.
let derived (env: Env) : GetEntries.Query -> JS.Promise<GetEntries.EntryItem list> =
    fun query ->
        promise {
            let url = sprintf "https://flarecam2/api/log?from=%s&to=%s" (isoDate query.From) (isoDate query.To)
            let options = createObj [ "headers" ==> createObj [ "user-agent" ==> "flare-calendar/1" ] ]
            let! resp = env.FLARECAM.fetch (url, options)
            if not resp.ok then return failwithf "the flaring log answered %d" resp.status
            else
                let! json = responseText resp
                match Decode.fromString log json with
                | Ok periods -> return periods |> List.map (entry env.FLARECAM_PICTURES)
                | Error err -> return failwithf "the flaring log did not decode: %s" err
        }
