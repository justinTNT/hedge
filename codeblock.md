You already have @tiptap/starter-kit which includes basic CodeBlock (plain <pre><code>). For syntax highlighting, swap it for:

  @tiptap/extension-code-block-lowlight — uses lowlight (highlight.js under the hood) for language-aware highlighting.

  npm install @tiptap/extension-code-block-lowlight lowlight

  Then in your TipTap setup, disable the default code block from StarterKit and add the lowlight one:

  import { lowlight } from 'lowlight'
  import CodeBlockLowlight from '@tiptap/extension-code-block-lowlight'

  StarterKit.configure({ codeBlock: false }),
  CodeBlockLowlight.configure({ lowlight })

  By default lowlight bundles all languages (~190). To keep bundle size small, import only what you need:

  import { createLowlight } from 'lowlight'
  import js from 'highlight.js/lib/languages/javascript'
  import fsharp from 'highlight.js/lib/languages/fsharp'

  const lowlight = createLowlight()
  lowlight.register('javascript', js)
  lowlight.register('fsharp', fsharp)

  You'll also need a highlight.js CSS theme — just pick one and add a <link> to it, e.g. highlight.js/styles/github.css or
  github-dark.css.
