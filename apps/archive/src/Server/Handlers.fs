module Server.Handlers

open Fable.Core
open Fable.Core.JsInterop
open Thoth.Json
open Hedge.Codec
open Hedge.Workers
open Hedge.Router
open Models.Api
open Server.Env
open Server.Db

/// id/title/section/date, shared by the front, section and search views.
let private toStub (row: obj) : ArticleStub =
    { Id = rowStr row "id"
      Title = rowStr row "title"
      Section = rowStr row "section"
      Teaser = ""
      Timestamp = rowInt row "article_date" }

/// Plain-text lead from the HTML body, for the front-page teasers.
let private deriveTeaser (html: string) : string =
    let noTags = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", " ")
    let unent =
        // Decode the entities the body uses; &amp; last so it can't re-form one.
        noTags.Replace("&nbsp;", " ")
              .Replace("&#39;", "'").Replace("&apos;", "'")
              .Replace("&quot;", "\"").Replace("&#34;", "\"")
              .Replace("&#8217;", "’").Replace("&rsquo;", "’").Replace("&#8216;", "‘").Replace("&lsquo;", "‘")
              .Replace("&#8220;", "“").Replace("&ldquo;", "“").Replace("&#8221;", "”").Replace("&rdquo;", "”")
              .Replace("&#8211;", "–").Replace("&ndash;", "–").Replace("&#8212;", "—").Replace("&mdash;", "—")
              .Replace("&#8230;", "…").Replace("&hellip;", "…")
              .Replace("&lt;", "<").Replace("&gt;", ">")
              .Replace("&amp;", "&")
    let collapsed = System.Text.RegularExpressions.Regex.Replace(unent, "\\s+", " ").Trim()
    if collapsed.Length <= 200 then collapsed else collapsed.[.. 199].TrimEnd() + "…"

let private toStubT (row: obj) : ArticleStub =
    { toStub row with Teaser = deriveTeaser (rowStr row "body") }

let private stubs (result: D1Result<obj>) =
    result.results |> Array.map toStub |> Array.toList

let private stubsT (result: D1Result<obj>) =
    result.results |> Array.map toStubT |> Array.toList

let private recentSection (env: Env) (section: string) (n: int) : JS.Promise<ArticleStub list> =
    promise {
        let! r = (bind (env.DB.prepare Sql.listSectionRecent) [| box section; box n |]).all()
        return stubsT r
    }

/// GET /api/front — lead column + per-section teasers + counts, one payload.
let getFront (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let! latest = (bind (env.DB.prepare Sql.listRecent) [| box 14 |]).all()
        let! waste = recentSection env "waste" 6
        let! uranium = recentSection env "uranium" 6
        let! ranger = recentSection env "ranger" 6
        let! rumjungle = recentSection env "rumjungle" 6
        let! intervention = recentSection env "intervention" 6
        let! countsR = (env.DB.prepare Sql.countsBySection).all()
        let counts =
            countsR.results
            |> Array.map (fun row -> ({ Section = rowStr row "section"; Count = rowInt row "n" } : SectionCount))
            |> Array.toList
        let resp : GetFront.Response =
            { Latest = stubsT latest
              Waste = waste
              Uranium = uranium
              Ranger = ranger
              Rumjungle = rumjungle
              Intervention = intervention
              Counts = counts }
        return okJson (encode resp |> Encode.toString 0)
    }

/// GET /api/section/:id — every article in the section (client groups by year).
let getSection (section: string) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let! r = (bind (env.DB.prepare Sql.listSectionAll) [| box section |]).all()
        let resp : GetSection.Response = { Section = section; Items = stubs r }
        return okJson (encode resp |> Encode.toString 0)
    }

/// GET /api/article/:id — the full article (original HTML body).
let getArticle (id: string) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let! r = (selectArticle id env.DB).all()
        if r.results.Length = 0 then
            return notFound ()
        else
            let a = parseArticleRow r.results.[0]
            let! _ = (bind (env.DB.prepare Sql.bumpViewCount) [| box id |]).run()
            let detail : GetArticle.ArticleDetail =
                { Id = a.Id
                  Aid = a.Aid
                  Title = a.Title
                  Body = a.Body
                  Section = a.Section
                  Attrib = a.Attrib
                  Source = a.Source
                  Timestamp = a.ArticleDate }
            let resp : GetArticle.Response = { Article = detail }
            return okJson (encode resp |> Encode.toString 0)
    }

/// Turn free text into a safe FTS5 MATCH: alnum terms only, each quoted, so
/// punctuation from the query can never break the match syntax (implicit AND).
let private ftsExpr (q: string) : string =
    q.Split([| ' '; '\t'; '\n'; '\r' |], System.StringSplitOptions.RemoveEmptyEntries)
    |> Array.map (fun t -> t |> String.filter System.Char.IsLetterOrDigit)
    |> Array.filter (fun t -> t <> "")
    |> Array.map (fun t -> "\"" + t + "\"")
    |> String.concat " "

/// GET /api/search/:id — full-text search over title + body.
let search (rawQ: string) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let q = JS.decodeURIComponent rawQ
        let expr = ftsExpr q
        if expr = "" then
            let resp : Search.Response = { Query = q; Items = [] }
            return okJson (encode resp |> Encode.toString 0)
        else
            let! r = (bind (env.DB.prepare Sql.searchFts) [| box expr; box 60 |]).all()
            let resp : Search.Response = { Query = q; Items = stubs r }
            return okJson (encode resp |> Encode.toString 0)
    }
