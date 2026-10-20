module Calendar.Vocab

// The closed vocabularies the stream uses. Stored as plain strings (Gen reflects strings into TEXT
// columns); these modules are the single source of the allowed values for the server's validation,
// the client's filters and editor, and anyone reading the admin.

/// Filter facets over the one entry stream. These are NOT separate calendars: the month grid is a
/// projection of the whole stream, and a facet merely hides or shows entries.
module Category =
    let sitewide = "sitewide"
    let community = "community"
    let art = "art"
    /// Owner-scoped: authored by a signed-in identity on the public site and visible only to it.
    let personal = "personal"
    let all = [ sitewide; community; art; personal ]
    let isPublic (c: string) = c <> personal
    let isValid (c: string) = List.contains c all
    /// Facets an owner may ask for on their own entry (sitewide is admin-authored only). The entry
    /// stays personal until an admin approves the request by setting its Category.
    let requestable = [ community; art ]
    let isRequestable (c: string) = List.contains c requestable

/// Recurrence of an entry, expanded by the projection (client) within the viewed window.
module Repeat =
    let none = "none"
    let daily = "daily"
    let weekly = "weekly"
    let fortnightly = "fortnightly"
    let monthly = "monthly"
    let yearly = "yearly"
    let all = [ none; daily; weekly; fortnightly; monthly; yearly ]
    let isValid (r: string) = List.contains r all
