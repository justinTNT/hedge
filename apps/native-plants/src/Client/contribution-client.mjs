const base = () => window.BASE_PATH || '';
export function uuid() { return crypto.randomUUID(); }

function render(image, edge) {
  const factor = Math.min(1, edge / Math.max(image.naturalWidth, image.naturalHeight));
  const canvas = document.createElement('canvas');
  canvas.width = Math.max(1, Math.round(image.naturalWidth * factor));
  canvas.height = Math.max(1, Math.round(image.naturalHeight * factor));
  const ctx = canvas.getContext('2d'); ctx.fillStyle = '#fff'; ctx.fillRect(0, 0, canvas.width, canvas.height);
  ctx.drawImage(image, 0, 0, canvas.width, canvas.height);
  return new Promise((resolve, reject) => canvas.toBlob(blob => {
    canvas.width = canvas.height = 1;
    blob ? resolve(blob) : reject(new Error('This photograph could not be prepared.'));
  }, 'image/jpeg', .87));
}
export async function upload(plantId, id, file, viewer) {
  if (!file || !['image/jpeg','image/png','image/webp'].includes(file.type))
    throw new Error('Choose a JPEG, PNG or WebP photograph.');
  if (file.size > 30 * 1024 * 1024) throw new Error('Choose a photograph smaller than 30 MB.');
  // Capture the session before asynchronous image processing; logout cancels the eventual write.
  return window.HedgeGuest.withSessionRequest(async () => {
    const url = URL.createObjectURL(file), image = new Image();
    try {
      image.src = url; await image.decode();
      if (!image.naturalWidth || !image.naturalHeight || image.naturalWidth * image.naturalHeight > 80000000)
        throw new Error('Choose a photograph up to 80 megapixels.');
      const full = await render(image, 3200), thumb = await render(image, 480);
      const data = new FormData(); data.append('image', full, 'image.jpg'); data.append('thumbnail', thumb, 'thumbnail.jpg');
      const response = await fetch(base() + '/api/plants/personal/' + encodeURIComponent(plantId) + '/photos/' + id, {
        method: 'POST', credentials: 'same-origin', cache: 'no-store', body: data,
        headers: { 'X-Contribution-Viewer': viewer }
      });
      return {Status:response.status, Body:await response.text()};
    } finally { URL.revokeObjectURL(url); }
  });
}
export function reviewImage(path, adminKey) {
  return window.HedgeGuest.withSessionRequest(async () => {
    const response = await fetch(base() + path, { credentials: 'same-origin', cache: 'no-store', headers: adminKey ? {'X-Admin-Key': adminKey} : {} });
    if (!response.ok) throw new Error('This photograph is no longer available for review.');
    return URL.createObjectURL(await response.blob());
  });
}
export function releaseImage(url) { if (url?.startsWith('blob:')) URL.revokeObjectURL(url); }
