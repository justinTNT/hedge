module Calendar.Client.Projection

// The calendar as a projection of the stream: expand each entry's repeat rule into the days it
// occurs on within a window, drop hidden facets, and bucket by day. Pure; no DOM.

open Calendar.Api
open Calendar.Vocab
open Calendar.Client.Dates

type Occurrence = { Entry: GetEntries.EntryItem; Day: Day }

/// Every day in [first, last] on which `entry` occurs.
let occurrences (first: Day) (last: Day) (entry: GetEntries.EntryItem) : Day list =
    let start = dayOfSeconds entry.Date
    let until =
        match entry.RepeatUntil with
        | Some u -> min last (dayOfSeconds u)
        | None -> last
    if start > last || until < first then []
    else
        let r = entry.Repeat
        if r = Repeat.none then
            (if start >= first && start <= last then [ start ] else [])
        elif r = Repeat.daily || r = Repeat.weekly || r = Repeat.fortnightly then
            let step = if r = Repeat.daily then 1 elif r = Repeat.weekly then 7 else 14
            // The first occurrence at or after the window start, on the entry's own cadence.
            let k = if first > start then (first - start + step - 1) / step else 0
            [ for d in start + k * step .. step .. until -> d ]
        elif r = Repeat.monthly then
            let dom = dayOfMonthOf start
            let lo = max start first
            let y0, m0 = yearOf lo, monthOf lo
            let y1, m1 = yearOf until, monthOf until
            [ for idx in y0 * 12 + (m0 - 1) .. y1 * 12 + (m1 - 1) do
                let y, m = idx / 12, idx % 12 + 1
                // Months without that day (the 31st, the 30th in February) are skipped, not clamped.
                if dom <= daysInMonth y m then
                    let d = dayOf y m dom
                    if d >= lo && d <= until then yield d ]
        elif r = Repeat.yearly then
            let dom, mon = dayOfMonthOf start, monthOf start
            let lo = max start first
            [ for y in yearOf lo .. yearOf until do
                if dom <= daysInMonth y mon then
                    let d = dayOf y mon dom
                    if d >= lo && d <= until then yield d ]
        else []

/// Whether an entry falls on one day, by its repeat rule.
let occursOn (day: Day) (entry: GetEntries.EntryItem) : bool =
    not (List.isEmpty (occurrences day day entry))

/// Project the stream onto [first, last]: hidden facets removed, recurrences expanded, bucketed by
/// day in stream order (the server orders by date, then creation).
let project (first: Day) (last: Day) (hidden: Set<string>) (entries: GetEntries.EntryItem list)
    : Map<Day, Occurrence list> =
    entries
    |> List.filter (fun e -> not (hidden.Contains e.Category))
    |> List.collect (fun e -> occurrences first last e |> List.map (fun d -> { Entry = e; Day = d }))
    |> List.groupBy (fun o -> o.Day)
    |> Map.ofList
