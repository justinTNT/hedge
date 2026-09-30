// Deployment credential only. Hedge attaches its own bearer at the API boundary.
export function previewFetch(fetchNative, origin, credential) {
  return (input, init={}) => {
    const url=new URL(typeof input==='string' || input instanceof URL ? input : input.url, location.href);
    if (url.origin!==origin) return fetchNative(input,init);
    const headers=new Headers(init.headers || (input instanceof Request ? input.headers : undefined));
    headers.set('X-Preview-Authorization',credential);
    return fetchNative(input,{...init,headers,cache:'no-store'});
  };
}
