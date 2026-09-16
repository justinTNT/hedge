module Client.RichText

open Fable.Core

// Element ID constants
let commentEditorId = "comment-editor"
let ownerCommentEditorId = "owner-comment-editor"

// Editor lifecycle (deferred — waits for DOM element to appear)

[<Emit("window.HedgeRT.waitForElement($0, function() { window.HedgeRT.createRichTextEditor({ elementId: $0, initialContent: $1, onChange: null }); })")>]
let createEditorWhenReady (elementId: string) (initialContent: string) : unit = jsNative

/// The public COMMENT editor (blog/articles reply box). Reports content changes via `onChange`
/// (draft text -> model) so submission builds from model state instead of a DOM read; `onClose`
/// handles the reply box's close affordance (pass `ignore` when none); `uploadEndpoint` selects
/// the blob endpoint ("" -> default admin `/api/blobs`; comments pass the guest-gated,
/// raster-only, size-capped "/api/blobs/guest"). Deferred creation is cancellable via
/// `destroyEditor` (the waitForElement poll self-cancels). The former fixed-onChange=null
/// `createEditorWithClose` is gone (C5) — all comment editors use this draft-model form.
[<Emit("window.HedgeRT.waitForElement($0, function() { window.HedgeRT.createRichTextEditor({ elementId: $0, initialContent: $1, onChange: $2, onClose: $3, uploadEndpoint: ($4 || undefined) }); })")>]
let createEditorScoped (elementId: string) (initialContent: string) (onChange: string -> unit) (onClose: unit -> unit) (uploadEndpoint: string) : unit = jsNative

[<Emit("window.HedgeRT.destroyRichTextEditor($0)")>]
let destroyEditor (elementId: string) : unit = jsNative

[<Emit("window.HedgeRT.getEditorContentJSON($0)")>]
let getEditorContent (elementId: string) : string = jsNative

[<Emit("(function(){ var e = window.HedgeRT.getEditor($0); if(e) e.commands.clearContent(); })()")>]
let clearEditor (elementId: string) : unit = jsNative

// Rendering — pure content -> HTML, for declarative views. Preferred over the
// viewer lifecycle below: nothing to dispose, and it survives any re-render.

[<Emit("window.HedgeRT.renderRichTextHtml($0)")>]
let toHtml (content: string) : string = jsNative

// Viewer lifecycle (deferred — waits for DOM element to appear)

[<Emit("window.HedgeRT.waitForElement($0, function() { window.HedgeRT.createRichTextViewer({ elementId: $0, content: $1 }); })")>]
let createViewerWhenReady (elementId: string) (content: string) : unit = jsNative

[<Emit("window.HedgeRT.destroyRichTextViewer($0)")>]
let destroyViewer (elementId: string) : unit = jsNative

// Plain text extraction

[<Emit("window.HedgeRT.extractPlainText($0)")>]
let extractPlainText (jsonString: string) : string = jsNative
