// The preview gate recognises the app by this fixed client marker, not by the web's password; the gate
// strips it, and Hedge attaches its own bearer at the API boundary. Only API-origin requests carry it.
export const APP_MARKER='native-plants-app';
export function previewFetch(fetchNative, origin) {
  return (input, init={}) => {
    const url=new URL(typeof input==='string' || input instanceof URL ? input : input.url, location.href);
    if (url.origin!==origin) return fetchNative(input,init);
    const headers=new Headers(init.headers || (input instanceof Request ? input.headers : undefined));
    headers.set('X-Preview-Client',APP_MARKER);
    return fetchNative(input,{...init,headers,cache:'no-store'});
  };
}
