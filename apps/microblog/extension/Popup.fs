module Extension.Popup

open Fable.Core
open Fable.Core.JsInterop
open Browser.Dom
open Browser.Types
open HedgeExtension
open HedgeExtension.TipTap
open Models.Api
open Client.ClientGen
open Codecs

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------

let mutable extractEditor: Editor option = None
let mutable commentEditor: Editor option = None
let mutable selectedImage: string option = None
let mutable pageUrl = ""

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
    var images = Array.from(document.images)
      .map(function(img) { return img.src; })
      .filter(function(src) { return src.startsWith('http'); })
      .filter(function(src, i, arr) { return arr.indexOf(src) === i; })
      .slice(0, 50);
    return { selectionHtml: selectionHtml, selectionText: selectionText, images: images };
  }
})
""")>]
let private executeContentScript (tabId: int) : JS.Promise<obj array> = jsNative

let extractPageData () : JS.Promise<PageData> =
    promise {
        let! tabs = Chrome.queryActiveTab ()
        if tabs.Length = 0 then
            return { Title = ""; Url = ""; SelectionHtml = ""; SelectionText = ""; Images = [||] }
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
                return { Title = tabTitle; Url = tabUrl; SelectionHtml = ""; SelectionText = ""; Images = [||] }
            | Some r ->
                let data = if r.Length > 0 then r.[0]?result else null
                if isNullOrUndefined data then
                    return { Title = tabTitle; Url = tabUrl; SelectionHtml = ""; SelectionText = ""; Images = [||] }
                else
                    let sh: string = if isNullOrUndefined data?selectionHtml then "" else string data?selectionHtml
                    let st: string = if isNullOrUndefined data?selectionText then "" else string data?selectionText
                    let imgs: string array = if isNullOrUndefined data?images then [||] else data?images
                    return {
                        Title = tabTitle
                        Url = tabUrl
                        SelectionHtml = sh
                        SelectionText = st
                        Images = imgs
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

        let req: SubmitItem.Request = {
            Title = title
            Slug = if slugRaw <> "" then Some slugRaw else None
            Link = if pageUrl <> "" then Some pageUrl else None
            Image = selectedImage
            Extract = extractJson |> Option.map (fun j -> JS.JSON.stringify j)
            OwnerComment = JS.JSON.stringify commentJson
            Tags = tags
        }

        let btn = elAs<HTMLButtonElement> "submitBtn"
        btn.disabled <- true
        setStatus "Submitting…" ""

        let! result = submitItem req
        btn.disabled <- false

        match result with
        | Ok _ ->
            setStatus "Submitted!" "success"
        | Error msg ->
            setStatus msg "error"
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
