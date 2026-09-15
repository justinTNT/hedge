/// Source-page archive: store the captured rendered DOM as a reference record. The browser
/// extension POSTs the already-rendered outerHTML; the server strips the executable/framework
/// cruft (so we keep what displayed, not the JS bundle) and stores the HTML in R2 with a
/// blog_snapshots row. Image URLs are left as-is (R2 holds only the post's own chosen image) —
/// the archive is a reference, never shown to readers, and may rot.
module Server.Archive

open Fable.Core
open Fable.Core.JsInterop
open Hedge.Workers
open Hedge.Router
open Server.Env

// -- Pipeline ---------------------------------------------------------------------------------

/// Strip the executable/framework cruft from the captured DOM so we archive what was displayed,
/// not the JS bundle: remove <script>, module/script preloads, <noscript>, and <base>. Image
/// src's are deliberately LEFT as their original URLs — R2 holds only the post's chosen image
/// (see Handlers.submitItem); the archived page is stored as-is, and its images may rot. Runs
/// through Cloudflare's HTMLRewriter (the same engine Meta.fs uses).
[<Emit("""(function(html){
  var res = new Response(html, { headers: { 'content-type': 'text/html; charset=utf-8' } });
  return new HTMLRewriter()
    .on('script', { element: function(e){ e.remove(); } })
    .on('link[rel="modulepreload"]', { element: function(e){ e.remove(); } })
    .on('link[rel="preload"][as="script"]', { element: function(e){ e.remove(); } })
    .on('noscript', { element: function(e){ e.remove(); } })
    .on('base', { element: function(e){ e.remove(); } })
    .transform(res).text();
})($0)""")>]
let private stripHtml (html: string) : JS.Promise<string> = jsNative

// -- Endpoints --------------------------------------------------------------------------------

/// POST /api/blog/snapshot (admin-gated) — body { itemId, sourceUrl, html }. Strips the framework
/// and stores the captured page in R2 (as-is otherwise — image URLs kept), recording a
/// blog_snapshots row. The archive is a reference record, not shown to readers; only the post's
/// chosen image is rehosted to R2 (see Handlers.submitItem). Returns { id }.
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
            if isNull (box itemId) || itemId = "" || isNull (box html) || html = "" then
                return badRequest "itemId and html are required"
            else
                try
                    let! cleaned = stripHtml html
                    let blobKey = sprintf "archive/%s.html" (newId ())
                    let! _ = r2PutText env.BLOBS blobKey cleaned "text/html; charset=utf-8"
                    let ins =
                        Blog.Db.insertItemSnapshot env.DB
                            { ItemId = itemId; Kind = "html"; BlobKey = blobKey
                              SourceUrl = sourceUrl; Status = "ok"; Error = None }
                    let! _ = ins.Stmt.run()
                    return okJson (sprintf """{"id":"%s"}""" ins.Id)
                with ex ->
                    return serverError ("snapshot failed: " + ex.Message)
    }

/// GET /archive/<snapshotId> — serve the stored snapshot HTML in a locked-down sandbox so foreign
/// markup can never run in the app origin. Reference access (e.g. via the admin snapshots table),
/// not a reader-facing feature.
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
