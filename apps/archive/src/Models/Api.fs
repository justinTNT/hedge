module Models.Api

open Hedge.Interface

/// Compact article stub for lists (front, section index, search) — no body.
/// Teaser is populated only on the front page (derived from the body); the
/// section index and search results leave it empty (they're title lists).
type ArticleStub = {
    Id: string
    Title: string
    Section: string
    Teaser: string
    Timestamp: int
}

/// Article count per section, for the front page / nav.
type SectionCount = {
    Section: string
    Count: int
}

module GetFront =
    /// The newspaper front: overall latest for the lead, plus the most recent
    /// few per section for the column teasers, plus per-section counts.
    type Response = {
        Latest: ArticleStub list
        Waste: ArticleStub list
        Uranium: ArticleStub list
        Ranger: ArticleStub list
        Rumjungle: ArticleStub list
        Intervention: ArticleStub list
        Counts: SectionCount list
    }
    let endpoint : Get<Response> = Get "/api/front"

module GetSection =
    /// Every article in a section (stub only). One shot — the client groups by
    /// year into the chronological index. Edge-cached; ~1.2k rows at most.
    type Response = {
        Section: string
        Items: ArticleStub list
    }
    let endpoint : GetBy<Response> = GetBy (sprintf "/api/section/%s")

module GetArticle =
    /// Full article for the detail page — the original archived HTML body.
    type ArticleDetail = {
        Id: string
        Aid: int
        Title: string
        Body: string
        Section: string
        Attrib: string
        Source: string
        Timestamp: int
    }
    type Response = { Article: ArticleDetail }
    // Path param carries the Mongo id (preserving old /article/<id> links).
    let endpoint : GetBy<Response> = GetBy (sprintf "/api/article/%s")

module Search =
    /// Full-text search over title + body (D1 FTS5). Query is a path param.
    type Response = {
        Query: string
        Items: ArticleStub list
    }
    let endpoint : GetBy<Response> = GetBy (sprintf "/api/search/%s")
