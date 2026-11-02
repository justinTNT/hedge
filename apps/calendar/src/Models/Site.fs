module Models.Site

// This site's policy constants, shared by the server (Services) and the client (the hosted
// component) so the two can never disagree. Compile-time, like a tenant's chrome: a deployment
// variant would select another value under a HEDGE_SITE condition, not an env var.

/// The facets this site shows on the shared grid, and which of them a signed-in user may ask an
/// admin to publish an entry as. `personal` is the module's own. Each public facet also needs a
/// `--cat` colour in public/styles.css (`.calendar-content .cat-<name>`).
let calendarFacets : Calendar.Vocab.Facets =
    Calendar.Vocab.Facets.define [ "sitewide"; "community"; "art" ] [ "community"; "art" ]
