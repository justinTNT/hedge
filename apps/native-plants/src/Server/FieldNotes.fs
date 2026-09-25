module Server.FieldNotes

open Fable.Core
open Fable.Core.JsInterop
open Hedge.Workers
open Server.Contributions

/// Reviewer assertions are immutable and attributed separately from the author's
/// words. The conditional insert rejects revisions changed or withdrawn in flight.
let identify env request (body:Models.Api.IdentifyNote.Request) = promise {
    if not(validId body.Id) then invalid "Invalid note."
    checkedRevision body.Revision
    if not(List.contains body.Outcome ["confirmed";"alternative";"rejected";"unknown"]) then invalid "Choose an identification outcome."
    let text=checkedText "Response" 3000 body.Text
    let alternative=checkedText "Alternative species" 100 body.AlternativePlantId
    if body.Outcome="alternative" && not(validId alternative) then invalid "Choose an alternative species from the catalogue."
    if body.Outcome<>"alternative" && alternative<>"" then invalid "An alternative species belongs only to an alternative identification."
    let! provider,id,name=promise {
        if Server.AuthConfig.isOwner env request then return "admin","owner","Site owner"
        else
            let! who,_=owner env request
            match who with
            | None -> return invalid "A signed-in identifier is required."
            | Some who ->
                let! identity=first env "SELECT name FROM identities WHERE provider=? AND provider_user_id=? ORDER BY created_at LIMIT 1" (ownerArgs who)
                let name=identity |> Option.map(fun r->unbox<string> r?name) |> Option.filter(System.String.IsNullOrWhiteSpace >> not) |> Option.defaultValue "Identifier"
                return who.Provider,who.Id,name
    }
    return! run env ("INSERT OR IGNORE INTO identification_responses (id,note_id,note_revision,submitted_text,outcome,text,alternative_plant_id,reviewer_provider,reviewer_id,reviewer_name,created_at) SELECT ?,n.id,n.revision,n.text,?,?,?,?,?,?,? FROM plant_notes n WHERE n.id=? AND n.revision=? AND "+purposeSql+"='identification' AND n.deleted_at IS NULL AND EXISTS(SELECT 1 FROM plants p WHERE p.id=n.plant_id AND p.published=1 AND p.deleted_at IS NULL) AND (?='' OR EXISTS(SELECT 1 FROM plants p WHERE p.id=? AND p.id<>n.plant_id AND p.published=1 AND p.deleted_at IS NULL))")
        [|box(newId());box body.Outcome;box text;(if alternative="" then null else box alternative);box provider;box id;box name;box(epochNow());box body.Id;box body.Revision;box alternative;box alternative|]
}
