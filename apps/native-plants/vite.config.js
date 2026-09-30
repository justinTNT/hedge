import { defineConfig } from 'vite';
import { resolve } from 'node:path';
export default defineConfig(({mode}) => {
  const native=mode==='native';
  const origin=process.env.API_ORIGIN || 'https://native-plants-preview.justin-8ee.workers.dev';
  if (native && (new URL(origin).origin!==origin || !origin.startsWith('https://')))
    throw new Error('API_ORIGIN must be an HTTPS origin, without a path or trailing slash.');
  return {
    publicDir:native ? false : 'public',
    define:native ? {'__PLANTS_API_ORIGIN__':JSON.stringify(origin)} : {},
    plugins:native ? [{name:'native-plants-shell',transformIndexHtml:{order:'pre',handler(html) {
      return html.replace('<script src="/lib/guest-session.js"></script>','')
        .replace('/dist/client/Main.js','/mobile/entry.mjs')
        .replace('href="/favicon.svg"','href="/public/favicon.svg"')
        .replace('width=device-width, initial-scale=1','width=device-width, initial-scale=1, viewport-fit=cover');
    }}}] : [],
    build:{outDir:native ? 'mobile/www' : '_site',rollupOptions:{input:native
      ? {main:resolve(import.meta.dirname,'index.html')}
      : {main:resolve(import.meta.dirname,'index.html'),admin:resolve(import.meta.dirname,'admin.html')}}},
    server:{port:3038,host:native ? '0.0.0.0' : '127.0.0.1',fs:{allow:['../..']},proxy:{'/api':'http://127.0.0.1:8794','/blobs':'http://127.0.0.1:8794'}}
  };
});
