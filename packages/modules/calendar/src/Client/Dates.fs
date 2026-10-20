module Calendar.Client.Dates

// Day arithmetic for the projection. A `Day` is a whole number of days since the Unix epoch (UTC).
// Entry dates are epoch seconds at UTC midnight (the admin date picker and the editor both write
// them that way), so day = seconds / 86400 and the grid never shifts with the viewer's timezone.
// Labels go through Intl with the tenant locale (Hedge.Tenant.config.Locale; "" = the viewer's own).

open Fable.Core

type Day = int

let [<Literal>] SecondsPerDay = 86400

let dayOfSeconds (secs: int) : Day = int (System.Math.Floor (float secs / 86400.0))
let secondsOfDay (d: Day) : int = d * SecondsPerDay

[<Emit("new Date($0 * 86400000).getUTCFullYear()")>]
let yearOf (d: Day) : int = jsNative

/// 1 = January … 12 = December.
[<Emit("new Date($0 * 86400000).getUTCMonth() + 1")>]
let monthOf (d: Day) : int = jsNative

[<Emit("new Date($0 * 86400000).getUTCDate()")>]
let dayOfMonthOf (d: Day) : int = jsNative

/// 0 = Sunday … 6 = Saturday.
[<Emit("new Date($0 * 86400000).getUTCDay()")>]
let weekdayOf (d: Day) : int = jsNative

[<Emit("Math.floor(Date.UTC($0, $1 - 1, $2) / 86400000)")>]
let dayOf (year: int) (month: int) (dayOfMonth: int) : Day = jsNative

[<Emit("new Date(Date.UTC($0, $1, 0)).getUTCDate()")>]
let daysInMonth (year: int) (month: int) : int = jsNative

/// Today's calendar date in the viewer's own timezone, as a day key.
[<Emit("(function(){ var n = new Date(); return Math.floor(Date.UTC(n.getFullYear(), n.getMonth(), n.getDate()) / 86400000); })()")>]
let today () : Day = jsNative

[<Emit("new Date($0 * 86400000).toISOString().slice(0, 10)")>]
let isoOf (d: Day) : string = jsNative

[<Emit("(function(s){ var t = Date.parse(s + 'T00:00:00Z'); return isNaN(t) ? -1 : Math.floor(t / 86400000); })($0)")>]
let private parseIsoRaw (s: string) : int = jsNative

/// "yyyy-mm-dd" -> day key; None when unparseable (or before 1970).
let parseIso (s: string) : Day option =
    if isNull s || s = "" then None
    else
        let v = parseIsoRaw s
        if v < 0 then None else Some v

/// (year, month) shifted by n months.
let addMonths (year: int) (month: int) (n: int) : int * int =
    let idx = year * 12 + (month - 1) + n
    let y = int (System.Math.Floor (float idx / 12.0))
    y, idx - y * 12 + 1

/// The six-week grid (Monday-first) that frames a month: first and last day shown.
let gridRange (year: int) (month: int) : Day * Day =
    let first = dayOf year month 1
    let offset = (weekdayOf first + 6) % 7
    let start = first - offset
    start, start + 6 * 7 - 1

let pad2 (n: int) : string = if n < 10 then "0" + string n else string n

[<Emit("new Intl.DateTimeFormat($0 || undefined, { month: 'long', year: 'numeric', timeZone: 'UTC' }).format(new Date(Date.UTC($1, $2 - 1, 1)))")>]
let monthLabel (locale: string) (year: int) (month: int) : string = jsNative

[<Emit("new Intl.DateTimeFormat($0 || undefined, { weekday: 'short', timeZone: 'UTC' }).format(new Date($1 * 86400000))")>]
let weekdayLabel (locale: string) (d: Day) : string = jsNative

[<Emit("new Intl.DateTimeFormat($0 || undefined, { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC' }).format(new Date($1 * 86400000))")>]
let longDayLabel (locale: string) (d: Day) : string = jsNative
