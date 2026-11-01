// Deployment boundary only: Hedge sessions, OAuth and roles remain independent.
const encoder = new TextEncoder();

// The OAuth handshake stays outside the credential check. The Android app completes Google/GitHub
// login in the system browser, which cannot carry the app's native credential and must not be
// challenged for the browser password (a Chrome Custom Tab either stalls on the challenge or shows
// the 401 body). These GET routes expose no catalogue content: `login` only redirects to the
// provider, `callback` needs the provider's code plus Hedge's signed state and guest cookie, and
// `return` needs a verified Hedge cookie. Every page, asset, media file and data API stays gated.
const handshake = /^\/api\/(?:auth\/[A-Za-z0-9_-]+\/(?:login|callback)|mobile\/return)$/;
const isHandshake = (request, url) => request.method === 'GET' && handshake.test(url.pathname);

// The Android app is recognised by a fixed client marker, not by the preview password: the password is
// the web's outer boundary, and the APK, handed out directly, is the device's. The marker is not a
// secret; anyone holding the APK could send it, which is exactly the access the APK grants. Hedge's own
// sessions, roles and the verified sign-in that notes and photographs need are unchanged behind it.
const APP_MARKER = 'native-plants-app';
const isApp = request => request.headers.get('X-Preview-Client') === APP_MARKER;

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
  result.headers.set('Vary', [result.headers.get('Vary'), 'Authorization', 'X-Preview-Authorization'].filter(Boolean).join(', '));
  return result;
}

export function protectPreview(application) {
  return {
    async fetch(request, env, context) {
      const url = new URL(request.url);
      if (url.protocol !== 'https:')
        return privateResponse(new Response('HTTPS required', {status: 403}));
      if (typeof env.PREVIEW_PASSWORD !== 'string' || env.PREVIEW_PASSWORD.length < 32)
        return privateResponse(new Response('Private preview unavailable', {status: 503}));

      const nativeCredential = request.headers.get('X-Preview-Authorization');
      const fromApp = isApp(request) || nativeCredential !== null;
      if (!isHandshake(request, url) && !isApp(request)) {
        const header = nativeCredential ?? request.headers.get('Authorization') ?? '';
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
      }

      // Hedge never sees the preview credential or the app marker: both are removed, and a browser's
      // Basic header (checked above, or merely cached by the browser on a handshake route) is not a
      // bearer. The app's own Authorization is its Hedge bearer and stays.
      const headers = new Headers(request.headers);
      headers.delete('X-Preview-Authorization');
      headers.delete('X-Preview-Client');
      if (!fromApp) headers.delete('Authorization');
      return privateResponse(await application.fetch(new Request(request, {headers}), env, context));
    },
  };
}
