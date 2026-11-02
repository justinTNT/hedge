import { defineConfig } from 'vite';
import { resolve } from 'path';

// -- Per-deployment configuration --
// Set at build time so one branch can produce every deployment's site:
//   BASE_PATH=/x SITE_TITLE=... SITE_SLUG=... npm run build
const basePath = (process.env.BASE_PATH || '').replace(/\/$/, '');
const siteTitle = process.env.SITE_TITLE || 'Flare log';
const adminTitle = process.env.ADMIN_TITLE || 'Flare log admin';
const siteLogo = process.env.SITE_LOGO || '';
// BCP-47 locale for date formatting (read via Hedge.Tenant). "" = viewer's own locale.
const siteLocale = process.env.SITE_LOCALE || '';
// Per-deployment CSS hook: adds `tenant-<slug>` to <body> so styles.css can scope rules.
const siteSlug = process.env.SITE_SLUG || '';
// Per-deployment feature flags (comma list) — read via Hedge.Tenant.hasFeature.
const siteFeatures = process.env.SITE_FEATURES || '';

/// Resolve the __BASE__ / __SITE_TITLE__ placeholders in the HTML entry points and
/// hand the client its runtime config on window (read by Hedge.Tenant).
function siteConfig() {
  return {
    name: 'hedge-site-config',
    // 'pre' so __BASE__ resolves before vite scans the HTML → vite bundles the theme CSS (and its
    // @imports, e.g. identity.css) into a hashed asset. One prescriptive CSS delivery across hedge.
    transformIndexHtml: {
    order: 'pre',
    handler(html, ctx) {
      const isAdmin = ctx.filename.endsWith('admin.html');
      const injected =
        `<script>window.BASE_PATH=${JSON.stringify(basePath)};` +
        `window.SITE_LOGO=${JSON.stringify(siteLogo)};` +
        `window.SITE_LOCALE=${JSON.stringify(siteLocale)};` +
        `window.SITE_SLUG=${JSON.stringify(siteSlug)};` +
        `window.SITE_TITLE=${JSON.stringify(siteTitle)};` +
        `window.SITE_FEATURES=${JSON.stringify(siteFeatures)};</script>`;
      return html
        .replace(/__SITE_TITLE__/g, isAdmin ? adminTitle : siteTitle)
        .replace(/__BASE__/g, basePath)
        .replace('<head>', `<head>\n    ${injected}`)
        // The deployment theme is for the public site only, never the shared admin tool.
        .replace('<body>', (siteSlug && !isAdmin) ? `<body class="tenant-${siteSlug}">` : '<body>');
      }
    }
  };
}

export default defineConfig({
  base: basePath + '/',
  plugins: [siteConfig()],
  build: {
    outDir: '_site' + basePath,
    rollupOptions: {
      input: {
        main: resolve(__dirname, 'index.html'),
        admin: resolve(__dirname, 'admin.html')
      }
    }
  },
  publicDir: false,
  server: {
    port: 3030,
    host: true,
    allowedHosts: true,
    watch: {
      ignored: ['!**/dist/**']
    },
    proxy: {
      '/api': {
        target: 'http://localhost:8787',
        changeOrigin: true,
        ws: true
      },
      '/blobs': {
        target: 'http://localhost:8787',
        changeOrigin: true
      }
    }
  }
});
