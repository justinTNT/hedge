// App-owned JPEG ingestion. The browser renders orientation and scales before upload;
// the server independently checks the container, dimensions, byte budget and metadata.
export function cleanJpeg(input, maxEdge = 3200) {
  const b = new Uint8Array(input);
  const bad = () => { throw new Error('Use a valid JPEG photograph.'); };
  if (b.length < 20 || b[0] !== 255 || b[1] !== 216) bad();
  const parts = [b.subarray(0, 2)];
  let pos = 2, width = 0, height = 0, scans = 0, ended = false;
  while (pos < b.length) {
    const start = pos;
    if (b[pos++] !== 255) bad();
    while (b[pos] === 255) pos++;
    const marker = b[pos++];
    if (marker === 217) { parts.push(b.subarray(start, pos)); ended = true; break; }
    if (marker === 216 || marker === 0 || marker === 1 || marker >= 208 && marker <= 215) bad();
    if (pos + 2 > b.length) bad();
    const len = (b[pos] << 8) | b[pos + 1];
    if (len < 2 || pos + len > b.length) bad();
    if ([192, 193, 194].includes(marker)) {
      if (len < 8 || width) bad();
      height = (b[pos + 3] << 8) | b[pos + 4];
      width = (b[pos + 5] << 8) | b[pos + 6];
      if (!width || !height || width > maxEdge || height > maxEdge || b[pos + 2] !== 8) bad();
    } else if (marker >= 192 && marker <= 207 && ![196, 200, 204].includes(marker)) bad();
    // Drop APP metadata (EXIF/GPS/XMP/IPTC included) and comments. Keep ICC colour profiles.
    if (!(marker >= 224 && marker <= 239 && marker !== 226) && marker !== 254)
      parts.push(b.subarray(start, pos + len));
    pos += len;
    if (marker === 218) {
      if (!width) bad();
      scans++;
      const scanStart = pos;
      while (pos < b.length) {
        if (b[pos] !== 255) { pos++; continue; }
        let next = pos + 1;
        while (b[next] === 255) next++;
        if (b[next] === 0 || b[next] >= 208 && b[next] <= 215) { pos = next + 1; continue; }
        break;
      }
      parts.push(b.subarray(scanStart, pos));
    }
  }
  if (!ended || !scans || !width || pos !== b.length) bad();
  const bytes = new Uint8Array(parts.reduce((n, p) => n + p.length, 0));
  let at = 0; for (const p of parts) { bytes.set(p, at); at += p.length; }
  return { bytes, width, height };
}

export async function readPhotoUpload(request) {
  if (!request.headers.get('Content-Type')?.startsWith('multipart/form-data'))
    throw new Error('Choose a photograph to upload.');
  // Enforce the request cap while streaming, including requests without Content-Length.
  const reader = request.body?.getReader();
  if (!reader) throw new Error('Choose a photograph to upload.');
  const chunks = []; let size = 0;
  try {
    while (true) {
      const { done, value } = await reader.read(); if (done) break;
      size += value.byteLength;
      if (size > 6 * 1024 * 1024) { await reader.cancel(); throw new Error('Photograph exceeds the 6 MB upload limit.'); }
      chunks.push(value);
    }
  } finally { reader.releaseLock(); }
  const data = await new Response(new Blob(chunks), { headers: { 'Content-Type': request.headers.get('Content-Type') } }).formData();
  const image = data.get('image'), thumbnail = data.get('thumbnail');
  if (!(image instanceof Blob) || !(thumbnail instanceof Blob) || image.size > 5 * 1024 * 1024 || thumbnail.size > 512 * 1024)
    throw new Error('Photographs must be at most 5 MB, with a thumbnail at most 512 KB.');
  const full = cleanJpeg(await image.arrayBuffer());
  const thumb = cleanJpeg(await thumbnail.arrayBuffer(), 480);
  if (Math.abs(full.width / full.height - thumb.width / thumb.height) > 0.03)
    throw new Error('The thumbnail must have the photograph’s proportions.');
  return { image: full.bytes, thumbnail: thumb.bytes, width: full.width, height: full.height,
    size: full.bytes.length + thumb.bytes.length };
}

async function boundedJsonText(request) {
  if (!request.headers.get('Content-Type')?.startsWith('application/json')) throw new Error('Send a JSON request.');
  const reader = request.body?.getReader(); if (!reader) throw new Error('Missing request.');
  const chunks = []; let size = 0;
  try {
    while (true) {
      const { done, value } = await reader.read(); if (done) break;
      size += value.length;
      if (size > 24000) { await reader.cancel(); throw new Error('This note is too long.'); }
      chunks.push(value);
    }
  } finally { reader.releaseLock(); }
  return new Blob(chunks).text();
}

export async function boundedJsonRequest(request) {
  const text = await boundedJsonText(request);
  return new Request(request.url, {method: request.method, headers: request.headers, body: text});
}

export async function readJson(request) {
  const text = await boundedJsonText(request);
  try {
    const value = JSON.parse(text);
    if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('Expected an object');
    return value;
  }
  catch { throw new Error('The request could not be read.'); }
}

export async function viewerToken(provider, id) {
  const hash = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(JSON.stringify([provider, id])));
  return Array.from(new Uint8Array(hash), b => b.toString(16).padStart(2, '0')).join('');
}
