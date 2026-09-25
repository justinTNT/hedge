// Deployment boundary only: Hedge sessions, OAuth and roles remain independent.
const encoder = new TextEncoder();

async function sameCredential(actual, expected) {
  const hashes = await Promise.all([actual, expected].map(value =>
    crypto.subtle.digest('SHA-256', encoder.encode(value))));
  const a = new Uint8Array(hashes[0]), b = new Uint8Array(hashes[1]);
  let difference = 0;
  for (let i = 0; i < a.length; i++) difference |= a[i] ^ b[i];
  return difference === 0;
}

function privateResponse(response) {
  // Copy Headers as Headers: preserve separate Set-Cookie headers on OAuth responses.
  const result = new Response(response.body, response);
  result.headers.set('Cache-Control', 'private, no-store');
  result.headers.set('X-Robots-Tag', 'noindex, nofollow, noarchive');
  result.headers.set('Referrer-Policy', 'no-referrer');
  result.headers.set('X-Content-Type-Options', 'nosniff');
  result.headers.set('X-Frame-Options', 'DENY');
  result.headers.set('Vary', [result.headers.get('Vary'), 'Authorization'].filter(Boolean).join(', '));
  return result;
}

export function protectPreview(application) {
  return {
    async fetch(request, env, context) {
      if (new URL(request.url).protocol !== 'https:')
        return privateResponse(new Response('HTTPS required', {status: 403}));
      if (typeof env.PREVIEW_PASSWORD !== 'string' || env.PREVIEW_PASSWORD.length < 32)
        return privateResponse(new Response('Private preview unavailable', {status: 503}));

      const header = request.headers.get('Authorization') || '';
      let credential = '';
      const encoded = header.length <= 1024 && /^Basic ([A-Za-z0-9+/]+={0,2})$/i.exec(header);
      if (encoded) {
        try { credential = atob(encoded[1]); } catch { /* malformed credentials stay denied */ }
      }
      if (!await sameCredential(credential, `preview:${env.PREVIEW_PASSWORD}`))
        return privateResponse(new Response('Private preview', {
          status: 401,
          headers: {'WWW-Authenticate': 'Basic realm="Native Plants private preview", charset="UTF-8"'},
        }));

      const headers = new Headers(request.headers);
      headers.delete('Authorization');
      return privateResponse(await application.fetch(new Request(request, {headers}), env, context));
    },
  };
}
