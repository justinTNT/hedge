/**
 * TipTap Rich Text Editor Wrapper
 *
 * Provides a shared rich text editor that can be used across multiple apps
 * (admin, web, extension). Uses TipTap with comprehensive formatting options.
 */

import { Editor } from '@tiptap/core'
import { Plugin, PluginKey } from '@tiptap/pm/state'
import { Decoration, DecorationSet } from '@tiptap/pm/view'
import StarterKit from '@tiptap/starter-kit'
import Link from '@tiptap/extension-link'
import Image from '@tiptap/extension-image'
import TextAlign from '@tiptap/extension-text-align'
import { Color } from '@tiptap/extension-color'
import TextStyle from '@tiptap/extension-text-style'
import Highlight from '@tiptap/extension-highlight'

const editors = new Map()

/**
 * Wait for a DOM element to appear, then call the callback.
 * Retries on each animation frame up to maxAttempts.
 */
export function waitForElement(elementId, callback, maxAttempts = 20) {
    let attempts = 0
    function check() {
        if (document.getElementById(elementId)) {
            callback()
        } else if (++attempts < maxAttempts) {
            requestAnimationFrame(check)
        } else {
            console.warn(`[hamlet-rt] Element never appeared: ${elementId}`)
        }
    }
    requestAnimationFrame(check)
}

// =============================================================================
// IMAGE UPLOAD HELPER
// =============================================================================

/**
 * A single data-URI placeholder shown while uploading.
 * 1×1 transparent GIF — the CSS class handles the visual.
 */
const PLACEHOLDER_SRC = 'data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7'

let placeholderId = 0

/**
 * Upload a file to the blob endpoint and insert the image into the editor.
 * Shows a placeholder with a progress bar while uploading.
 *
 * @param {Editor} editor - The TipTap editor
 * @param {File} file - The image file to upload
 * @param {HTMLElement} container - The editor container (holds _hamletUploadEndpoint)
 * @param {number} [insertPos] - Optional ProseMirror position to insert at
 */
function uploadAndInsertImage(editor, file, container, insertPos) {
    const endpoint = container._hamletUploadEndpoint || '/api/blobs'
    const id = `upload-${++placeholderId}`

    // Determine insert position
    const pos = insertPos != null ? insertPos : editor.state.selection.anchor

    // Insert placeholder node
    const { tr } = editor.state
    const placeholderNode = editor.state.schema.nodes.image.create({
        src: PLACEHOLDER_SRC,
        alt: 'Uploading…',
        'data-upload-id': id,
    })
    tr.insert(pos, placeholderNode)
    editor.view.dispatch(tr)

    // Upload via XHR for progress events
    const xhr = new XMLHttpRequest()
    const formData = new FormData()
    formData.append('file', file)

    xhr.upload.addEventListener('progress', (e) => {
        if (!e.lengthComputable) return
        const pct = Math.round((e.loaded / e.total) * 100)
        // Update the progress overlay on the placeholder
        const placeholderEl = container.querySelector(`img[data-upload-id="${id}"]`)
        if (placeholderEl) {
            const wrapper = placeholderEl.closest('.hamlet-rt-img-upload')
            if (wrapper) {
                const bar = wrapper.querySelector('.hamlet-rt-upload-bar')
                if (bar) bar.style.width = `${pct}%`
            }
        }
    })

    xhr.addEventListener('load', () => {
        if (xhr.status >= 200 && xhr.status < 300) {
            try {
                const data = JSON.parse(xhr.responseText)
                // Find and replace the placeholder with the real image
                replacePlaceholder(editor, id, data.url)
            } catch (err) {
                console.error('[hamlet-rt] Failed to parse upload response:', err)
                removePlaceholder(editor, id)
            }
        } else {
            console.error(`[hamlet-rt] Upload failed: ${xhr.status}`)
            removePlaceholder(editor, id)
        }
    })

    xhr.addEventListener('error', () => {
        console.error('[hamlet-rt] Upload network error')
        removePlaceholder(editor, id)
    })

    xhr.open('POST', endpoint)
    xhr.send(formData)
}

/**
 * Replace a placeholder image node with the final URL.
 */
function replacePlaceholder(editor, uploadId, url) {
    const { doc, tr } = editor.state
    doc.descendants((node, pos) => {
        if (node.type.name === 'image' && node.attrs['data-upload-id'] === uploadId) {
            tr.setNodeMarkup(pos, null, {
                ...node.attrs,
                src: url,
                alt: null,
                'data-upload-id': null,
            })
            return false // stop searching
        }
    })
    editor.view.dispatch(tr)
}

/**
 * Remove a placeholder image node (on upload failure).
 */
function removePlaceholder(editor, uploadId) {
    const { doc, tr } = editor.state
    doc.descendants((node, pos) => {
        if (node.type.name === 'image' && node.attrs['data-upload-id'] === uploadId) {
            tr.delete(pos, pos + node.nodeSize)
            return false
        }
    })
    editor.view.dispatch(tr)
}

/**
 * Extract image files from a DataTransfer (drop or paste).
 */
function getImageFiles(dataTransfer) {
    const files = []
    if (dataTransfer?.files) {
        for (const file of dataTransfer.files) {
            if (file.type.startsWith('image/')) {
                files.push(file)
            }
        }
    }
    return files
}

// =============================================================================
// RESIZABLE IMAGE NODE VIEW
// =============================================================================

/**
 * Creates a ProseMirror NodeView that wraps images in a resizable container.
 * Corner handles allow drag-to-resize. Width is stored as a node attribute.
 */
function createResizableImageView(node, view, getPos) {
    // Outer wrapper
    const wrapper = document.createElement('div')
    wrapper.className = 'hamlet-rt-img-wrapper'

    // If this is an upload placeholder, add the upload overlay
    const isUploading = node.attrs['data-upload-id'] != null && node.attrs.src === PLACEHOLDER_SRC
    if (isUploading) {
        wrapper.classList.add('hamlet-rt-img-upload')
    }

    // The <img> element
    const img = document.createElement('img')
    img.src = node.attrs.src
    if (node.attrs.alt) img.alt = node.attrs.alt
    if (node.attrs.title) img.title = node.attrs.title
    if (node.attrs['data-upload-id']) {
        img.setAttribute('data-upload-id', node.attrs['data-upload-id'])
    }
    // Apply persisted width
    if (node.attrs.width) {
        img.style.width = node.attrs.width
    }

    wrapper.appendChild(img)

    // Upload progress bar overlay
    if (isUploading) {
        const overlay = document.createElement('div')
        overlay.className = 'hamlet-rt-upload-overlay'
        const bar = document.createElement('div')
        bar.className = 'hamlet-rt-upload-bar'
        overlay.appendChild(bar)
        wrapper.appendChild(overlay)
    }

    // Resize handle (bottom-right corner)
    if (!isUploading) {
        const handle = document.createElement('div')
        handle.className = 'hamlet-rt-resize-handle'

        let startX, startWidth

        handle.addEventListener('mousedown', (e) => {
            e.preventDefault()
            e.stopPropagation()
            startX = e.clientX
            startWidth = img.offsetWidth

            wrapper.classList.add('hamlet-rt-resizing')

            const onMouseMove = (e) => {
                const dx = e.clientX - startX
                const newWidth = Math.max(50, startWidth + dx)
                img.style.width = `${newWidth}px`
            }

            const onMouseUp = () => {
                document.removeEventListener('mousemove', onMouseMove)
                document.removeEventListener('mouseup', onMouseUp)
                wrapper.classList.remove('hamlet-rt-resizing')

                // Persist the width into the ProseMirror node attrs
                const pos = getPos()
                if (pos != null) {
                    const tr = view.state.tr.setNodeMarkup(pos, null, {
                        ...node.attrs,
                        width: `${img.offsetWidth}px`,
                    })
                    view.dispatch(tr)
                }
            }

            document.addEventListener('mousemove', onMouseMove)
            document.addEventListener('mouseup', onMouseUp)
        })

        wrapper.appendChild(handle)
    }

    return {
        dom: wrapper,
        update(updatedNode) {
            if (updatedNode.type.name !== 'image') return false
            img.src = updatedNode.attrs.src
            if (updatedNode.attrs.alt) img.alt = updatedNode.attrs.alt
            else img.removeAttribute('alt')
            if (updatedNode.attrs.title) img.title = updatedNode.attrs.title
            if (updatedNode.attrs['data-upload-id']) {
                img.setAttribute('data-upload-id', updatedNode.attrs['data-upload-id'])
            } else {
                img.removeAttribute('data-upload-id')
            }
            if (updatedNode.attrs.width) {
                img.style.width = updatedNode.attrs.width
            }

            // Transition from uploading to uploaded
            const wasUploading = wrapper.classList.contains('hamlet-rt-img-upload')
            const nowUploading = updatedNode.attrs['data-upload-id'] != null && updatedNode.attrs.src === PLACEHOLDER_SRC
            if (wasUploading && !nowUploading) {
                wrapper.classList.remove('hamlet-rt-img-upload')
                const overlay = wrapper.querySelector('.hamlet-rt-upload-overlay')
                if (overlay) overlay.remove()

                // Add resize handle now that upload is done
                if (!wrapper.querySelector('.hamlet-rt-resize-handle')) {
                    const handle = document.createElement('div')
                    handle.className = 'hamlet-rt-resize-handle'
                    let startX, startWidth

                    handle.addEventListener('mousedown', (e) => {
                        e.preventDefault()
                        e.stopPropagation()
                        startX = e.clientX
                        startWidth = img.offsetWidth
                        wrapper.classList.add('hamlet-rt-resizing')

                        const onMouseMove = (ev) => {
                            const dx = ev.clientX - startX
                            const newWidth = Math.max(50, startWidth + dx)
                            img.style.width = `${newWidth}px`
                        }
                        const onMouseUp = () => {
                            document.removeEventListener('mousemove', onMouseMove)
                            document.removeEventListener('mouseup', onMouseUp)
                            wrapper.classList.remove('hamlet-rt-resizing')
                            const pos = getPos()
                            if (pos != null) {
                                const tr = view.state.tr.setNodeMarkup(pos, null, {
                                    ...updatedNode.attrs,
                                    width: `${img.offsetWidth}px`,
                                })
                                view.dispatch(tr)
                            }
                        }
                        document.addEventListener('mousemove', onMouseMove)
                        document.addEventListener('mouseup', onMouseUp)
                    })
                    wrapper.appendChild(handle)
                }
            }

            // Update node reference for closures
            node = updatedNode
            return true
        },
        selectNode() {
            wrapper.classList.add('ProseMirror-selectednode')
        },
        deselectNode() {
            wrapper.classList.remove('ProseMirror-selectednode')
        },
        destroy() {
            // cleanup handled by GC
        },
    }
}

// Predefined colors for text and highlight
const TEXT_COLORS = [
    { name: 'Default', value: null },
    { name: 'Red', value: '#dc3545' },
    { name: 'Orange', value: '#fd7e14' },
    { name: 'Green', value: '#28a745' },
    { name: 'Blue', value: '#007bff' },
    { name: 'Purple', value: '#6f42c1' },
    { name: 'Gray', value: '#6c757d' },
]

const HIGHLIGHT_COLORS = [
    { name: 'None', value: null },
    { name: 'Yellow', value: '#fff3cd' },
    { name: 'Green', value: '#d4edda' },
    { name: 'Blue', value: '#cce5ff' },
    { name: 'Pink', value: '#f8d7da' },
    { name: 'Purple', value: '#e2d9f3' },
]

/**
 * Creates a dropdown for color selection
 */
function createColorDropdown(editor, type, colors, container) {
    const wrapper = document.createElement('div')
    wrapper.className = 'hamlet-rt-dropdown'

    const button = document.createElement('button')
    button.type = 'button'
    button.className = 'hamlet-rt-btn hamlet-rt-dropdown-btn'
    button.title = type === 'text' ? 'Text Color' : 'Highlight'
    button.innerHTML = type === 'text' ? 'A' : '<span class="highlight-icon">█</span>'

    const dropdown = document.createElement('div')
    dropdown.className = 'hamlet-rt-dropdown-menu'
    dropdown.style.display = 'none'

    colors.forEach(color => {
        const option = document.createElement('button')
        option.type = 'button'
        option.className = 'hamlet-rt-color-option'
        option.title = color.name
        if (color.value) {
            option.style.backgroundColor = color.value
            if (type === 'text') {
                option.style.backgroundColor = 'white'
                option.style.color = color.value
                option.textContent = 'A'
            }
        } else {
            option.textContent = type === 'text' ? 'A' : '∅'
            option.style.fontSize = '12px'
        }
        option.onclick = (e) => {
            e.preventDefault()
            e.stopPropagation()
            if (type === 'text') {
                if (color.value) {
                    editor.chain().focus().setColor(color.value).run()
                } else {
                    editor.chain().focus().unsetColor().run()
                }
            } else {
                if (color.value) {
                    editor.chain().focus().setHighlight({ color: color.value }).run()
                } else {
                    editor.chain().focus().unsetHighlight().run()
                }
            }
            dropdown.style.display = 'none'
        }
        dropdown.appendChild(option)
    })

    button.onclick = (e) => {
        e.preventDefault()
        e.stopPropagation()
        // Close other dropdowns
        container.querySelectorAll('.hamlet-rt-dropdown-menu').forEach(d => {
            if (d !== dropdown) d.style.display = 'none'
        })
        dropdown.style.display = dropdown.style.display === 'none' ? 'flex' : 'none'
    }

    wrapper.appendChild(button)
    wrapper.appendChild(dropdown)
    return wrapper
}

/**
 * Creates the toolbar with all formatting options.
 */
function createToolbar(editor, container) {
    const toolbar = document.createElement('div')
    toolbar.className = 'hamlet-rt-toolbar'

    // Close dropdowns when clicking outside
    document.addEventListener('click', (e) => {
        if (!toolbar.contains(e.target)) {
            toolbar.querySelectorAll('.hamlet-rt-dropdown-menu').forEach(d => {
                d.style.display = 'none'
            })
        }
    })

    const buttonGroups = [
        // Text formatting
        [
            { label: 'B', cmd: () => editor.chain().focus().toggleBold().run(), active: () => editor.isActive('bold'), title: 'Bold (Ctrl+B)' },
            { label: 'I', cmd: () => editor.chain().focus().toggleItalic().run(), active: () => editor.isActive('italic'), title: 'Italic (Ctrl+I)' },
            { label: '{ }', cmd: () => editor.chain().focus().toggleCode().run(), active: () => editor.isActive('code'), title: 'Inline Code', className: 'code-btn' },
            { label: '🔗', cmd: () => {
                const url = window.prompt('Enter URL:')
                if (url) {
                    editor.chain().focus().setLink({ href: url }).run()
                } else if (url === '') {
                    editor.chain().focus().unsetLink().run()
                }
            }, active: () => editor.isActive('link'), title: 'Link' },
            { label: '🖼', cmd: () => {
                const input = document.createElement('input')
                input.type = 'file'
                input.accept = 'image/*'
                input.onchange = () => {
                    const file = input.files[0]
                    if (!file) return
                    uploadAndInsertImage(editor, file, container)
                }
                input.click()
            }, active: () => false, title: 'Image' },
        ],
        // Headings
        [
            { label: 'H1', cmd: () => editor.chain().focus().toggleHeading({ level: 1 }).run(), active: () => editor.isActive('heading', { level: 1 }), title: 'Heading 1' },
            { label: 'H2', cmd: () => editor.chain().focus().toggleHeading({ level: 2 }).run(), active: () => editor.isActive('heading', { level: 2 }), title: 'Heading 2' },
            { label: 'H3', cmd: () => editor.chain().focus().toggleHeading({ level: 3 }).run(), active: () => editor.isActive('heading', { level: 3 }), title: 'Heading 3' },
        ],
        // Lists and blocks
        [
            { label: '•', cmd: () => editor.chain().focus().toggleBulletList().run(), active: () => editor.isActive('bulletList'), title: 'Bullet List' },
            { label: '1.', cmd: () => editor.chain().focus().toggleOrderedList().run(), active: () => editor.isActive('orderedList'), title: 'Numbered List' },
            { label: '"', cmd: () => editor.chain().focus().toggleBlockquote().run(), active: () => editor.isActive('blockquote'), title: 'Quote' },
        ],
        // Text alignment - using CSS-based line icons
        [
            { label: '<span class="align-icon align-icon-left"></span>', cmd: () => editor.chain().focus().setTextAlign('left').run(), active: () => editor.isActive({ textAlign: 'left' }), title: 'Align Left' },
            { label: '<span class="align-icon align-icon-center"></span>', cmd: () => editor.chain().focus().setTextAlign('center').run(), active: () => editor.isActive({ textAlign: 'center' }), title: 'Align Center' },
            { label: '<span class="align-icon align-icon-right"></span>', cmd: () => editor.chain().focus().setTextAlign('right').run(), active: () => editor.isActive({ textAlign: 'right' }), title: 'Align Right' },
            { label: '<span class="align-icon align-icon-justify"></span>', cmd: () => editor.chain().focus().setTextAlign('justify').run(), active: () => editor.isActive({ textAlign: 'justify' }), title: 'Justify' },
        ],
    ]

    // Add button groups with separators
    buttonGroups.forEach((group, groupIndex) => {
        group.forEach((btn, i) => {
            const button = document.createElement('button')
            button.type = 'button'
            button.innerHTML = btn.label
            button.title = btn.title
            button.className = 'hamlet-rt-btn' + (btn.className ? ' ' + btn.className : '')
            button.dataset.groupIndex = groupIndex
            button.dataset.index = i
            button.onclick = (e) => {
                e.preventDefault()
                btn.cmd()
            }
            toolbar.appendChild(button)
        })

        // Add separator between groups (except after last group)
        if (groupIndex < buttonGroups.length - 1) {
            const separator = document.createElement('span')
            separator.className = 'hamlet-rt-separator'
            toolbar.appendChild(separator)
        }
    })

    // Add color dropdowns
    const separator = document.createElement('span')
    separator.className = 'hamlet-rt-separator'
    toolbar.appendChild(separator)

    toolbar.appendChild(createColorDropdown(editor, 'text', TEXT_COLORS, toolbar))
    toolbar.appendChild(createColorDropdown(editor, 'highlight', HIGHLIGHT_COLORS, toolbar))

    // Update button active states on selection change
    const updateActiveStates = () => {
        toolbar.querySelectorAll('.hamlet-rt-btn:not(.hamlet-rt-dropdown-btn)').forEach((b) => {
            const groupIdx = parseInt(b.dataset.groupIndex, 10)
            const idx = parseInt(b.dataset.index, 10)
            if (!isNaN(groupIdx) && !isNaN(idx) && buttonGroups[groupIdx] && buttonGroups[groupIdx][idx]) {
                b.classList.toggle('active', buttonGroups[groupIdx][idx].active())
            }
        })
    }

    editor.on('selectionUpdate', updateActiveStates)
    editor.on('transaction', updateActiveStates)

    container.appendChild(toolbar)
}

/**
 * Creates a TipTap rich text editor in the specified container.
 *
 * @param {Object} options
 * @param {string} options.elementId - The ID of the container element
 * @param {string} options.initialContent - Initial content as JSON string (ProseMirror format)
 * @param {function} options.onChange - Callback when content changes, receives JSON string
 * @param {string} [options.uploadEndpoint] - Custom upload endpoint URL (default: '/api/blobs')
 * @returns {Editor|null} The TipTap editor instance, or null if container not found
 */
export function createRichTextEditor({ elementId, initialContent, onChange, uploadEndpoint }) {
    const container = document.getElementById(elementId)
    if (!container) {
        console.warn(`[hamlet-rt] Container not found: ${elementId}`)
        return null
    }

    // Destroy existing editor if any
    destroyRichTextEditor(elementId)

    const editorEl = document.createElement('div')
    editorEl.className = 'hamlet-rt-content'

    // Parse initial content (accepts JSON string or object)
    let content = { type: 'doc', content: [{ type: 'paragraph' }] }
    if (initialContent) {
        if (typeof initialContent === 'string') {
            try {
                content = JSON.parse(initialContent)
            } catch (e) {
                console.warn(`[hamlet-rt] Failed to parse initial content:`, e)
            }
        } else if (typeof initialContent === 'object') {
            content = initialContent
        }
    }

    const editor = new Editor({
        element: editorEl,
        extensions: [
            StarterKit,
            Link.configure({
                openOnClick: false,
                HTMLAttributes: {
                    class: 'hamlet-rt-link',
                },
            }),
            Image.extend({
                addAttributes() {
                    return {
                        ...this.parent?.(),
                        width: {
                            default: null,
                            parseHTML: el => el.style.width || el.getAttribute('width') || null,
                            renderHTML: attrs => attrs.width ? { style: `width: ${attrs.width}` } : {},
                        },
                        'data-upload-id': {
                            default: null,
                            parseHTML: el => el.getAttribute('data-upload-id'),
                            renderHTML: attrs => attrs['data-upload-id'] ? { 'data-upload-id': attrs['data-upload-id'] } : {},
                        },
                    }
                },
                addNodeView() {
                    return ({ node, view, getPos }) => createResizableImageView(node, view, getPos)
                },
            }).configure({
                inline: false,
            }),
            TextAlign.configure({
                types: ['heading', 'paragraph'],
                alignments: ['left', 'center', 'right', 'justify'],
            }),
            TextStyle,
            Color,
            Highlight.configure({
                multicolor: true,
            }),
        ],
        content,
        editorProps: {
            handleDrop(view, event, slice, moved) {
                if (moved) return false // internal drag, let ProseMirror handle it
                const files = getImageFiles(event.dataTransfer)
                if (files.length === 0) return false
                event.preventDefault()

                const coords = view.posAtCoords({ left: event.clientX, top: event.clientY })
                const insertPos = coords ? coords.pos : view.state.selection.anchor

                for (const file of files) {
                    uploadAndInsertImage(editor, file, container, insertPos)
                }
                return true
            },
            handlePaste(view, event) {
                const files = getImageFiles(event.clipboardData)
                if (files.length === 0) return false
                event.preventDefault()

                for (const file of files) {
                    uploadAndInsertImage(editor, file, container)
                }
                return true
            },
        },
        onUpdate: ({ editor }) => {
            if (onChange) {
                onChange(JSON.stringify(editor.getJSON()))
            }
        }
    })

    container.classList.add('hamlet-rt-editor')
    if (uploadEndpoint) {
        container._hamletUploadEndpoint = uploadEndpoint
    }
    createToolbar(editor, container)
    container.appendChild(editorEl)

    editors.set(elementId, editor)
    return editor
}

/**
 * Destroys a TipTap editor instance.
 *
 * @param {string} elementId - The ID of the container element
 */
export function destroyRichTextEditor(elementId) {
    const editor = editors.get(elementId)
    if (editor) {
        editor.destroy()
        editors.delete(elementId)
    }
    const container = document.getElementById(elementId)
    if (container) {
        container.innerHTML = ''
        container.classList.remove('hamlet-rt-editor')
    }
}

/**
 * Gets an existing editor instance by element ID.
 *
 * @param {string} elementId - The ID of the container element
 * @returns {Editor|undefined} The editor instance, or undefined if not found
 */
export function getEditor(elementId) {
    return editors.get(elementId)
}

/**
 * Safely get editor content as JSON string.
 * Returns empty string if editor not found.
 */
export function getEditorContentJSON(elementId) {
    const editor = editors.get(elementId)
    if (!editor) {
        console.warn(`[hamlet-rt] getEditorContentJSON: editor not found: ${elementId}`, 'registered editors:', [...editors.keys()])
        return ''
    }
    return JSON.stringify(editor.getJSON())
}

/**
 * Updates the content of an existing editor.
 *
 * @param {string} elementId - The ID of the container element
 * @param {string} content - New content as JSON string
 */
export function setEditorContent(elementId, content) {
    const editor = editors.get(elementId)
    if (editor && content) {
        if (typeof content === 'string') {
            try {
                editor.commands.setContent(JSON.parse(content))
            } catch (e) {
                console.warn(`[hamlet-rt] Failed to set content:`, e)
            }
        } else if (typeof content === 'object') {
            editor.commands.setContent(content)
        }
    }
}

// Store for viewer instances (separate from editors)
const viewers = new Map()

/**
 * Creates a read-only TipTap viewer for displaying rich content.
 * No toolbar, not editable - just renders the content with full formatting.
 *
 * @param {Object} options
 * @param {string} options.elementId - The ID of the container element
 * @param {string} options.content - Content as JSON string (ProseMirror format)
 * @returns {Editor|null} The TipTap editor instance (read-only), or null if container not found
 */
export function createRichTextViewer({ elementId, content }) {
    const container = document.getElementById(elementId)
    if (!container) {
        console.warn(`[hamlet-rt] Viewer container not found: ${elementId}`)
        return null
    }

    // Destroy existing viewer if any
    destroyRichTextViewer(elementId)

    // Parse content (accepts JSON string or object)
    // Backward compat: if it's a plain text string that doesn't parse as JSON,
    // wrap it in a ProseMirror doc structure
    let parsedContent = { type: 'doc', content: [{ type: 'paragraph' }] }
    if (content) {
        if (typeof content === 'string') {
            const trimmed = content.trimStart()
            if (trimmed.startsWith('{') || trimmed.startsWith('[')) {
                try {
                    parsedContent = JSON.parse(content)
                } catch (e) {
                    // Looked like JSON but wasn't — treat as plain text
                    parsedContent = {
                        type: 'doc',
                        content: [{ type: 'paragraph', content: [{ type: 'text', text: content }] }]
                    }
                }
            } else {
                // Plain text — wrap in ProseMirror doc
                parsedContent = {
                    type: 'doc',
                    content: [{ type: 'paragraph', content: [{ type: 'text', text: content }] }]
                }
            }
        } else if (typeof content === 'object') {
            parsedContent = content
        }
    }

    const viewer = new Editor({
        element: container,
        extensions: [
            StarterKit,
            Link.configure({
                openOnClick: true,
                HTMLAttributes: {
                    class: 'hamlet-rt-link',
                    target: '_blank',
                    rel: 'noopener noreferrer',
                },
            }),
            Image.extend({
                addAttributes() {
                    return {
                        ...this.parent?.(),
                        width: {
                            default: null,
                            parseHTML: el => el.style.width || el.getAttribute('width') || null,
                            renderHTML: attrs => attrs.width ? { style: `width: ${attrs.width}` } : {},
                        },
                    }
                },
            }).configure({
                inline: false,
            }),
            TextAlign.configure({
                types: ['heading', 'paragraph'],
                alignments: ['left', 'center', 'right', 'justify'],
            }),
            TextStyle,
            Color,
            Highlight.configure({
                multicolor: true,
            }),
        ],
        content: parsedContent,
        editable: false,
    })

    container.classList.add('hamlet-rt-viewer')
    viewers.set(elementId, viewer)
    return viewer
}

/**
 * Destroys a TipTap viewer instance.
 *
 * @param {string} elementId - The ID of the container element
 */
export function destroyRichTextViewer(elementId) {
    const viewer = viewers.get(elementId)
    if (viewer) {
        viewer.destroy()
        viewers.delete(elementId)
    }
    const container = document.getElementById(elementId)
    if (container) {
        container.innerHTML = ''
        container.classList.remove('hamlet-rt-viewer')
    }
}

/**
 * Updates the content of an existing viewer.
 *
 * @param {string} elementId - The ID of the container element
 * @param {string} content - New content as JSON string
 */
export function setViewerContent(elementId, content) {
    const viewer = viewers.get(elementId)
    if (viewer && content) {
        if (typeof content === 'string') {
            try {
                viewer.commands.setContent(JSON.parse(content))
            } catch (e) {
                console.warn(`[hamlet-rt] Failed to set viewer content:`, e)
            }
        } else if (typeof content === 'object') {
            viewer.commands.setContent(content)
        }
    }
}

// =============================================================================
// PLAIN TEXT EXTRACTION
// =============================================================================

export function extractPlainText(jsonString) {
    if (!jsonString) return '';
    try {
        const doc = JSON.parse(typeof jsonString === 'string' ? jsonString : JSON.stringify(jsonString));
        return walkText(doc);
    } catch (e) {
        return jsonString; // plain text fallback
    }
}

function walkText(node) {
    if (node.text) return node.text;
    if (!node.content) return '';
    return node.content.map(walkText).join('');
}
