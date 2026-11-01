// Photographs on the bundled app come from the API host, not the capacitor:// WebView. The web build
// shows them as ordinary <img src> paths and never calls fetchImageBlob.
export function remoteImage(path) {
  return !!window.API_ORIGIN && path?.startsWith('/') && !path.startsWith('//');
}
// A photograph's bytes from the API host. Private personal media (/api/ paths) carries the bearer and
// runs inside the session lock, so a sign-out mid-flight is refused rather than served.
export function fetchImageBlob(path) {
  const read = async () => {
    const bearer=window.HedgeGuest.currentBearer();
    const response=await fetch((window.API_ORIGIN||'')+path,{cache:'no-store',headers:bearer?{Authorization:'Bearer '+bearer}:{}});
    if (!response.ok) throw new Error('Photograph unavailable');
    return response.blob();
  };
  return path.startsWith('/api/') ? window.HedgeGuest.withSessionRequest(read) : read();
}
