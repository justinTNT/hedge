/**
 * Hedge Extension — Background Service Worker
 *
 * Proxies API requests from the popup to the configured Hedge server.
 * Supports multiple site configs with migration from legacy single-URL format.
 */

async function getSitesData() {
  const data = await chrome.storage.local.get(['sites', 'activeSiteIndex', 'apiUrl'])

  // Migrate legacy single apiUrl to multi-site format
  if (data.apiUrl && !data.sites) {
    const migrated = {
      sites: [{ name: 'Default', url: data.apiUrl, key: '' }],
      activeSiteIndex: 0,
    }
    await chrome.storage.local.set(migrated)
    await chrome.storage.local.remove('apiUrl')
    return migrated
  }

  if (data.sites && data.sites.length > 0) {
    return { sites: data.sites, activeSiteIndex: data.activeSiteIndex || 0 }
  }

  // Storage empty — seed from bundled sites.json if present
  try {
    const res = await fetch(chrome.runtime.getURL('sites.json'))
    const defaults = await res.json()
    if (defaults.sites && defaults.sites.length > 0) {
      await chrome.storage.local.set({ sites: defaults.sites, activeSiteIndex: 0 })
      return { sites: defaults.sites, activeSiteIndex: 0 }
    }
  } catch {
    // No sites.json bundled — that's fine
  }

  return { sites: [], activeSiteIndex: 0 }
}

function getActiveSite(data) {
  const { sites, activeSiteIndex } = data
  if (sites.length === 0) return null
  const idx = Math.min(activeSiteIndex, sites.length - 1)
  return sites[idx]
}

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (message.type === 'api') {
    handleApiRequest(message).then(sendResponse)
    return true
  }

  if (message.type === 'captureImage') {
    handleCaptureImage(message).then(sendResponse)
    return true
  }

  if (message.type === 'getSites') {
    getSitesData().then(sendResponse)
    return true
  }

  if (message.type === 'setSites') {
    chrome.storage.local
      .set({ sites: message.sites, activeSiteIndex: message.activeSiteIndex })
      .then(() => sendResponse({ ok: true }))
    return true
  }

  if (message.type === 'getActiveSite') {
    getSitesData().then((data) => {
      sendResponse(getActiveSite(data))
    })
    return true
  }

  // Legacy support — kept briefly for transition
  if (message.type === 'setApiUrl') {
    chrome.storage.local.set({ apiUrl: message.url }).then(() => {
      sendResponse({ ok: true })
    })
    return true
  }

  if (message.type === 'getApiUrl') {
    getSitesData().then((data) => {
      const site = getActiveSite(data)
      sendResponse({ url: site ? site.url : 'http://localhost:8787' })
    })
    return true
  }
})

async function handleApiRequest({ method, path, body }) {
  try {
    const data = await getSitesData()
    const site = getActiveSite(data)
    const baseUrl = site ? site.url : 'http://localhost:8787'
    const url = baseUrl.replace(/\/+$/, '') + path

    const opts = {
      method,
      headers: { 'Content-Type': 'application/json' },
    }
    // Authoring (POST /api/blog/item) is owner-only server-side, so send the
    // configured admin key. Public reads/comments ignore it.
    if (site && site.key) {
      opts.headers['X-Admin-Key'] = site.key
    }
    if (body !== undefined) {
      opts.body = JSON.stringify(body)
    }

    const res = await fetch(url, opts)
    const text = await res.text()
    let responseData
    try {
      responseData = JSON.parse(text)
    } catch {
      responseData = text
    }

    if (!res.ok) {
      return { ok: false, status: res.status, error: responseData }
    }
    return { ok: true, data: responseData }
  } catch (err) {
    return { ok: false, error: err.message }
  }
}

/**
 * Hybrid image capture, tier 1: fetch the chosen image with the extension's
 * host permission (a readable cross-origin response, usually a cache hit from
 * the page load) and rehost the bytes to R2 via POST /api/blobs. This bypasses
 * the hotlink/Referer/cookie blocks a server re-fetch from a Cloudflare IP would
 * hit. Best-effort: the popup falls back to the original URL on any failure
 * (the server then tries its own rehost as tier 2).
 */
async function handleCaptureImage({ url }) {
  // Tier-1 is best-effort and its failure is silently swallowed by the popup
  // (falls back to the raw URL for the server to rehost). Log each failure with
  // enough detail to tell WHICH step failed — a resize CDN that gates on Origin/
  // Referer typically returns a 403 or an HTML error page here, which the /api/blobs
  // image-type gate then rejects. Grep the service-worker console for "[hedge capture]".
  try {
    const data = await getSitesData()
    const site = getActiveSite(data)
    if (!site) return { ok: false, error: 'No active site' }
    const baseUrl = site.url.replace(/\/+$/, '')

    const imgRes = await fetch(url)
    if (!imgRes.ok) {
      console.warn('[hedge capture] image fetch not ok', {
        url, status: imgRes.status, statusText: imgRes.statusText,
        contentType: imgRes.headers.get('content-type'),
      })
      return { ok: false, error: 'Image fetch failed: ' + imgRes.status }
    }
    const blob = await imgRes.blob()
    // blob.type/size is the tell: an HTML error page or an empty/opaque body here
    // is why the upload's image-type gate later rejects it.
    console.info('[hedge capture] fetched image', { url, type: blob.type, size: blob.size })

    // Derive a filename from the URL path so the stored key has a sensible name;
    // the upload's content-type gate reads the blob's MIME, not this extension.
    let name = 'image'
    try {
      const last = new URL(url).pathname.split('/').filter(Boolean).pop()
      if (last) name = last
    } catch {
      // Non-parseable URL — keep the 'image' default.
    }

    const fd = new FormData()
    fd.append('file', blob, name)

    // No Content-Type header — the browser sets the multipart boundary itself.
    const opts = { method: 'POST', body: fd, headers: {} }
    if (site.key) opts.headers['X-Admin-Key'] = site.key

    const res = await fetch(baseUrl + '/api/blobs', opts)
    const text = await res.text()
    let responseData
    try {
      responseData = JSON.parse(text)
    } catch {
      responseData = text
    }

    if (!res.ok) {
      console.warn('[hedge capture] blob upload rejected', {
        url, status: res.status, blobType: blob.type, response: responseData,
      })
      return { ok: false, status: res.status, error: responseData }
    }
    return { ok: true, data: responseData }
  } catch (err) {
    console.warn('[hedge capture] threw', { url, error: err && err.message })
    return { ok: false, error: err.message }
  }
}
