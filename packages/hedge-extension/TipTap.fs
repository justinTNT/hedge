module HedgeExtension.TipTap

open Fable.Core
open Fable.Core.JsInterop

// TipTap Editor handle (opaque)
type Editor = obj

// Create editor with extensions, element, and optional initial content
[<Emit("""
(function() {
  var StarterKit = require('@tiptap/starter-kit').default;
  var Link = require('@tiptap/extension-link').default;
  var core = require('@tiptap/core');
  return new core.Editor({
    element: $0,
    extensions: [StarterKit, Link.configure({ openOnClick: false })],
    content: $1 || { type: 'doc', content: [{ type: 'paragraph' }] }
  });
})()
""")>]
let createEditor (element: Browser.Types.HTMLElement) (initialContent: obj) : Editor = jsNative

// Get editor content as JSON object
[<Emit("$0.getJSON()")>]
let getJSON (editor: Editor) : obj = jsNative

// Get editor content as plain text
[<Emit("$0.getText()")>]
let getText (editor: Editor) : string = jsNative

// Set editor content from HTML string
[<Emit("$0.commands.setContent($1)")>]
let setContent (editor: Editor) (content: obj) : unit = jsNative

// Destroy editor instance
[<Emit("$0.destroy()")>]
let destroy (editor: Editor) : unit = jsNative

// Toolbar chain commands
[<Emit("$0.chain().focus().toggleBold().run()")>]
let toggleBold (editor: Editor) : unit = jsNative

[<Emit("$0.chain().focus().toggleItalic().run()")>]
let toggleItalic (editor: Editor) : unit = jsNative

[<Emit("$0.chain().focus().toggleCode().run()")>]
let toggleCode (editor: Editor) : unit = jsNative

[<Emit("$0.chain().focus().toggleHeading({ level: 2 }).run()")>]
let toggleHeading (editor: Editor) : unit = jsNative

[<Emit("$0.chain().focus().toggleBlockquote().run()")>]
let toggleBlockquote (editor: Editor) : unit = jsNative

[<Emit("$0.chain().focus().toggleBulletList().run()")>]
let toggleBulletList (editor: Editor) : unit = jsNative

[<Emit("$0.chain().focus().setLink({ href: $1 }).run()")>]
let setLink (editor: Editor) (url: string) : unit = jsNative

[<Emit("$0.chain().focus().unsetLink().run()")>]
let unsetLink (editor: Editor) : unit = jsNative

// Check active state
[<Emit("$0.isActive($1)")>]
let isActive (editor: Editor) (name: string) : bool = jsNative

[<Emit("$0.isActive('heading', { level: 2 })")>]
let isActiveHeading (editor: Editor) : bool = jsNative

// Event listeners
[<Emit("$0.on($1, $2)")>]
let on (editor: Editor) (event: string) (handler: unit -> unit) : unit = jsNative
