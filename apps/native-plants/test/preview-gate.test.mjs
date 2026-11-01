import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {protectPreview} from '../preview-gate.mjs';

const password = 'preview-test-password-at-least-thirty-two-characters';
const auth = 'Basic ' + btoa('preview:' + password);
const env = {PREVIEW_PASSWORD: password};
const request = (path, options = {}) => new Request('https://plants.test' + path, options);

test('preview denies all routes and methods before application or asset access', async () => {
  let called = 0;
  const worker = protectPreview({fetch() {called++; throw Error('Must never run');}});
  for (const path of ['/', '/admin', '/review', '/media/photo.jpg', '/assets/app.js', '/api/plants/catalogue', '/api/auth/me', '/api/auth/providers', '/blobs/private/example', '/robots.txt']) {
    for (const method of ['GET', 'HEAD', 'POST', 'OPTIONS']) {
      const response = await worker.fetch(request(path, {method}), env);
      assert.equal(response.status, 401, method + ' ' + path);
      assert.match(response.headers.get('www-authenticate'), /^Basic /);
      assert.equal(response.headers.get('set-cookie'), null);
      assert.equal(response.headers.get('cache-control'), 'private, no-store');
      assert.match(response.headers.get('x-robots-tag'), /noindex/);
    }
  }
  assert.equal(called, 0);
});

test('preview opens only the GET OAuth handshake so the Android system browser can finish login', async () => {
  const seen = [];
  const worker = protectPreview({fetch(req) {seen.push(req); return new Response(null, {status:302, headers:{Location:'nativeplants://auth?code=one-time'}});}});
  const stale = {Authorization:'Basic ' + btoa('preview:stale-browser-credential'), Cookie:'hedge_guest=session'};
  for (const path of ['/api/auth/google/login?returnTo=' + encodeURIComponent('/api/mobile/return?challenge=' + 'a'.repeat(64)),
                      '/api/auth/github/callback?code=provider&state=signed', '/api/mobile/return?challenge=' + 'a'.repeat(64)]) {
    const response = await worker.fetch(request(path, {headers:stale}), env);
    assert.equal(response.status, 302, path);
    assert.equal(response.headers.get('location'), 'nativeplants://auth?code=one-time');
    assert.equal(response.headers.get('cache-control'), 'private, no-store');
    assert.match(response.headers.get('x-robots-tag'), /noindex/);
  }
  assert.equal(seen.length, 3);
  for (const req of seen) {
    assert.equal(req.headers.get('authorization'), null, 'a cached browser credential never reaches Hedge');
    assert.equal(req.headers.get('x-preview-authorization'), null);
    assert.equal(req.headers.get('cookie'), 'hedge_guest=session');
  }
  for (const [method, path] of [['POST', '/api/auth/google/login'], ['HEAD', '/api/auth/google/callback'], ['OPTIONS', '/api/mobile/return'],
                                ['GET', '/api/auth/providers'], ['GET', '/api/auth/me'], ['GET', '/api/auth/google/logins'], ['GET', '/api/auth//login'],
                                ['GET', '/api/auth/google/login/'], ['GET', '/api/mobile/return/'], ['GET', '/api/mobile/me'], ['GET', '/api/mobile/exchange'],
                                ['GET', '/api/auth/google/login/../../plants/catalogue'], ['GET', '/api/auth/google/callback/../../../media/photo.jpg']]) {
    const response = await worker.fetch(request(path, {method}), env);
    assert.equal(response.status, 401, method + ' ' + path);
    assert.match(response.headers.get('www-authenticate'), /^Basic /);
  }
  assert.equal(seen.length, 3);
  assert.equal((await worker.fetch(new Request('http://plants.test/api/auth/google/login'), env)).status, 403);
  assert.equal((await worker.fetch(request('/api/auth/google/login'), {PREVIEW_PASSWORD:''})).status, 503);
  assert.equal(seen.length, 3);
});

test('the Android app passes by its client marker alone, which never reaches Hedge, while its bearer does', async () => {
  const seen = [];
  const worker = protectPreview({fetch(req) {seen.push(req); return new Response('ok');}});
  const app = {'X-Preview-Client':'native-plants-app', Authorization:'Bearer mobile-session-token'};
  for (const [method, path] of [['GET', '/api/plants/catalogue'], ['GET', '/api/plants/accounts'], ['GET', '/media/photo.webp'], ['POST', '/api/plants/v2/notes/entry'], ['GET', '/api/plants/v2/personal/x']]) {
    const response = await worker.fetch(request(path, {method, headers:app}), env);
    assert.equal(response.status, 200, method + ' ' + path);
    assert.equal(response.headers.get('cache-control'), 'private, no-store');
  }
  assert.equal(seen.length, 5);
  for (const req of seen) {
    assert.equal(req.headers.get('x-preview-client'), null, 'the marker is stripped');
    assert.equal(req.headers.get('authorization'), 'Bearer mobile-session-token', 'the Hedge bearer stays');
  }
  for (const headers of [{'X-Preview-Client':'native-plants'}, {'X-Preview-Client':''}, {'X-Preview-Client':'native-plants-app-2'}, {'X-Preview-Client':'Native-Plants-App'}, {'X-Native-Plants':'native-plants-app'}])
    assert.equal((await worker.fetch(request('/api/plants/catalogue', {headers}), env)).status, 401);
  assert.equal((await worker.fetch(new Request('http://plants.test/api/plants/catalogue', {headers:app}), env)).status, 403, 'HTTPS still required');
  assert.equal((await worker.fetch(request('/api/plants/catalogue', {headers:app}), {PREVIEW_PASSWORD:''})).status, 503, 'the gate still fails closed without its secret');
  assert.equal(seen.length, 5);
});

test('preview fails closed without its secret and rejects malformed or wrong credentials', async () => {
  const worker = protectPreview({fetch() {throw Error('Must never run');}});
  for (const value of [undefined, '', 'short'])
    assert.equal((await worker.fetch(request('/', {headers:{Authorization:auth}}), {PREVIEW_PASSWORD:value})).status, 503);
  for (const value of ['Basic %%%', 'Basic a', 'Bearer abc', 'Basic ' + btoa('admin:' + password), 'Basic ' + btoa('preview:wrong'), auth + 'x', 'Basic ' + 'a'.repeat(2048)])
    assert.equal((await worker.fetch(request('/', {headers:{Authorization:value}}), env)).status, 401);
  assert.equal((await worker.fetch(new Request('http://plants.test/', {headers:{Authorization:auth}}), env)).status, 403);
});

test('preview preserves Hedge cookies, OAuth redirects, uploads and logout without forwarding its password', async () => {
  const worker = protectPreview({async fetch(req, suppliedEnv, context) {
    assert.equal(suppliedEnv, env);
    assert.equal(context.marker, true);
    assert.equal(req.headers.get('authorization'), null);
    assert.equal(req.headers.get('cookie'), 'hedge_guest=session');
    assert.equal(req.headers.get('x-admin-key'), 'separate-owner-key');
    assert.equal(await req.text(), 'unchanged upload bytes');
    const headers = new Headers({Location:'/plants/example', Vary:'Cookie'});
    headers.append('Set-Cookie', 'hedge_guest=replacement; Secure; HttpOnly; SameSite=Lax');
    headers.append('Set-Cookie', 'other=value; Secure');
    return new Response(null, {status:302, headers});
  }});
  const response = await worker.fetch(request('/api/auth/google/callback', {method:'POST',
    headers:{Authorization:auth, Cookie:'hedge_guest=session', 'X-Admin-Key':'separate-owner-key'}, body:'unchanged upload bytes'}), env, {marker:true});
  assert.equal(response.status, 302);
  assert.equal(response.headers.get('location'), '/plants/example');
  assert.equal(response.headers.getSetCookie().length, 2);
  assert.match(response.headers.get('vary'), /Cookie, Authorization/);
  assert.equal(response.headers.get('referrer-policy'), 'no-referrer');
});

test('preview deployment explicitly routes every static asset through the gate and disables version URLs', () => {
  const config = readFileSync(new URL('../wrangler.toml', import.meta.url), 'utf8');
  const preview = config.slice(config.indexOf('[env.preview]'));
  assert.match(preview, /main = "worker-preview.js"/);
  assert.match(preview, /preview_urls = false/);
  assert.match(preview, /run_worker_first = true/);
  assert.match(preview, /ENVIRONMENT = "preview"/);
  const entry = readFileSync(new URL('../worker-preview.js', import.meta.url), 'utf8');
  assert.match(entry, /export default protectPreview\(application\)/);
});
