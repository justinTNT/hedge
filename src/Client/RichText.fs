module Client.RichText

open Fable.Core

// Element ID constants
let commentEditorId = "comment-editor"
let ownerCommentEditorId = "owner-comment-editor"

// Editor lifecycle (deferred — waits for DOM element to appear)

[<Emit("window.HedgeRT.waitForElement($0, function() { window.HedgeRT.createRichTextEditor({ elementId: $0, initialContent: $1, onChange: null }); })")>]
let createEditorWhenReady (elementId: string) (initialContent: string) : unit = jsNative

[<Emit("window.HedgeRT.destroyRichTextEditor($0)")>]
let destroyEditor (elementId: string) : unit = jsNative

[<Emit("window.HedgeRT.getEditorContentJSON($0)")>]
let getEditorContent (elementId: string) : string = jsNative

[<Emit("(function(){ var e = window.HedgeRT.getEditor($0); if(e) e.commands.clearContent(); })()")>]
let clearEditor (elementId: string) : unit = jsNative

// Viewer lifecycle (deferred — waits for DOM element to appear)

[<Emit("window.HedgeRT.waitForElement($0, function() { window.HedgeRT.createRichTextViewer({ elementId: $0, content: $1 }); })")>]
let createViewerWhenReady (elementId: string) (content: string) : unit = jsNative

[<Emit("window.HedgeRT.destroyRichTextViewer($0)")>]
let destroyViewer (elementId: string) : unit = jsNative

// Plain text extraction

[<Emit("window.HedgeRT.extractPlainText($0)")>]
let extractPlainText (jsonString: string) : string = jsNative
