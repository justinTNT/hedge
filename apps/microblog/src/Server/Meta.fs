/// Open Graph tags for social previews.
///
/// Crawlers (Facebook, Slack, iMessage, Twitter) don't run JavaScript, so they
/// only ever see the static SPA shell: the site title and an empty <div id="app">.
/// This module claims item URLs before the SPA fallback, looks the item up, and
/// injects per-item tags into the shell it serves.
///
/// App-level on purpose — unfurl metadata is policy, not framework protocol.
module Server.Meta

open Fable.Core
open Fable.Core.JsInterop
open Hedge.Workers
open Hedge.Router
open Server.Env

// ---- Cloudflare / JS primitives ----

[<Emit("$0.ASSETS.fetch($1)")>]
let private assetShell (env: obj) (request: WorkerRequest) : JS.Promise<WorkerResponse> = jsNative

/// Streaming rewrite of the shell: retitle, and append tags to <head>.
[<Emit("new HTMLRewriter().on('title', { element(e) { e.setInnerContent($1) } }).on('head', { element(e) { e.append($2, { html: true }) } }).transform($0)")>]
let private rewriteHead (response: WorkerResponse) (title: string) (headHtml: string) : WorkerResponse = jsNative

/// Titles routinely contain & and quotes; unescaped they break the head.
[<Emit("String($0).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/\"/g, '&quot;')")>]
let private esc (s: string) : string = jsNative

/// The prefix is stripped before dispatch, so the canonical URL has to be
/// rebuilt from the env or a sub-path deployment advertises URLs that 404.
[<Emit("$0.BASE_PATH || ''")>]
let private basePathOf (env: obj) : string = jsNative

[<Emit("new URL($0).origin")>]
let private originOf (url: string) : string = jsNative

/// Per-tenant without another config knob: one worker serves one host.
[<Emit("new URL($0).hostname")>]
let private hostOf (url: string) : string = jsNative

/// Extract is a TipTap document, not a string — walk it for text nodes so raw
/// JSON never reaches og:description. Falls back to the value as-is for rows
/// that predate rich content.
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

// ---- Tag building ----

let private truncate (n: int) (s: string) =
    if s.Length <= n then s else s.[.. n - 1].TrimEnd() + "…"

/// Paths the SPA owns that are not items. Mirrors Handlers.reservedSlugs, which
/// already refuses to mint a slug colliding with any of these.
let private reserved = set [ "tag"; "new"; "feed"; "api"; "blobs"; "public"; "admin" ]

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
          // Without this the card renders as a thumbnail strip, not a banner.
          yield """<meta name="twitter:card" content="summary_large_image">"""
      | None ->
          yield """<meta name="twitter:card" content="summary">"""
      yield sprintf """<meta name="twitter:title" content="%s">""" (esc title)
      if description <> "" then
          yield sprintf """<meta name="twitter:description" content="%s">""" (esc description) ]
    |> String.concat "\n    "

// ---- Route handling ----

/// Serve the SPA shell with per-item Open Graph tags, for a single-segment path
/// that resolves to an item. Returns None for everything else so the framework's
/// SPA fallback handles it unchanged — only item URLs pay for the lookup.
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
                let stmt = bind (env.DB.prepare Sql.itemMetaBySlugOrId) [| box idOrSlug; box idOrSlug |]
                let! result = stmt.all()
                let! shell = assetShell (box env) request
                if result.results.Length = 0 then
                    return shell
                else
                    let row = result.results.[0]
                    let title = rowStr row "title"
                    let description =
                        rowStrOpt row "extract"
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
