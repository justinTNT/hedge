module Client.RichText

open Fable.Core

/// Render stored rich content (ProseMirror JSON) to HTML for display.
/// Backed by window.HedgeRT (lib/rich-text/bootstrap.js), loaded in index.html.
[<Emit("window.HedgeRT.renderRichTextHtml($0)")>]
let toHtml (content: string) : string = jsNative
