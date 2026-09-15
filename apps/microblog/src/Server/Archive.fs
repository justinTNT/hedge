/// Source-page archive: turn a captured rendered DOM into a cleaned, self-contained snapshot
/// stored in R2, and serve it back sandboxed. The capture itself happens in the browser
/// extension (it POSTs the already-rendered outerHTML + an image map); the server only strips
/// executable/framework cruft, rehosts remaining images into R2, stores the result, and records
/// a blog_snapshots row.
module Server.Archive

open Fable.Core
open Fable.Core.JsInterop
open Hedge.Workers
open Hedge.Router
open Server.Env

// -- JS helpers -------------------------------------------------------------------------------

/// Strip scripts/framework preloads/base + rewrite <img>/<source> src from a url->/blobs map.
/// Runs the captured HTML through Cloudflare's HTMLRewriter (the same engine Meta.fs uses).
[<Emit("""(function(html, map){
  var res = new Response(html, { headers: { 'content-type': 'text/html; charset=utf-8' } });
  var rewriteSrc = { element: function(e){ var s = e.getAttribute('src'); if (s && map[s]) e.setAttribute('src', map[s]); } };
  return new HTMLRewriter()
    .on('script', { element: function(e){ e.remove(); } })
    .on('link[rel="modulepreload"]', { element: function(e){ e.remove(); } })
    .on('link[rel="preload"][as="script"]', { element: function(e){ e.remove(); } })
    .on('noscript', { element: function(e){ e.remove(); } })
    .on('base', { element: function(e){ e.remove(); } })
    .on('img', rewriteSrc)
    .on('source', rewriteSrc)
    .transform(res).text();
})($0, $1)""")>]
let private stripAndRewrite (html: string) (urlMap: obj) : JS.Promise<string> = jsNative

/// Collect the http(s) <img>/<source> src URLs referenced in the HTML, deduped — so we know
/// which need rehosting. (HTMLRewriter does the actual rewrite; this is just discovery.)
[<Emit("""(function(html){
  var re = /<(?:img|source)\b[^>]*?\ssrc=["']([^"']+)["']/gi, m, out = [], seen = {};
  while ((m = re.exec(html)) !== null) {
    var u = m[1];
    if (u && u.indexOf('http') === 0 && !seen[u]) { seen[u] = 1; out.push(u); }
  }
  return out;
})($0)""")>]
let private collectImageUrls (html: string) : string[] = jsNative

/// map[key] or "" (guards a null/undefined map).
[<Emit("($0 && $0[$1]) || ''")>]
let private mapGet (o: obj) (k: string) : string = jsNative

[<Emit("$0[$1] = $2")>]
let private mapSet (o: obj) (k: string) (v: string) : unit = jsNative

// Cap server-side rehost fetches per capture to stay well under the Worker subrequest budget.
let private maxServerRehost = 30

// -- Pipeline ---------------------------------------------------------------------------------

/// Produce cleaned, self-contained HTML: images resolved to /blobs URLs (tier 1: the extension's
/// imageMap; tier 2: server rehost, capped; tier 3: keep the original URL), scripts/framework
/// stripped. Best-effort throughout — an image that can't be rehosted keeps its original URL.
let cleanAndRehost (blobs: R2Bucket) (html: string) (imageMap: obj) : JS.Promise<string> =
    promise {
        let map = createObj []
        let mutable rehosted = 0
        for url in collectImageUrls html do
            let fromExt = mapGet imageMap url
            if fromExt <> "" then
                mapSet map url fromExt                                   // tier 1: browser-captured
            elif rehosted < maxServerRehost then
                let! r = rehostRemoteImage blobs "archive-img" allowedImageTypes url
                if r <> url then
                    mapSet map url r                                     // tier 2: server rehost
                    rehosted <- rehosted + 1
            // else tier 3: leave the original URL (graceful — may rot)
        return! stripAndRewrite html map
    }

// -- Endpoints --------------------------------------------------------------------------------

/// POST /api/blog/snapshot (admin-gated) — body { itemId, sourceUrl, html, imageMap }.
/// Cleans + stores the snapshot and records a blog_snapshots row; returns { id, url }.
let handleSnapshot (request: WorkerRequest) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let key = getHeader request "X-Admin-Key"
        if key = "" || key <> env.ADMIN_KEY then
            return unauthorized ()
        else
            let! bodyText = request.text()
            let parsed = JS.JSON.parse bodyText
            let itemId : string = parsed?itemId
            let html : string = parsed?html
            let sourceUrl : string = let s : string = parsed?sourceUrl in if isNull (box s) then "" else s
            let imageMap : obj = parsed?imageMap
            if isNull (box itemId) || itemId = "" || isNull (box html) || html = "" then
                return badRequest "itemId and html are required"
            else
                try
                    let! cleaned = cleanAndRehost env.BLOBS html imageMap
                    let blobKey = sprintf "archive/%s.html" (newId ())
                    let! _ = r2PutText env.BLOBS blobKey cleaned "text/html; charset=utf-8"
                    let ins =
                        Blog.Db.insertItemSnapshot env.DB
                            { ItemId = itemId; Kind = "html"; BlobKey = blobKey
                              SourceUrl = sourceUrl; Status = "ok"; Error = None }
                    let! _ = ins.Stmt.run()
                    return okJson (sprintf """{"id":"%s","url":"/archive/%s"}""" ins.Id ins.Id)
                with ex ->
                    return serverError ("snapshot failed: " + ex.Message)
    }

/// GET /api/blog/item/<itemId>/snapshot — the latest snapshot id for an item (or null). Lets
/// the reader UI reveal an "Archived copy" link without bloating the item response/query.
let handleLatestSnapshot (itemId: string) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let! row = (Blog.Db.selectItemSnapshotsByItemId itemId env.DB).first()
        if isNull (box row) then
            return okJson """{"id":null}"""
        else
            return okJson (sprintf """{"id":"%s"}""" (rowStr row "id"))
    }

/// GET /archive/<snapshotId> — serve the stored snapshot HTML in a locked-down sandbox so
/// foreign markup can never run in the app origin (it could read the admin key otherwise).
let handleArchiveServe (snapshotId: string) (env: Env) : JS.Promise<WorkerResponse> =
    promise {
        let! row = (Blog.Db.selectItemSnapshot snapshotId env.DB).first()
        if isNull (box row) then
            return jsonResponse """{"error":"Not found"}""" 404
        else
            let blobKey = rowStr row "blob_key"
            let! objOpt = env.BLOBS.get blobKey
            match objOpt with
            | None -> return jsonResponse """{"error":"Not found"}""" 404
            | Some obj ->
                let options =
                    createObj [
                        "status" ==> 200
                        "headers" ==> createObj [
                            "Content-Type" ==> "text/html; charset=utf-8"
                            "X-Content-Type-Options" ==> "nosniff"
                            // No scripts (default-src 'none' + no script-src, and they're stripped
                            // anyway); images/fonts/media by scheme (https:/data:) rather than
                            // 'self', so they still load when the reader frames this under the
                            // iframe's opaque (sandboxed) origin. Origin isolation is the iframe's
                            // sandbox attribute (see the reader affordance), not a CSP sandbox
                            // directive — a CSP sandbox would also break scheme-'self' image loads.
                            "Content-Security-Policy" ==> "default-src 'none'; img-src https: data:; style-src 'unsafe-inline'; font-src https: data:; media-src https: data:"
                        ]
                    ]
                return streamResponse obj.body options
    }
