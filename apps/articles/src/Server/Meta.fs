/// Open Graph tags for social previews. Crawlers don't run JS, so they only see
/// the static SPA shell; this claims article URLs before the SPA fallback and
/// injects per-article tags. App-level on purpose — unfurl metadata is policy.
module Server.Meta

open Fable.Core
open Fable.Core.JsInterop
open Hedge.Workers
open Hedge.Router
open Server.Env

[<Emit("$0.ASSETS.fetch($1)")>]
let private assetShell (env: obj) (request: WorkerRequest) : JS.Promise<WorkerResponse> = jsNative

// Per-article OG tags. The static shell already carries site-level og:/twitter: (baked by
// vite for the homepage); strip those first, else they'd appear before these and win — Open
// Graph takes the first occurrence per property — leaving article shares showing the homepage
// title/url/logo. Removing + re-appending yields exactly one, article-specific, set.
[<Emit("new HTMLRewriter().on('title', { element(e) { e.setInnerContent($1) } }).on('meta[property^=\"og:\"]', { element(e) { e.remove() } }).on('meta[name^=\"twitter:\"]', { element(e) { e.remove() } }).on('head', { element(e) { e.append($2, { html: true }) } }).transform($0)")>]
let private rewriteHead (response: WorkerResponse) (title: string) (headHtml: string) : WorkerResponse = jsNative

[<Emit("String($0).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/\"/g, '&quot;')")>]
let private esc (s: string) : string = jsNative

[<Emit("$0.BASE_PATH || ''")>]
let private basePathOf (env: obj) : string = jsNative

[<Emit("new URL($0).origin")>]
let private originOf (url: string) : string = jsNative

[<Emit("new URL($0).hostname")>]
let private hostOf (url: string) : string = jsNative

/// Teaser is a TipTap document, not a string — walk it for text nodes so raw
/// JSON never reaches og:description. Falls back to the value as-is.
[<Emit("""(function (s) {
  try {
    return (function walk(n) {
      if (!n) return '';
      if (n.type === 'text') return n.text || '';
      if (Array.isArray(n.content)) return n.content.map(walk).join(' ');
      return '';
    })(JSON.parse(s)).replace(/\s+/g, ' ').trim();
  } catch (e) { return String(s).replace(/\s+/g, ' ').trim(); }
})($0)""")>]
let private plainText (s: string) : string = jsNative

let private truncate (n: int) (s: string) =
    if s.Length <= n then s else s.[.. n - 1].TrimEnd() + "…"

/// Paths the SPA owns that are not articles. "blog" is reserved so the unified shell's
/// blog mount (/blog[/*]) is never treated as an article slug — it falls through to the
/// single-page-application fallback, which the shell routes to the hosted blog module.
let private reserved = set [ "new"; "feed"; "api"; "blobs"; "public"; "admin"; "auth"; "blog" ]

let private metaTags (siteName: string) (title: string) (description: string) (image: string option) (url: string) =
    let tag prop content = sprintf """<meta property="%s" content="%s">""" prop (esc content)
    [ yield tag "og:type" "article"
      yield tag "og:site_name" siteName
      yield tag "og:title" title
      yield tag "og:url" url
      if description <> "" then yield tag "og:description" description
      match image with
      | Some src ->
          yield tag "og:image" src
          yield """<meta name="twitter:card" content="summary_large_image">"""
      | None ->
          yield """<meta name="twitter:card" content="summary">"""
      yield sprintf """<meta name="twitter:title" content="%s">""" (esc title)
      if description <> "" then
          yield sprintf """<meta name="twitter:description" content="%s">""" (esc description) ]
    |> String.concat "\n    "

/// Serve the SPA shell with per-article Open Graph tags for a single-segment
/// path that resolves to an article. None for everything else.
let handleRequest (request: WorkerRequest) (env: Env) : JS.Promise<WorkerResponse> option =
    match parseRoute request with
    | GET path ->
        let segments =
            path.Split('/')
            |> Array.filter (fun s -> s <> "")
            |> Array.toList
        match segments with
        | [ idOrSlug ] when not (Set.contains idOrSlug reserved) && not (idOrSlug.Contains ".") ->
            Some (promise {
                let stmt = bind (env.DB.prepare Articles.Sql.postMetaBySlugOrId) [| box idOrSlug; box idOrSlug |]
                let! result = stmt.all()
                let! shell = assetShell (box env) request
                if result.results.Length = 0 then
                    return shell
                else
                    let row = result.results.[0]
                    let title = rowStr row "title"
                    let description =
                        rowStrOpt row "teaser"
                        |> Option.map (plainText >> truncate 200)
                        |> Option.defaultValue ""
                    let image = rowStrOpt row "image"
                    let slug = rowStrOpt row "slug" |> Option.defaultValue (rowStr row "id")
                    let canonical =
                        sprintf "%s%s/%s" (originOf request.url) (basePathOf (box env)) slug
                    return rewriteHead shell title
                            (metaTags (hostOf request.url) title description image canonical)
            })
        | _ -> None
    | _ -> None
