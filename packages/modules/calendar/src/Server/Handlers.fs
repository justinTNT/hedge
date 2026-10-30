module Calendar.Handlers

// The calendar module's handlers: the windowed stream, and the signed-in user's own entries. Content
// only — the host resolves identity into Services.Viewer and runs the bounded-body preflight in front
// of the POSTs. Table names come via Calendar.Sql (Tables-driven), so the module never hardcodes one.

open Fable.Core
open Thoth.Json
open Hedge.Codec
open Hedge.Workers
open Hedge.Router
open Calendar.Codecs
open Calendar.Api
open Calendar.Vocab
open Calendar.Db
open Content.Server.Viewer
open Calendar.Services

let private respond (v: Viewer) (resp: WorkerResponse) = withReplacementCookie v.Replacement resp

let private toItem (viewerId: string option) (r: EntryRow) : GetEntries.EntryItem =
    { Id = r.Id
      Date = r.Date
      Category = r.Category
      Summary = r.Summary
      Body = r.Body
      Repeat = r.Repeat
      RepeatUntil = r.RepeatUntil
      Requested = r.Requested
      Mine = (match viewerId with Some v -> r.IdentityId = Some v | None -> false) }

// ============================================================
// The stream
// ============================================================

/// A month grid spans at most 6 weeks; allow a little slack, never an open-ended scan.
let [<Literal>] private MaxWindowSeconds = 70 * 86400

/// GET /api/calendar/entries?from=&to= — the windowed stream (public + the viewer's own entries), plus
/// whatever the host derives for the window (Services.Derived).
let getEntries (query: GetEntries.Query) (request: WorkerRequest) (services: Services)
    : JS.Promise<WorkerResponse> =
    promise {
        if query.From < 0 || query.To < query.From || query.To - query.From > MaxWindowSeconds then
            return badRequest "window must run forward and span at most 70 days"
        else
            let! v = services.Viewer request
            let stmt =
                match v.IdentityId with
                | Some id ->
                    bind (services.DB.prepare Sql.visibleEntries)
                         [| box id; box query.From; box query.To; box query.To; box query.From |]
                | None ->
                    bind (services.DB.prepare Sql.publicEntries)
                         [| box query.From; box query.To; box query.To; box query.From |]
            let! r = stmt.all()
            let! derived = services.Derived query
            let items = (r.results |> Array.map (parseEntryRow >> toItem v.IdentityId) |> Array.toList) @ derived
            let resp : GetEntries.Response = { Entries = items; SignedIn = v.IdentityId.IsSome }
            return respond v (okJson (encode resp |> Encode.toString 0))
    }

// ============================================================
// The signed-in user's own entries
// ============================================================

let [<Literal>] private MaxSummaryChars = 200
let [<Literal>] private MaxBodyChars = 60000

/// The message for a request the site doesn't accept: name the facets it does take, or say it takes none.
let private requestError (facets: Facets) : string =
    if facets.Requestable.IsEmpty then "This site doesn't take requests to publish entries."
    else sprintf "You can ask for %s; the other facets are the site's own." (String.concat " or " facets.Requestable)

let private validateSave (facets: Facets) (req: SaveEntry.Request) : Result<SaveEntry.Request, string> =
    let summary = req.Summary.Trim()
    if summary = "" then Error "A summary is required."
    elif summary.Length > MaxSummaryChars then Error "The summary is too long (200 characters max)."
    elif req.Date <= 0 then Error "A date is required."
    elif not (Repeat.isValid req.Repeat) then Error "Unknown repeat rule."
    elif (match req.RepeatUntil with Some u -> u < req.Date | None -> false) then
        Error "The repeat-until date can't be before the entry's date."
    elif req.Body.Length > MaxBodyChars then Error "The body is too long."
    elif (match req.Requested with Some f when f <> "" -> not (Facets.isRequestable facets f) | _ -> false) then
        Error (requestError facets)
    else
        Ok { req with
                Summary = summary
                RepeatUntil = (if req.Repeat = Repeat.none then None else req.RepeatUntil)
                Requested = (match req.Requested with Some f when f <> "" -> Some f | _ -> None) }

/// Re-read a live own entry after a write (ownership-checked), as the response shape.
let private ownEntryResponse (services: Services) (v: Viewer) (owner: string) (id: string) : JS.Promise<WorkerResponse> =
    promise {
        let! row = (bind (services.DB.prepare Sql.ownEntry) [| box id; box owner |]).first()
        if isNull (box row) then return respond v (notFound ())
        else
            let resp : SaveEntry.Response = { Entry = toItem (Some owner) (parseEntryRow row) }
            return respond v (okJson (encode resp |> Encode.toString 0))
    }

/// POST /api/calendar/entry — create or update one of the signed-in identity's entries. Always lands
/// as personal; `Requested` asks an admin to publish it under community | art.
let saveEntry (req: SaveEntry.Request) (request: WorkerRequest) (services: Services)
    : JS.Promise<WorkerResponse> =
    promise {
        let! v = services.Viewer request
        match v.IdentityId with
        | None -> return respond v (unauthorized ())
        | Some owner ->
            match validateSave services.Facets req with
            | Error msg -> return respond v (badRequest msg)
            | Ok req ->
                let now = services.Now ()
                let until = optIntToDb req.RepeatUntil
                let requested = optToDb req.Requested
                match req.Id with
                | Some id ->
                    // Ownership first: an update of someone else's (or a deleted) entry is a 404, never a write.
                    let! existing = (bind (services.DB.prepare Sql.ownEntry) [| box id; box owner |]).first()
                    if isNull (box existing) then return respond v (notFound ())
                    else
                        let! _ =
                            (bind (services.DB.prepare Sql.updateOwn)
                                  [| box req.Date; box req.Summary; box req.Body; box req.Repeat; until; requested; box now; box id; box owner |]).run()
                        return! ownEntryResponse services v owner id
                | None ->
                    let id = services.NewId ()
                    let! _ =
                        (bind (services.DB.prepare Sql.insertPersonal)
                              [| box id; box req.Date; box req.Summary; box req.Body; box req.Repeat; until; requested; box owner; box now |]).run()
                    return! ownEntryResponse services v owner id
    }

/// POST /api/calendar/entry/delete — soft-delete one of the signed-in identity's own entries.
let deleteEntry (req: DeleteEntry.Request) (request: WorkerRequest) (services: Services)
    : JS.Promise<WorkerResponse> =
    promise {
        let! v = services.Viewer request
        match v.IdentityId with
        | None -> return respond v (unauthorized ())
        | Some owner ->
            let! existing = (bind (services.DB.prepare Sql.ownEntry) [| box req.Id; box owner |]).first()
            if isNull (box existing) then return respond v (notFound ())
            else
                let! _ = (bind (services.DB.prepare Sql.deleteOwn) [| box (services.Now ()); box req.Id; box owner |]).run()
                let resp : DeleteEntry.Response = { Ok = true }
                return respond v (okJson (encode resp |> Encode.toString 0))
    }
