module Calendar.Vocab

// The module's vocabularies. `personal` is the ONE facet the module reserves (an entry private to the
// identity that wrote it); every public facet is the host's to name — a site's filter chips are its
// own words (sitewide/community/art on the reference host; a club might say fixtures/social). The
// repeat rules are the module's: the projection expands them.

/// The reserved facet: private to its owner, never on the public grid.
module Category =
    let personal = "personal"

/// A site's facet vocabulary. `Public` names the facets on the shared grid (the filter chips, and
/// what an admin may publish an entry as); `Requestable` is the subset a signed-in user may ask for
/// from the editor (an approval-queue request). Facet names double as CSS class suffixes
/// (`cat-<name>`): the host's stylesheet gives each a `--cat` colour.
type Facets = { Public: string list; Requestable: string list }

module Facets =
    /// A facet name is a CSS class suffix and an API value: lowercase letters, digits and hyphens,
    /// starting with a letter.
    let isName (s: string) =
        not (isNull s) && s.Length > 0
        && (s.[0] >= 'a' && s.[0] <= 'z')
        && (s |> Seq.forall (fun c -> (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c = '-'))

    /// Define a site's facets, failing FAST (at module load, server and client alike) on a vocabulary
    /// the module can't honour: a malformed or duplicate name, the reserved `personal`, or a
    /// requestable facet that isn't public.
    let define (publicFacets: string list) (requestable: string list) : Facets =
        let bad = publicFacets @ requestable |> List.filter (isName >> not)
        if not bad.IsEmpty then failwithf "calendar facets: invalid name(s) %A" bad
        if List.contains Category.personal publicFacets then
            failwithf "calendar facets: '%s' is reserved by the module" Category.personal
        if List.length (List.distinct publicFacets) <> List.length publicFacets then
            failwith "calendar facets: duplicate public facet"
        let orphan = requestable |> List.filter (fun r -> not (List.contains r publicFacets))
        if not orphan.IsEmpty then failwithf "calendar facets: requestable %A must be public" orphan
        { Public = publicFacets; Requestable = requestable }

    let isRequestable (f: Facets) (name: string) = List.contains name f.Requestable

module Repeat =
    let none = "none"
    let daily = "daily"
    let weekly = "weekly"
    let fortnightly = "fortnightly"
    let monthly = "monthly"
    let yearly = "yearly"
    let all = [ none; daily; weekly; fortnightly; monthly; yearly ]
    let isValid (s: string) = List.contains s all
