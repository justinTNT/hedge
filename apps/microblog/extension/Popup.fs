module Extension.Popup

open Fable.Core
open Fable.Core.JsInterop
open Browser.Dom
open Browser.Types
open HedgeExtension
open HedgeExtension.TipTap
open Blog.Api
open Client.ClientGen
open Codecs

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------

let mutable extractEditor: Editor option = None
let mutable commentEditor: Editor option = None
let mutable selectedImage: string option = None
let mutable pageUrl = ""
let mutable documentHtml = ""

type Site = { Name: string; Url: string; Key: string }

let mutable sites: Site list = []
let mutable activeSiteIndex = 0
let mutable showConfig = false

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

let el (id: string) = document.getElementById id
let elAs<'T when 'T :> HTMLElement> (id: string) = document.getElementById id :?> 'T

// ---------------------------------------------------------------------------
// TipTap toolbar factory
// ---------------------------------------------------------------------------

type ToolbarButton =
    | Separator
    | Button of label: string * title: string * cmd: (unit -> unit) * active: (unit -> bool)

let createToolbar (editor: Editor) (toolbarEl: HTMLElement) =
    let buttons = [
        Button("B", "Bold", (fun () -> toggleBold editor), fun () -> isActive editor "bold")
        Button("I", "Italic", (fun () -> toggleItalic editor), fun () -> isActive editor "italic")
        Button("{}", "Code", (fun () -> toggleCode editor), fun () -> isActive editor "code")
        Separator
        Button("H2", "Heading", (fun () -> toggleHeading editor), fun () -> isActiveHeading editor)
        Button("\"", "Quote", (fun () -> toggleBlockquote editor), fun () -> isActive editor "blockquote")
        Button("•", "List", (fun () -> toggleBulletList editor), fun () -> isActive editor "bulletList")
        Separator
        Button("🔗", "Link", (fun () ->
            let url = window.prompt "URL:"
            if not (isNull url) then
                if url = "" then unsetLink editor
                else setLink editor url
        ), fun () -> isActive editor "link")
    ]

    let btnEls = ResizeArray<HTMLElement * (unit -> bool)>()

    for btn in buttons do
        match btn with
        | Separator ->
            let sep = document.createElement "div"
            sep.className <- "sep"
            toolbarEl.appendChild sep |> ignore
        | Button(label, title, cmd, active) ->
            let b = document.createElement "button" :?> HTMLButtonElement
            b.``type`` <- "button"
            b.textContent <- label
            b.title <- title
            b.addEventListener("click", fun (e: Event) ->
                e.preventDefault()
                cmd()
            )
            toolbarEl.appendChild b |> ignore
            btnEls.Add(b :> HTMLElement, active)

    let update () =
        for (b, active) in btnEls do
            if active() then b.classList.add "active"
            else b.classList.remove "active"

    on editor "selectionUpdate" update
    on editor "transaction" update

let createEditorInEl (contentEl: HTMLElement) (toolbarEl: HTMLElement) (initialContent: obj option) =
    let content = initialContent |> Option.defaultValue (createObj [ "type" ==> "doc"; "content" ==> [| createObj [ "type" ==> "paragraph" ] |] ])
    let editor = createEditor contentEl content
    createToolbar editor toolbarEl
    editor

// ---------------------------------------------------------------------------
// Page data extraction
// ---------------------------------------------------------------------------

type PageData = {
    Title: string
    Url: string
    SelectionHtml: string
    SelectionText: string
    Images: string array
    DocumentHtml: string
}

// Content script that runs in the page context — must be plain JS
[<Emit("""
chrome.scripting.executeScript({
  target: { tabId: $0 },
  func: function() {
    var sel = window.getSelection();
    var selectionHtml = '';
    var selectionText = '';
    if (sel && sel.rangeCount > 0 && !sel.isCollapsed) {
      selectionText = sel.toString();
      var container = document.createElement('div');
      for (var i = 0; i < sel.rangeCount; i++) {
        container.appendChild(sel.getRangeAt(i).cloneContents());
      }
      selectionHtml = container.innerHTML;
    }
    // Prefer the largest variant a responsive image actually offers: the rendered
    // .src/.currentSrc is only the size the browser chose for this viewport (often a
    // thumbnail), while srcset lists the full-res URLs -- including the <source>
    // siblings of a <picture>. Fall back to .src when there's no srcset. These are
    // real URLs the page serves, so no 403/404 from synthesizing a size.
    function bestSrc(img) {
      var best = img.currentSrc || img.src || '';
      var bestW = 0;
      var srcsets = [];
      if (img.srcset) srcsets.push(img.srcset);
      var p = img.parentElement;
      if (p && p.tagName === 'PICTURE') {
        Array.from(p.querySelectorAll('source')).forEach(function(s) { if (s.srcset) srcsets.push(s.srcset); });
      }
      // Parse srcset per the HTML grammar rather than a naive comma split: a URL is a
      // maximal run of non-whitespace (so commas INSIDE it, e.g. Cloudinary
      // /c_fill,w_1600/story.jpg, are preserved); a trailing comma with no descriptor
      // is a candidate separator; otherwise the descriptor runs up to the next comma.
      srcsets.forEach(function(ss) {
        var i = 0, n = ss.length;
        while (i < n) {
          while (i < n && /[\s,]/.test(ss[i])) i++;
          if (i >= n) break;
          var s = i;
          while (i < n && !/\s/.test(ss[i])) i++;
          var url = ss.slice(s, i);
          var w = 0;
          if (url.slice(-1) === ',') {
            url = url.replace(/,+$/, '');
          } else {
            while (i < n && /\s/.test(ss[i])) i++;
            var ds = i;
            while (i < n && ss[i] !== ',') i++;
            var desc = ss.slice(ds, i).trim();
            if (desc.slice(-1) === 'w') w = parseInt(desc, 10) || 0;
          }
          if (url && w > bestW) { bestW = w; best = url; }
        }
      });
      try { best = new URL(best, document.baseURI).href; } catch (e) {}
      return best;
    }
    // The page's designated share image is usually the full-size lead -- offer it first.
    var ogImg = '';
    var ogMeta = document.querySelector('meta[property="og:image"], meta[name="og:image"], meta[name="twitter:image"]');
    if (ogMeta && ogMeta.content) { try { ogImg = new URL(ogMeta.content, document.baseURI).href; } catch (e) { ogImg = ogMeta.content; } }
    var images = (ogImg ? [ogImg] : []).concat(Array.from(document.images).map(bestSrc))
      .filter(function(src) { return src && src.indexOf('http') === 0; })
      .filter(function(src, i, arr) { return arr.indexOf(src) === i; })
      .slice(0, 50);
    // The already-rendered DOM (post-JS) for the archive snapshot — what displayed now.
    var documentHtml = document.documentElement ? document.documentElement.outerHTML : '';
    return { selectionHtml: selectionHtml, selectionText: selectionText, images: images, documentHtml: documentHtml };
  }
})
""")>]
let private executeContentScript (tabId: int) : JS.Promise<obj array> = jsNative

let extractPageData () : JS.Promise<PageData> =
    promise {
        let! tabs = Chrome.queryActiveTab ()
        if tabs.Length = 0 then
            return { Title = ""; Url = ""; SelectionHtml = ""; SelectionText = ""; Images = [||]; DocumentHtml = "" }
        else
            let tab = tabs.[0]
            let tabUrl: string = if isNullOrUndefined tab?url then "" else string tab?url
            let tabTitle: string = if isNullOrUndefined tab?title then "" else string tab?title
            let tabId: int = tab?id

            pageUrl <- tabUrl

            let! results =
                promise {
                    try
                        let! r = executeContentScript tabId
                        return Some r
                    with _ ->
                        return None
                }

            match results with
            | None ->
                return { Title = tabTitle; Url = tabUrl; SelectionHtml = ""; SelectionText = ""; Images = [||]; DocumentHtml = "" }
            | Some r ->
                let data = if r.Length > 0 then r.[0]?result else null
                if isNullOrUndefined data then
                    return { Title = tabTitle; Url = tabUrl; SelectionHtml = ""; SelectionText = ""; Images = [||]; DocumentHtml = "" }
                else
                    let sh: string = if isNullOrUndefined data?selectionHtml then "" else string data?selectionHtml
                    let st: string = if isNullOrUndefined data?selectionText then "" else string data?selectionText
                    let imgs: string array = if isNullOrUndefined data?images then [||] else data?images
                    let dh: string = if isNullOrUndefined data?documentHtml then "" else string data?documentHtml
                    return {
                        Title = tabTitle
                        Url = tabUrl
                        SelectionHtml = sh
                        SelectionText = st
                        Images = imgs
                        DocumentHtml = dh
                    }
    }

// ---------------------------------------------------------------------------
// Convert HTML selection to TipTap JSON
// ---------------------------------------------------------------------------

let htmlToTipTapJson (html: string) : obj option =
    if System.String.IsNullOrEmpty html then None
    else
        let tempEl = document.createElement "div"
        tempEl?style?display <- "none"
        document.body.appendChild tempEl |> ignore
        let tempEditor = createEditor (unbox<HTMLElement> tempEl) (box "")
        setContent tempEditor html
        let json = getJSON tempEditor
        destroy tempEditor
        tempEl?remove()
        Some json

// ---------------------------------------------------------------------------
// Images gallery
// ---------------------------------------------------------------------------

let renderImages (images: string array) =
    let gallery = el "imagesGallery"
    gallery.innerHTML <- ""
    selectedImage <- None

    for src in images do
        let img = document.createElement "img" :?> HTMLImageElement
        img.className <- "img-thumb"
        img.src <- src
        img.title <- src
        img.addEventListener("click", fun _ ->
            if selectedImage = Some src then
                selectedImage <- None
                img.classList.remove "selected"
            else
                selectedImage <- Some src
                let nodes = gallery.querySelectorAll(".img-thumb")
                for j in 0 .. int nodes.length - 1 do
                    (nodes.[j] :?> HTMLElement).classList.remove "selected"
                img.classList.add "selected"
        )
        img.addEventListener("error", fun _ -> img.remove())
        gallery.appendChild img |> ignore

// ---------------------------------------------------------------------------
// Status display
// ---------------------------------------------------------------------------

let setStatus (text: string) (typ: string) =
    let status = el "status"
    status.textContent <- text
    status.className <- "status" + (if typ <> "" then " " + typ else "")

// ---------------------------------------------------------------------------
// Site config UI
// ---------------------------------------------------------------------------

let saveSites () : JS.Promise<unit> =
    promise {
        let sitesJs = sites |> List.map (fun s -> createObj [ "name" ==> s.Name; "url" ==> s.Url; "key" ==> s.Key ]) |> List.toArray
        let! _ = Chrome.sendMessage (createObj [ "type" ==> "setSites"; "sites" ==> sitesJs; "activeSiteIndex" ==> activeSiteIndex ])
        return ()
    }

let rec renderSiteDropdown () =
    let select = elAs<HTMLSelectElement> "siteSelect"
    select.innerHTML <- ""

    if sites.IsEmpty then
        let opt = document.createElement "option" :?> HTMLOptionElement
        opt.textContent <- "(no sites configured)"
        opt.disabled <- true
        select.appendChild opt |> ignore
    else
        for i in 0 .. sites.Length - 1 do
            let site = sites.[i]
            let opt = document.createElement "option" :?> HTMLOptionElement
            opt.value <- string i
            opt.textContent <- if site.Name <> "" then site.Name else site.Url
            if i = activeSiteIndex then opt.selected <- true
            select.appendChild opt |> ignore

and renderConfigTable () =
    let tbody = el "configBody"
    tbody.innerHTML <- ""

    for i in 0 .. sites.Length - 1 do
        let site = sites.[i]
        let tr = document.createElement "tr"

        let tdName = document.createElement "td"
        tdName.textContent <- if site.Name <> "" then site.Name else "—"
        tr.appendChild tdName |> ignore

        let tdUrl = document.createElement "td"
        tdUrl?style?fontFamily <- "monospace"
        tdUrl.textContent <- site.Url
        tr.appendChild tdUrl |> ignore

        let tdKey = document.createElement "td"
        tdKey?style?fontFamily <- "monospace"
        tdKey.textContent <- if site.Key <> "" then "••••" else "—"
        tr.appendChild tdKey |> ignore

        let tdActions = document.createElement "td"
        tdActions.className <- "actions-cell"

        let adminBtn = document.createElement "button" :?> HTMLButtonElement
        adminBtn.className <- "btn-sm btn-admin"
        adminBtn.textContent <- "Admin"
        adminBtn.addEventListener("click", fun _ ->
            let adminUrl = site.Url.TrimEnd('/') + "/admin.html#key=" + JS.encodeURIComponent site.Key
            window.``open``(adminUrl, "_blank") |> ignore
        )
        tdActions.appendChild adminBtn |> ignore

        let delBtn = document.createElement "button" :?> HTMLButtonElement
        delBtn.className <- "btn-sm btn-del"
        delBtn.textContent <- "Del"
        delBtn?style?marginLeft <- "4px"
        let idx = i
        delBtn.addEventListener("click", fun _ ->
            sites <- sites |> List.indexed |> List.filter (fun (j, _) -> j <> idx) |> List.map snd
            if activeSiteIndex >= sites.Length then
                activeSiteIndex <- max 0 (sites.Length - 1)
            saveSites () |> ignore
            renderSiteDropdown ()
            renderConfigTable ()
        )
        tdActions.appendChild delBtn |> ignore

        tr.appendChild tdActions |> ignore
        tbody.appendChild tr |> ignore

let toggleConfig () =
    showConfig <- not showConfig
    let section = el "configSection"
    if showConfig then section.classList.remove "hidden"
    else section.classList.add "hidden"
    if showConfig then renderConfigTable ()

// ---------------------------------------------------------------------------
// Image capture (hybrid tier 1)
// ---------------------------------------------------------------------------

/// Ask the background worker to rehost the chosen image to R2 (it holds the
/// host permission needed to read cross-origin image bytes). Best-effort:
/// returns None on any failure so submit falls back to the original URL, which
/// the server then tries to rehost itself (tier 2) or keeps as-is (tier 3).
let captureImageToBlob (dest: Client.Api.Destination) (url: string) : JS.Promise<string option> =
    promise {
        try
            let site = createObj [ "url" ==> dest.Url; "key" ==> dest.Key ]
            let! raw = Chrome.sendMessage (createObj [ "type" ==> "captureImage"; "url" ==> url; "site" ==> site ])
            let ok: bool = raw?ok
            if ok then
                let data = raw?data
                let blobUrl: string = if isNullOrUndefined data?url then "" else string data?url
                return if blobUrl <> "" then Some blobUrl else None
            else
                return None
        with _ ->
            return None
    }

// ---------------------------------------------------------------------------
// Submit
// ---------------------------------------------------------------------------

let submit () : JS.Promise<unit> =
    promise {
        let title = (elAs<HTMLInputElement> "title").value.Trim()
        if title = "" then
            setStatus "Title is required" "error"
        else

        match commentEditor with
        | None ->
            setStatus "Editor not ready" "error"
        | Some editor ->

        let commentText = getText(editor).Trim()
        if commentText = "" then
            setStatus "Comment is required" "error"
        else

        let commentJson = getJSON editor
        let extractJson =
            match extractEditor with
            | Some ext ->
                let t = getText(ext).Trim()
                if t <> "" then Some (getJSON ext) else None
            | None -> None

        let slugRaw = (elAs<HTMLInputElement> "slug").value.Trim()
        let tagsRaw = (elAs<HTMLInputElement> "tags").value
        let tags = tagsRaw.Split(',') |> Array.map (fun t -> t.Trim()) |> Array.filter (fun t -> t <> "") |> Array.toList

        let btn = elAs<HTMLButtonElement> "submitBtn"
        // Pin the destination for the WHOLE submission. Image capture, the item POST, and
        // the archive POST each otherwise resolve the active site independently in the
        // background — so changing the active site mid-submit (switch, or even delete via
        // settings) would upload the image to one tenant and post it from another (a broken
        // relative /blobs URL). Resolve the site once here and pass it to every request;
        // disabling the selector is just the visible cue, not the guarantee.
        // CP-D: a typed destination (was a raw {url,key} obj). The degenerate no-site case yields
        // an empty destination, which the background rejects exactly as the old null did.
        let submitSite : Client.Api.Destination =
            if activeSiteIndex >= 0 && activeSiteIndex < sites.Length then
                let s = sites.[activeSiteIndex]
                { Url = s.Url; Key = s.Key }
            else { Url = ""; Key = "" }
        let siteSelect = elAs<HTMLSelectElement> "siteSelect"
        btn.disabled <- true
        siteSelect.disabled <- true
        setStatus "Submitting…" ""

        // Rehost the one chosen post image to our R2 before submit, so the post's
        // lead image survives source-side rot (only this one image goes to R2 —
        // the archive keeps original URLs). Falls back to the external URL if the
        // browser capture fails; the server retries the rehost as tier 2.
        let! imageForReq =
            promise {
                match selectedImage with
                | Some url ->
                    let! captured = captureImageToBlob submitSite url
                    return Some (captured |> Option.defaultValue url)
                | None ->
                    return None
            }

        let req: SubmitItem.Request = {
            Title = title
            Slug = if slugRaw <> "" then Some slugRaw else None
            Link = if pageUrl <> "" then Some pageUrl else None
            Image = imageForReq
            Extract = extractJson |> Option.map (fun j -> JS.JSON.stringify j)
            OwnerComment = JS.JSON.stringify commentJson
            Tags = tags
        }

        // C2: submit through the blog module's transport-neutral client, bound to the
        // extension transport pinned to this submission's destination ({url, key}). The
        // generated client owns the path + request/response codecs; the typed ApiError
        // renders to the status string. (Image capture above and the snapshot below share
        // the same pinned destination so the whole submission stays on one tenant.)
        let blogClient = Blog.ClientGen.createClient (Client.Api.extensionTransport submitSite)
        let! result = blogClient.blogSubmitItem req

        match result with
        | Ok resp ->
            setStatus "Submitted!" "success"
            // Best-effort archive of the rendered source page (reference-only —
            // never shown to readers, ok if it rots). The item already exists, so
            // a snapshot failure must not surface as a submit error. Initiated while
            // the site is still locked so it targets the same tenant as the post.
            if documentHtml <> "" then
                // C4: snapshot capture is now a typed blog endpoint — post it through the same
                // generated client (pinned to submitSite), not a hand-built body. Best-effort:
                // the result is ignored so a snapshot failure never surfaces as a submit error.
                let snapReq : Blog.Api.SubmitSnapshot.Request =
                    { ItemId = Hedge.Interface.ForeignKey resp.Item.Id
                      SourceUrl = (if pageUrl = "" then None else Some pageUrl)
                      Html = documentHtml }
                blogClient.blogSubmitSnapshot snapReq |> ignore
        | Error apiErr ->
            setStatus (Hedge.Http.renderError apiErr) "error"

        btn.disabled <- false
        siteSelect.disabled <- false
    }

// ---------------------------------------------------------------------------
// Init
// ---------------------------------------------------------------------------

let init () : JS.Promise<unit> =
    promise {
        // Load sites config
        let! data = Chrome.sendMessage (createObj [ "type" ==> "getSites" ])
        let rawSites: obj array = data?sites |> Option.ofObj |> Option.defaultValue [||]
        sites <- rawSites |> Array.map (fun s -> { Name = s?name; Url = s?url; Key = s?key |> Option.ofObj |> Option.defaultValue "" }) |> Array.toList
        activeSiteIndex <- if isNullOrUndefined data?activeSiteIndex then 0 else int data?activeSiteIndex

        renderSiteDropdown ()

        if sites.IsEmpty then
            showConfig <- true
            (el "configSection").classList.remove "hidden"

        // Dropdown change
        (el "siteSelect").addEventListener("change", fun (e: Event) ->
            activeSiteIndex <- int (e.target :?> HTMLSelectElement).value
            saveSites () |> ignore
        )

        // Gear toggle
        (el "gearBtn").addEventListener("click", fun _ -> toggleConfig ())

        // Add site
        (el "addSiteBtn").addEventListener("click", fun _ ->
            let name = (elAs<HTMLInputElement> "addName").value.Trim()
            let url = (elAs<HTMLInputElement> "addUrl").value.Trim()
            let key = (elAs<HTMLInputElement> "addKey").value.Trim()
            if url <> "" then
                sites <- sites @ [ { Name = (if name <> "" then name else url); Url = url; Key = key } ]
                if sites.Length = 1 then activeSiteIndex <- 0
                saveSites () |> ignore
                renderSiteDropdown ()
                renderConfigTable ()
                (elAs<HTMLInputElement> "addName").value <- ""
                (elAs<HTMLInputElement> "addUrl").value <- ""
                (elAs<HTMLInputElement> "addKey").value <- ""
        )

        // Extract page data
        let! pageData = extractPageData ()

        // Populate title
        (elAs<HTMLInputElement> "title").value <- pageData.Title

        // Show URL
        (el "pageUrl").textContent <- if pageData.Url <> "" then pageData.Url else "—"
        pageUrl <- pageData.Url
        documentHtml <- pageData.DocumentHtml

        // Convert selection HTML to TipTap JSON
        let extractContent = htmlToTipTapJson pageData.SelectionHtml

        // Initialize editors
        extractEditor <- Some (createEditorInEl (el "extractEditor") (el "extractToolbar") extractContent)
        commentEditor <- Some (createEditorInEl (el "commentEditor") (el "commentToolbar") None)

        // Render images
        renderImages pageData.Images

        // Submit handler
        (el "submitBtn").addEventListener("click", fun _ -> submit () |> ignore)
    }

init () |> ignore
