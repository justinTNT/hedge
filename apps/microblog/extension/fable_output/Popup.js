import { int32ToString, equals, defaultOf, disposeSafe, getEnumerator, createAtom } from "./fable_modules/fable-library-js.4.29.0/Util.js";
import { toString, Union, Record } from "./fable_modules/fable-library-js.4.29.0/Types.js";
import { array_type, union_type, bool_type, lambda_type, unit_type, record_type, string_type } from "./fable_modules/fable-library-js.4.29.0/Reflection.js";
import { singleton, append, ofArray, indexed, filter, item as item_1, length, isEmpty, map, toArray, empty } from "./fable_modules/fable-library-js.4.29.0/List.js";
import { ofNullable, map as map_2, value as value_9, some, defaultArg } from "./fable_modules/fable-library-js.4.29.0/Option.js";
import { PromiseBuilder__Delay_62FBFDE1, PromiseBuilder__Run_212F1D4B } from "./fable_modules/Fable.Promise.3.2.0/Promise.fs.js";
import { promise } from "./fable_modules/Fable.Promise.3.2.0/PromiseImpl.fs.js";
import { map as map_1, item } from "./fable_modules/fable-library-js.4.29.0/Array.js";
import { trimEnd, isNullOrEmpty } from "./fable_modules/fable-library-js.4.29.0/String.js";
import { max } from "./fable_modules/fable-library-js.4.29.0/Double.js";
import { SubmitItem_Request } from "./src/Models/Api.js";
import { submitItem } from "./src/Client/generated/ClientGen.js";
import { parse } from "./fable_modules/fable-library-js.4.29.0/Int32.js";

export let extractEditor = createAtom(undefined);

export let commentEditor = createAtom(undefined);

export let selectedImage = createAtom(undefined);

export let pageUrl = createAtom("");

export class Site extends Record {
    constructor(Name, Url, Key) {
        super();
        this.Name = Name;
        this.Url = Url;
        this.Key = Key;
    }
}

export function Site_$reflection() {
    return record_type("Extension.Popup.Site", [], Site, () => [["Name", string_type], ["Url", string_type], ["Key", string_type]]);
}

export let sites = createAtom(empty());

export let activeSiteIndex = createAtom(0);

export let showConfig = createAtom(false);

export function el(id) {
    return document.getElementById(id);
}

export function elAs(id) {
    return document.getElementById(id);
}

export class ToolbarButton extends Union {
    constructor(tag, fields) {
        super();
        this.tag = tag;
        this.fields = fields;
    }
    cases() {
        return ["Separator", "Button"];
    }
}

export function ToolbarButton_$reflection() {
    return union_type("Extension.Popup.ToolbarButton", [], ToolbarButton, () => [[], [["label", string_type], ["title", string_type], ["cmd", lambda_type(unit_type, unit_type)], ["active", lambda_type(unit_type, bool_type)]]]);
}

export function createToolbar(editor, toolbarEl) {
    const btnEls = [];
    const enumerator = getEnumerator([new ToolbarButton(1, ["B", "Bold", () => {
        editor.chain().focus().toggleBold().run();
    }, () => (editor.isActive("bold"))]), new ToolbarButton(1, ["I", "Italic", () => {
        editor.chain().focus().toggleItalic().run();
    }, () => (editor.isActive("italic"))]), new ToolbarButton(1, ["{}", "Code", () => {
        editor.chain().focus().toggleCode().run();
    }, () => (editor.isActive("code"))]), new ToolbarButton(0, []), new ToolbarButton(1, ["H2", "Heading", () => {
        editor.chain().focus().toggleHeading({ level: 2 }).run();
    }, () => (editor.isActive('heading', { level: 2 }))]), new ToolbarButton(1, ["\"", "Quote", () => {
        editor.chain().focus().toggleBlockquote().run();
    }, () => (editor.isActive("blockquote"))]), new ToolbarButton(1, ["•", "List", () => {
        editor.chain().focus().toggleBulletList().run();
    }, () => (editor.isActive("bulletList"))]), new ToolbarButton(0, []), new ToolbarButton(1, ["🔗", "Link", () => {
        const url = window.prompt("URL:");
        if (!(url == null)) {
            if (url === "") {
                editor.chain().focus().unsetLink().run();
            }
            else {
                editor.chain().focus().setLink({ href: url }).run();
            }
        }
    }, () => (editor.isActive("link"))])]);
    try {
        while (enumerator["System.Collections.IEnumerator.MoveNext"]()) {
            const btn = enumerator["System.Collections.Generic.IEnumerator`1.get_Current"]();
            if (btn.tag === 1) {
                const b = document.createElement("button");
                b.type = "button";
                b.textContent = btn.fields[0];
                b.title = btn.fields[1];
                b.addEventListener("click", (e) => {
                    e.preventDefault();
                    btn.fields[2]();
                });
                toolbarEl.appendChild(b);
                void (btnEls.push([b, btn.fields[3]]));
            }
            else {
                const sep = document.createElement("div");
                sep.className = "sep";
                toolbarEl.appendChild(sep);
            }
        }
    }
    finally {
        disposeSafe(enumerator);
    }
    const update = () => {
        let enumerator_1 = getEnumerator(btnEls);
        try {
            while (enumerator_1["System.Collections.IEnumerator.MoveNext"]()) {
                const forLoopVar = enumerator_1["System.Collections.Generic.IEnumerator`1.get_Current"]();
                const b_1 = forLoopVar[0];
                if (forLoopVar[1]()) {
                    b_1.classList.add("active");
                }
                else {
                    b_1.classList.remove("active");
                }
            }
        }
        finally {
            disposeSafe(enumerator_1);
        }
    };
    editor.on("selectionUpdate", update);
    editor.on("transaction", update);
}

export function createEditorInEl(contentEl, toolbarEl, initialContent) {
    const content = defaultArg(initialContent, {
        type: "doc",
        content: [{
            type: "paragraph",
        }],
    });
    const editor = (function() {
  var StarterKit = require('@tiptap/starter-kit').default;
  var Link = require('@tiptap/extension-link').default;
  var core = require('@tiptap/core');
  return new core.Editor({
    element: contentEl,
    extensions: [StarterKit, Link.configure({ openOnClick: false })],
    content: content || { type: 'doc', content: [{ type: 'paragraph' }] }
  });
})()
;
    createToolbar(editor, toolbarEl);
    return editor;
}

export class PageData extends Record {
    constructor(Title, Url, SelectionHtml, SelectionText, Images) {
        super();
        this.Title = Title;
        this.Url = Url;
        this.SelectionHtml = SelectionHtml;
        this.SelectionText = SelectionText;
        this.Images = Images;
    }
}

export function PageData_$reflection() {
    return record_type("Extension.Popup.PageData", [], PageData, () => [["Title", string_type], ["Url", string_type], ["SelectionHtml", string_type], ["SelectionText", string_type], ["Images", array_type(string_type)]]);
}

export function extractPageData() {
    return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => ((chrome.tabs.query({ active: true, currentWindow: true })).then((_arg) => {
        const tabs = _arg;
        if (tabs.length === 0) {
            return Promise.resolve(new PageData("", "", "", "", []));
        }
        else {
            const tab = item(0, tabs);
            const tabUrl = (tab.url == null) ? "" : toString(tab.url);
            const tabTitle = (tab.title == null) ? "" : toString(tab.title);
            const tabId = tab.id | 0;
            pageUrl(tabUrl);
            return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => (PromiseBuilder__Delay_62FBFDE1(promise, () => ((chrome.scripting.executeScript({
  target: { tabId: tabId },
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
).then((_arg_1) => (Promise.resolve(_arg_1))))).catch((_arg_2) => (Promise.resolve(undefined)))))).then((_arg_3) => {
                const results = _arg_3;
                if (results != null) {
                    const r_1 = results;
                    const data = (r_1.length > 0) ? item(0, r_1).result : defaultOf();
                    if (data == null) {
                        return Promise.resolve(new PageData(tabTitle, tabUrl, "", "", []));
                    }
                    else {
                        const sh = (data.selectionHtml == null) ? "" : toString(data.selectionHtml);
                        const st = (data.selectionText == null) ? "" : toString(data.selectionText);
                        const imgs = (data.images == null) ? [] : data.images;
                        return Promise.resolve(new PageData(tabTitle, tabUrl, sh, st, imgs));
                    }
                }
                else {
                    return Promise.resolve(new PageData(tabTitle, tabUrl, "", "", []));
                }
            });
        }
    }))));
}

export function htmlToTipTapJson(html) {
    if (isNullOrEmpty(html)) {
        return undefined;
    }
    else {
        const tempEl = document.createElement("div");
        tempEl.style.display = "none";
        document.body.appendChild(tempEl);
        const tempEditor = (function() {
  var StarterKit = require('@tiptap/starter-kit').default;
  var Link = require('@tiptap/extension-link').default;
  var core = require('@tiptap/core');
  return new core.Editor({
    element: tempEl,
    extensions: [StarterKit, Link.configure({ openOnClick: false })],
    content: "" || { type: 'doc', content: [{ type: 'paragraph' }] }
  });
})()
;
        tempEditor.commands.setContent(html);
        const json = tempEditor.getJSON();
        tempEditor.destroy();
        tempEl.remove();
        return some(json);
    }
}

export function renderImages(images) {
    const gallery = el("imagesGallery");
    gallery.innerHTML = "";
    selectedImage(undefined);
    for (let idx = 0; idx <= (images.length - 1); idx++) {
        const src = item(idx, images);
        const img = document.createElement("img");
        img.className = "img-thumb";
        img.src = src;
        img.title = src;
        img.addEventListener("click", (_arg) => {
            if (equals(selectedImage(), src)) {
                selectedImage(undefined);
                img.classList.remove("selected");
            }
            else {
                selectedImage(src);
                const nodes = gallery.querySelectorAll(".img-thumb");
                for (let j = 0; j <= (nodes.length - 1); j++) {
                    (nodes[j]).classList.remove("selected");
                }
                img.classList.add("selected");
            }
        });
        img.addEventListener("error", (_arg_1) => {
            img.remove();
        });
        gallery.appendChild(img);
    }
}

export function setStatus(text, typ) {
    const status = el("status");
    status.textContent = text;
    status.className = ("status" + ((typ !== "") ? (" " + typ) : ""));
}

export function saveSites() {
    return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => {
        const sitesJs = toArray(map((s) => ({
            name: s.Name,
            url: s.Url,
            key: s.Key,
        }), sites()));
        return (chrome.runtime.sendMessage({
            type: "setSites",
            sites: sitesJs,
            activeSiteIndex: activeSiteIndex(),
        })).then((_arg) => (Promise.resolve(undefined)));
    }));
}

export function renderSiteDropdown() {
    const select = elAs("siteSelect");
    select.innerHTML = "";
    if (isEmpty(sites())) {
        const opt = document.createElement("option");
        opt.textContent = "(no sites configured)";
        opt.disabled = true;
        select.appendChild(opt);
    }
    else {
        for (let i = 0; i <= (length(sites()) - 1); i++) {
            const site = item_1(i, sites());
            const opt_1 = document.createElement("option");
            opt_1.value = int32ToString(i);
            opt_1.textContent = ((site.Name !== "") ? site.Name : site.Url);
            if (i === activeSiteIndex()) {
                opt_1.selected = true;
            }
            select.appendChild(opt_1);
        }
    }
}

export function renderConfigTable() {
    const tbody = el("configBody");
    tbody.innerHTML = "";
    for (let i = 0; i <= (length(sites()) - 1); i++) {
        const site = item_1(i, sites());
        const tr = document.createElement("tr");
        const tdName = document.createElement("td");
        tdName.textContent = ((site.Name !== "") ? site.Name : "—");
        tr.appendChild(tdName);
        const tdUrl = document.createElement("td");
        tdUrl.style.fontFamily = "monospace";
        tdUrl.textContent = site.Url;
        tr.appendChild(tdUrl);
        const tdKey = document.createElement("td");
        tdKey.style.fontFamily = "monospace";
        tdKey.textContent = ((site.Key !== "") ? "••••" : "—");
        tr.appendChild(tdKey);
        const tdActions = document.createElement("td");
        tdActions.className = "actions-cell";
        const adminBtn = document.createElement("button");
        adminBtn.className = "btn-sm btn-admin";
        adminBtn.textContent = "Admin";
        adminBtn.addEventListener("click", (_arg) => {
            const adminUrl = (trimEnd(site.Url, "/") + "/admin.html#key=") + encodeURIComponent(site.Key);
            window.open(adminUrl, "_blank");
        });
        tdActions.appendChild(adminBtn);
        const delBtn = document.createElement("button");
        delBtn.className = "btn-sm btn-del";
        delBtn.textContent = "Del";
        delBtn.style.marginLeft = "4px";
        delBtn.addEventListener("click", (_arg_1) => {
            sites(map((tuple) => tuple[1], filter((tupledArg) => (tupledArg[0] !== i), indexed(sites()))));
            if (activeSiteIndex() >= length(sites())) {
                activeSiteIndex(max(0, length(sites()) - 1));
            }
            saveSites();
            renderSiteDropdown();
            renderConfigTable();
        });
        tdActions.appendChild(delBtn);
        tr.appendChild(tdActions);
        tbody.appendChild(tr);
    }
}

export function toggleConfig() {
    showConfig(!showConfig());
    const section = el("configSection");
    if (showConfig()) {
        section.classList.remove("hidden");
    }
    else {
        section.classList.add("hidden");
    }
    if (showConfig()) {
        renderConfigTable();
    }
}

export function submit() {
    return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => {
        let array_1;
        const title = elAs("title").value.trim();
        if (title === "") {
            setStatus("Title is required", "error");
            return Promise.resolve();
        }
        else if (commentEditor() != null) {
            const editor = value_9(commentEditor());
            if ((editor.getText()).trim() === "") {
                setStatus("Comment is required", "error");
                return Promise.resolve();
            }
            else {
                const commentJson = editor.getJSON();
                let extractJson;
                if (extractEditor() == null) {
                    extractJson = undefined;
                }
                else {
                    const ext = value_9(extractEditor());
                    extractJson = (((ext.getText()).trim() !== "") ? some(ext.getJSON()) : undefined);
                }
                const slugRaw = elAs("slug").value.trim();
                const tagsRaw = elAs("tags").value;
                const tags = ofArray((array_1 = map_1((t_1) => t_1.trim(), tagsRaw.split(",")), array_1.filter((t_2) => (t_2 !== ""))));
                const req = new SubmitItem_Request(title, (slugRaw !== "") ? slugRaw : undefined, (pageUrl() !== "") ? pageUrl() : undefined, selectedImage(), map_2((j) => JSON.stringify(j), extractJson), JSON.stringify(commentJson), tags);
                const btn = elAs("submitBtn");
                btn.disabled = true;
                setStatus("Submitting…", "");
                return submitItem(req).then((_arg) => {
                    const result = _arg;
                    btn.disabled = false;
                    if (result.tag === 1) {
                        setStatus(result.fields[0], "error");
                        return Promise.resolve();
                    }
                    else {
                        setStatus("Submitted!", "success");
                        return Promise.resolve();
                    }
                });
            }
        }
        else {
            setStatus("Editor not ready", "error");
            return Promise.resolve();
        }
    }));
}

export function init() {
    return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => ((chrome.runtime.sendMessage({
        type: "getSites",
    })).then((_arg) => {
        const data = _arg;
        const rawSites = defaultArg(ofNullable(data.sites), []);
        sites(ofArray(map_1((s) => (new Site(s.name, s.url, defaultArg(ofNullable(s.key), ""))), rawSites)));
        activeSiteIndex((data.activeSiteIndex == null) ? 0 : data.activeSiteIndex);
        renderSiteDropdown();
        return (isEmpty(sites()) ? ((showConfig(true), (el("configSection").classList.remove("hidden"), Promise.resolve()))) : (Promise.resolve())).then(() => PromiseBuilder__Delay_62FBFDE1(promise, () => {
            el("siteSelect").addEventListener("change", (e) => {
                activeSiteIndex(parse(e.target.value, 511, false, 32));
                saveSites();
            });
            el("gearBtn").addEventListener("click", (_arg_1) => {
                toggleConfig();
            });
            el("addSiteBtn").addEventListener("click", (_arg_2) => {
                const name = elAs("addName").value.trim();
                const url = elAs("addUrl").value.trim();
                const key = elAs("addKey").value.trim();
                if (url !== "") {
                    sites(append(sites(), singleton(new Site((name !== "") ? name : url, url, key))));
                    if (length(sites()) === 1) {
                        activeSiteIndex(0);
                    }
                    saveSites();
                    renderSiteDropdown();
                    renderConfigTable();
                    elAs("addName").value = "";
                    elAs("addUrl").value = "";
                    elAs("addKey").value = "";
                }
            });
            return extractPageData().then((_arg_3) => {
                const pageData = _arg_3;
                elAs("title").value = pageData.Title;
                el("pageUrl").textContent = ((pageData.Url !== "") ? pageData.Url : "—");
                pageUrl(pageData.Url);
                const extractContent = htmlToTipTapJson(pageData.SelectionHtml);
                extractEditor(some(createEditorInEl(el("extractEditor"), el("extractToolbar"), extractContent)));
                commentEditor(some(createEditorInEl(el("commentEditor"), el("commentToolbar"), undefined)));
                renderImages(pageData.Images);
                el("submitBtn").addEventListener("click", (_arg_4) => {
                    submit();
                });
                return Promise.resolve();
            });
        }));
    }))));
}

init();

