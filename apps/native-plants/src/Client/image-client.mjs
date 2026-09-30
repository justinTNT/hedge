export function remoteImage(path) {
  return !!window.API_ORIGIN && path?.startsWith('/') && !path.startsWith('//');
}
export async function imageUrl(path) {
  if (!remoteImage(path)) return path;
  const read = async () => {
    const bearer=window.HedgeGuest.currentBearer();
    const response=await fetch(window.API_ORIGIN+path,{cache:'no-store',headers:bearer?{Authorization:'Bearer '+bearer}:{}});
    if (!response.ok) throw new Error('Photograph unavailable');
    return response.blob();
  };
  const blob=await (path.startsWith('/api/') ? window.HedgeGuest.withSessionRequest(read) : read());
  return URL.createObjectURL(blob);
}
export function releaseImage(url) { if (url?.startsWith('blob:')) URL.revokeObjectURL(url); }
export async function measurePhoto(path) {
  let url;
  try {
    url=await imageUrl(path);
    const image=new Image(); image.src=url; await image.decode();
    return [path,image.naturalWidth,image.naturalHeight];
  } catch { return [path,0,0]; }
  finally { releaseImage(url); }
}
