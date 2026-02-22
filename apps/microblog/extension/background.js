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

  return {
    sites: data.sites || [],
    activeSiteIndex: data.activeSiteIndex || 0,
  }
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
