module Blog.Snapshots

// C4 — the blog module owns the complete source-snapshot feature (ItemSnapshot is Blog.Domain).
// Moved here from the app-local Server.Archive: capture (the POST /api/blog/snapshot handler),
// reference serving (GET /archive/<id>, mounted by the app), and the R2 private-key prefix. It
// runs on Blog.Services (no Server.Env), like the rest of the module's handlers.

open Fable.Core
open Fable.Core.JsInterop
open Hedge.Interface
open Hedge.Workers
open Hedge.Router
open Blog.Api
open Blog.Services

/// The private R2 key prefix under which captured snapshot HTML is stored. Never served through
/// the public /blobs/ route — the consuming app registers this in its BlobServingPolicy (C4
/// slice 1), and reference access goes only through `serveArchive` below.
let privatePrefix = "archive/"

/// Strip the executable/framework cruft from the captured DOM so we archive what was displayed,
/// not the JS bundle: remove <script>, module/script preloads, <noscript>, and <base>. Image
/// src's are deliberately LEFT as their original URLs — R2 holds only the post's chosen image;
/// the archived page is stored as-is and its images may rot. Runs through Cloudflare's
/// HTMLRewriter. Defense-in-depth: also strips inline on* handlers and javascript: URLs so the
/// bytes are inert even if some path served them without the CSP.
[<Emit("""(function(html){
  var res = new Response(html, { headers: { 'content-type': 'text/html; charset=utf-8' } });
  return new HTMLRewriter()
    .on('script', { element: function(e){ e.remove(); } })
    .on('link[rel="modulepreload"]', { element: function(e){ e.remove(); } })
    .on('link[rel="preload"][as="script"]', { element: function(e){ e.remove(); } })
    .on('noscript', { element: function(e){ e.remove(); } })
    .on('base', { element: function(e){ e.remove(); } })
    .on('*', { element: function(e){
      var drop = [];
      for (var a of e.attributes) {
        var n = a[0].toLowerCase();
        var v = (a[1] || '');
        if (n.indexOf('on') === 0) drop.push(a[0]);
        else if ((n === 'href' || n === 'src' || n === 'xlink:href') && /^\s*javascript:/i.test(v)) drop.push(a[0]);
      }
      for (var i = 0; i < drop.length; i++) e.removeAttribute(drop[i]);
    } })
    .transform(res).text();
})($0)""")>]
let private stripHtml (html: string) : JS.Promise<string> = jsNative

/// POST /api/blog/snapshot — the typed capture endpoint. Disabled hosts (services.CaptureEnabled
/// = false, e.g. Justat) return 404 and write nothing; enabled hosts require the admin key (the
/// extension sends X-Admin-Key), then strip the framework, store the page in R2 under
/// `privatePrefix`, record a blog_snapshots row, and return { id }. A snapshot failure is the
/// caller's to treat as best-effort — it never turns a published item into a failed submission.
let capture (req: SubmitSnapshot.Request) (request: WorkerRequest) (services: Services) : JS.Promise<WorkerResponse> =
    promise {
        if not services.CaptureEnabled then
            return notFound ()
        else
        let key = getHeader request "X-Admin-Key"
        if key = "" || key <> services.AdminKey then
            return unauthorized ()
        else
        let (ForeignKey itemId) = req.ItemId
        let html = req.Html
        let sourceUrl = req.SourceUrl |> Option.defaultValue ""
        if itemId = "" || html = "" then
            return badRequest "itemId and html are required"
        else
            try
                let! cleaned = stripHtml html
                let blobKey = sprintf "%s%s.html" privatePrefix (services.NewId ())
                let! _ = r2PutText services.Blobs blobKey cleaned "text/html; charset=utf-8"
                let ins =
                    Blog.Db.insertItemSnapshot services.DB
                        { ItemId = itemId; Kind = "html"; BlobKey = blobKey
                          SourceUrl = sourceUrl; Status = "ok"; Error = None }
                let! _ = ins.Stmt.run()
                return okJson (sprintf """{"id":"%s"}""" ins.Id)
            with ex ->
                return serverError ("snapshot failed: " + ex.Message)
    }

/// GET /archive/<snapshotId> — serve the stored snapshot HTML in a locked-down CSP sandbox so
/// foreign markup can never run in the app origin, EVEN when opened directly (not relying on a
/// reader iframe): a `sandbox` directive with no allow-* makes the origin opaque, and
/// script-src/base-uri/form-action 'none' + nosniff harden it further. Images/fonts/media load
/// by scheme (https:/data:), which stays valid in the opaque origin. Sanitization (stripHtml)
/// is additional, not the barrier. Mounted by the app only when capture is enabled.
let serveArchive (snapshotId: string) (services: Services) : JS.Promise<WorkerResponse> =
    promise {
        let! row = (Blog.Db.selectItemSnapshot snapshotId services.DB).first()
        if isNull (box row) then
            return jsonResponse """{"error":"Not found"}""" 404
        else
            let blobKey = rowStr row "blob_key"
            let! objOpt = services.Blobs.get blobKey
            match objOpt with
            | None -> return jsonResponse """{"error":"Not found"}""" 404
            | Some obj ->
                let options =
                    createObj [
                        "status" ==> 200
                        "headers" ==> createObj [
                            "Content-Type" ==> "text/html; charset=utf-8"
                            "X-Content-Type-Options" ==> "nosniff"
                            "Content-Security-Policy" ==> "sandbox; default-src 'none'; script-src 'none'; base-uri 'none'; form-action 'none'; img-src https: data:; style-src 'unsafe-inline'; font-src https: data:; media-src https: data:"
                        ]
                    ]
                return streamResponse obj.body options
    }
