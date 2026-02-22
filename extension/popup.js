/**
 * Hedge Extension — Popup
 *
 * Extracts page data from the active tab, presents TipTap editors for
 * extract and comment, and submits items to the Hedge API via the
 * background service worker.
 */

import { Editor } from '@tiptap/core'
import StarterKit from '@tiptap/starter-kit'
import Link from '@tiptap/extension-link'

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------

let extractEditor = null
let commentEditor = null
let selectedImage = null
let pageUrl = ''

let sites = []
let activeSiteIndex = 0
let showConfig = false

// ---------------------------------------------------------------------------
// TipTap editor factory
// ---------------------------------------------------------------------------

const EXTENSIONS = [
  StarterKit,
  Link.configure({ openOnClick: false }),
]

function createToolbar(editor, toolbarEl) {
  const buttons = [
    { label: 'B', cmd: () => editor.chain().focus().toggleBold().run(), active: () => editor.isActive('bold'), title: 'Bold' },
    { label: 'I', cmd: () => editor.chain().focus().toggleItalic().run(), active: () => editor.isActive('italic'), title: 'Italic' },
    { label: '{}', cmd: () => editor.chain().focus().toggleCode().run(), active: () => editor.isActive('code'), title: 'Code' },
    null, // separator
    { label: 'H2', cmd: () => editor.chain().focus().toggleHeading({ level: 2 }).run(), active: () => editor.isActive('heading', { level: 2 }), title: 'Heading' },
    { label: '"', cmd: () => editor.chain().focus().toggleBlockquote().run(), active: () => editor.isActive('blockquote'), title: 'Quote' },
    { label: '•', cmd: () => editor.chain().focus().toggleBulletList().run(), active: () => editor.isActive('bulletList'), title: 'List' },
    null,
    {
      label: '🔗', title: 'Link',
      cmd: () => {
        const url = window.prompt('URL:')
        if (url) editor.chain().focus().setLink({ href: url }).run()
        else if (url === '') editor.chain().focus().unsetLink().run()
      },
      active: () => editor.isActive('link'),
    },
  ]

  for (const btn of buttons) {
    if (btn === null) {
      const sep = document.createElement('div')
      sep.className = 'sep'
      toolbarEl.appendChild(sep)
      continue
    }
    const el = document.createElement('button')
    el.type = 'button'
    el.textContent = btn.label
    el.title = btn.title
    el.addEventListener('click', (e) => {
      e.preventDefault()
      btn.cmd()
    })
    toolbarEl.appendChild(el)

    // Track for active-state updates
    el._active = btn.active
  }

  // Update active states on editor changes
  const update = () => {
    for (const b of toolbarEl.querySelectorAll('button')) {
      if (b._active) b.classList.toggle('active', b._active())
    }
  }
  editor.on('selectionUpdate', update)
  editor.on('transaction', update)
}

function createEditor(contentEl, toolbarEl, initialContent) {
  const editor = new Editor({
    element: contentEl,
    extensions: EXTENSIONS,
    content: initialContent || { type: 'doc', content: [{ type: 'paragraph' }] },
  })
  createToolbar(editor, toolbarEl)
  return editor
}

// ---------------------------------------------------------------------------
// Page data extraction
// ---------------------------------------------------------------------------

async function extractPageData() {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true })
  if (!tab) return {}

  pageUrl = tab.url || ''

  // Execute script in the active tab to get selection + images
  let results
  try {
    results = await chrome.scripting.executeScript({
      target: { tabId: tab.id },
      func: () => {
        const sel = window.getSelection()
        let selectionHtml = ''
        let selectionText = ''
        if (sel && sel.rangeCount > 0 && !sel.isCollapsed) {
          selectionText = sel.toString()
          const container = document.createElement('div')
          for (let i = 0; i < sel.rangeCount; i++) {
            container.appendChild(sel.getRangeAt(i).cloneContents())
          }
          selectionHtml = container.innerHTML
        }

        const images = Array.from(document.images)
          .map((img) => img.src)
          .filter((src) => src.startsWith('http'))
          // Deduplicate
          .filter((src, i, arr) => arr.indexOf(src) === i)
          // Skip tiny tracking pixels (heuristic: skip data URIs already filtered, skip known tiny sizes)
          .slice(0, 50) // cap at 50 images

        return { selectionHtml, selectionText, images }
      },
    })
  } catch {
    // Can't inject into chrome:// or other restricted pages
    return { title: tab.title || '', url: pageUrl, selectionHtml: '', selectionText: '', images: [] }
  }

  const data = results?.[0]?.result || {}
  return {
    title: tab.title || '',
    url: pageUrl,
    selectionHtml: data.selectionHtml || '',
    selectionText: data.selectionText || '',
    images: data.images || [],
  }
}

// ---------------------------------------------------------------------------
// Convert HTML selection to TipTap JSON
// ---------------------------------------------------------------------------

function htmlToTipTapJson(html) {
  if (!html) return null

  // Create a temporary hidden editor to parse HTML into TipTap JSON
  const tempEl = document.createElement('div')
  tempEl.style.display = 'none'
  document.body.appendChild(tempEl)

  const tempEditor = new Editor({
    element: tempEl,
    extensions: EXTENSIONS,
    content: '',
  })

  tempEditor.commands.setContent(html)
  const json = tempEditor.getJSON()
  tempEditor.destroy()
  tempEl.remove()

  return json
}

// ---------------------------------------------------------------------------
// Images gallery
// ---------------------------------------------------------------------------

function renderImages(images) {
  const gallery = document.getElementById('imagesGallery')
  gallery.innerHTML = ''
  selectedImage = null

  for (const src of images) {
    const img = document.createElement('img')
    img.className = 'img-thumb'
    img.src = src
    img.title = src
    img.addEventListener('click', () => {
      // Toggle selection
      if (selectedImage === src) {
        selectedImage = null
        img.classList.remove('selected')
      } else {
        selectedImage = src
        gallery.querySelectorAll('.img-thumb').forEach((el) => el.classList.remove('selected'))
        img.classList.add('selected')
      }
    })
    // Skip broken images
    img.addEventListener('error', () => img.remove())
    gallery.appendChild(img)
  }
}

// ---------------------------------------------------------------------------
// API communication
// ---------------------------------------------------------------------------

function sendMessage(msg) {
  return new Promise((resolve) => chrome.runtime.sendMessage(msg, resolve))
}

// ---------------------------------------------------------------------------
// Status display
// ---------------------------------------------------------------------------

function setStatus(text, type) {
  const el = document.getElementById('status')
  el.textContent = text
  el.className = 'status' + (type ? ' ' + type : '')
}

// ---------------------------------------------------------------------------
// Site config UI
// ---------------------------------------------------------------------------

async function saveSites() {
  await sendMessage({ type: 'setSites', sites, activeSiteIndex })
}

function renderSiteDropdown() {
  const select = document.getElementById('siteSelect')
  select.innerHTML = ''

  if (sites.length === 0) {
    const opt = document.createElement('option')
    opt.textContent = '(no sites configured)'
    opt.disabled = true
    select.appendChild(opt)
    return
  }

  for (let i = 0; i < sites.length; i++) {
    const opt = document.createElement('option')
    opt.value = i
    opt.textContent = sites[i].name || sites[i].url
    if (i === activeSiteIndex) opt.selected = true
    select.appendChild(opt)
  }
}

function renderConfigTable() {
  const tbody = document.getElementById('configBody')
  tbody.innerHTML = ''

  for (let i = 0; i < sites.length; i++) {
    const site = sites[i]
    const tr = document.createElement('tr')

    const tdName = document.createElement('td')
    tdName.textContent = site.name || '—'
    tr.appendChild(tdName)

    const tdUrl = document.createElement('td')
    tdUrl.style.fontFamily = 'monospace'
    tdUrl.textContent = site.url
    tr.appendChild(tdUrl)

    const tdKey = document.createElement('td')
    tdKey.style.fontFamily = 'monospace'
    tdKey.textContent = site.key ? '••••' : '—'
    tr.appendChild(tdKey)

    const tdActions = document.createElement('td')
    tdActions.className = 'actions-cell'

    const adminBtn = document.createElement('button')
    adminBtn.className = 'btn-sm btn-admin'
    adminBtn.textContent = 'Admin'
    adminBtn.addEventListener('click', () => {
      const adminUrl = site.url.replace(/\/+$/, '') + '/admin.html#key=' + encodeURIComponent(site.key)
      window.open(adminUrl, '_blank')
    })
    tdActions.appendChild(adminBtn)

    const delBtn = document.createElement('button')
    delBtn.className = 'btn-sm btn-del'
    delBtn.textContent = 'Del'
    delBtn.style.marginLeft = '4px'
    delBtn.addEventListener('click', async () => {
      sites.splice(i, 1)
      if (activeSiteIndex >= sites.length) {
        activeSiteIndex = Math.max(0, sites.length - 1)
      }
      await saveSites()
      renderSiteDropdown()
      renderConfigTable()
    })
    tdActions.appendChild(delBtn)

    tr.appendChild(tdActions)
    tbody.appendChild(tr)
  }
}

function toggleConfig() {
  showConfig = !showConfig
  const section = document.getElementById('configSection')
  section.classList.toggle('hidden', !showConfig)
  if (showConfig) renderConfigTable()
}

// ---------------------------------------------------------------------------
// Submit
// ---------------------------------------------------------------------------

async function submit() {
  const title = document.getElementById('title').value.trim()
  if (!title) {
    setStatus('Title is required', 'error')
    return
  }

  const commentJson = commentEditor.getJSON()
  // Validate comment has some content
  const commentText = commentEditor.getText().trim()
  if (!commentText) {
    setStatus('Comment is required', 'error')
    return
  }

  const extractJson = extractEditor.getJSON()
  const extractText = extractEditor.getText().trim()

  const tagsRaw = document.getElementById('tags').value
  const tags = tagsRaw
    .split(',')
    .map((t) => t.trim())
    .filter(Boolean)

  const body = {
    Title: title,
    Link: pageUrl || null,
    Image: selectedImage || null,
    Extract: extractText ? JSON.stringify(extractJson) : null,
    OwnerComment: JSON.stringify(commentJson),
    Tags: tags,
  }

  const btn = document.getElementById('submitBtn')
  btn.disabled = true
  setStatus('Submitting…', '')

  const res = await sendMessage({ type: 'api', method: 'POST', path: '/api/item', body })

  btn.disabled = false

  if (res?.ok) {
    setStatus('Submitted!', 'success')
  } else {
    const msg = res?.error?.message || res?.error || 'Request failed'
    setStatus(typeof msg === 'string' ? msg : JSON.stringify(msg), 'error')
  }
}

// ---------------------------------------------------------------------------
// Init
// ---------------------------------------------------------------------------

async function init() {
  // Load sites config
  const data = await sendMessage({ type: 'getSites' })
  sites = data.sites || []
  activeSiteIndex = data.activeSiteIndex || 0

  // Render dropdown
  renderSiteDropdown()

  // If no sites, show config automatically
  if (sites.length === 0) {
    showConfig = true
    document.getElementById('configSection').classList.remove('hidden')
  }

  // Dropdown change handler
  document.getElementById('siteSelect').addEventListener('change', async (e) => {
    activeSiteIndex = parseInt(e.target.value, 10)
    await saveSites()
  })

  // Gear toggle
  document.getElementById('gearBtn').addEventListener('click', toggleConfig)

  // Add site button
  document.getElementById('addSiteBtn').addEventListener('click', async () => {
    const name = document.getElementById('addName').value.trim()
    const url = document.getElementById('addUrl').value.trim()
    const key = document.getElementById('addKey').value.trim()
    if (!url) return

    sites.push({ name: name || url, url, key })
    if (sites.length === 1) activeSiteIndex = 0
    await saveSites()
    renderSiteDropdown()
    renderConfigTable()

    // Clear inputs
    document.getElementById('addName').value = ''
    document.getElementById('addUrl').value = ''
    document.getElementById('addKey').value = ''
  })

  // Extract page data
  const pageData = await extractPageData()

  // Populate title
  document.getElementById('title').value = pageData.title || ''

  // Show URL
  document.getElementById('pageUrl').textContent = pageData.url || '—'
  pageUrl = pageData.url || ''

  // Convert selection HTML to TipTap JSON
  const extractContent = htmlToTipTapJson(pageData.selectionHtml) || undefined

  // Initialize editors
  extractEditor = createEditor(
    document.getElementById('extractEditor'),
    document.getElementById('extractToolbar'),
    extractContent,
  )

  commentEditor = createEditor(
    document.getElementById('commentEditor'),
    document.getElementById('commentToolbar'),
  )

  // Render images
  renderImages(pageData.images || [])

  // Submit handler
  document.getElementById('submitBtn').addEventListener('click', submit)
}

init()
