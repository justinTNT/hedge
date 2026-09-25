export function storedAdminKey() {
  try { return localStorage.getItem('adminKey') || ''; } catch { return ''; }
}

export function readCapabilities(key) {
  const read = async () => {
    const response = await fetch((window.BASE_PATH || '') + '/api/plants/access', {
      headers: key ? {'X-Admin-Key': key} : {}, credentials: 'same-origin', cache: 'no-store',
    });
    if (!response.ok) throw new Error('Access could not be checked.');
    const data = await response.json();
    if (typeof data.CanEditCatalogue !== 'boolean' || typeof data.CanReview !== 'boolean')
      throw new Error('Access could not be checked.');
    return data;
  };
  return window.HedgeGuest?.withSessionRequest ? window.HedgeGuest.withSessionRequest(read) : read();
}
