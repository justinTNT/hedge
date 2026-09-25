import { storedAdminKey, readCapabilities, subscribeCredentials } from './access-client.mjs';

export function showCapabilities(root, access) {
  for (const element of root.querySelectorAll('[data-plants-access]')) {
    element.hidden = element.dataset.plantsAccess === 'review' ? access?.CanReview !== true : access?.CanEditCatalogue !== true;
  }
}

// The shared admin persists its owner key in this document; observe that value without
// changing the framework's login flow. Permissions are always confirmed by the server.
export function mountAdminAccess(root = document, host = window) {
  let epoch=0, key=storedAdminKey(), lastCheck=0;
  const hide=()=>showCapabilities(root,null);
  async function refresh() {
    const attempt=++epoch;
    const currentKey=storedAdminKey();
    if (currentKey!==key) hide();
    key=currentKey; lastCheck=Date.now();
    try {
      const access=await readCapabilities(currentKey);
      if (attempt===epoch && currentKey===storedAdminKey()) showCapabilities(root,access);
    } catch { if (attempt===epoch) hide(); }
  }
  const invalidate=()=>{ ++epoch; hide(); void refresh(); };
  const unsubscribe=subscribeCredentials(invalidate);
  const visible=()=>{ if (!root.hidden) invalidate(); };
  hide(); void refresh();
  host.addEventListener('focus',invalidate);
  host.addEventListener('hedge:session-cleared',invalidate);
  root.addEventListener('visibilitychange',visible);
  const timer=host.setInterval(()=>{
    if (!root.hidden && Date.now()-lastCheck>=15000) void refresh();
  },15000);
  return ()=>{
    ++epoch; hide(); host.clearInterval(timer);
    unsubscribe(); host.removeEventListener('focus',invalidate);
    host.removeEventListener('hedge:session-cleared',invalidate); root.removeEventListener('visibilitychange',visible);
  };
}

if (typeof document!=='undefined' && document.querySelector('[data-plants-access]')) mountAdminAccess();
