import { defineConfig } from 'vite';
import { resolve } from 'path';

// -- Per-deployment configuration --
// Set at build time so one branch can produce every tenant's site:
//   BASE_PATH=/st SITE_TITLE=... npm run build
// Defaults reproduce the root-mounted darwin.news build exactly.
const basePath = (process.env.BASE_PATH || '').replace(/\/$/, '');
const siteTitle = process.env.SITE_TITLE || 'Darwin News';
const adminTitle = process.env.ADMIN_TITLE || 'DNews Admin';
const siteLogo = process.env.SITE_LOGO || '/public/darwinnews.png';
// BCP-47 locale for date formatting (read via Hedge.Tenant). Defaults to the
// estate's en-AU; a site can override, or set "" for the viewer's own locale.
const siteLocale = process.env.SITE_LOCALE || 'en-AU';
// Per-tenant CSS hook: adds `tenant-<slug>` to <body> so styles.css can scope
// deploy-specific rules (e.g. body.tenant-usbase nav img { width: 50% }).
const siteSlug = process.env.SITE_SLUG || '';
// Per-tenant feature flags (comma list, e.g. "bigText") — read via Hedge.Tenant.
const siteFeatures = process.env.SITE_FEATURES || '';
// Optional external info/companion page (e.g. a campaign page on Pages). When set,
// the client shows a prominent link to it (see Blog.Client.Shared).
const siteInfoUrl = process.env.SITE_INFO_URL || '';
const siteInfoLabel = process.env.SITE_INFO_LABEL || '';

/// Resolves the __BASE__ / __SITE_TITLE__ placeholders in the HTML entry
/// points and hands the client its runtime config on window.
function siteConfig() {
  return {
    name: 'hedge-site-config',
    transformIndexHtml(html, ctx) {
      const isAdmin = ctx.filename.endsWith('admin.html');
      const injected =
        `<script>window.BASE_PATH=${JSON.stringify(basePath)};` +
        `window.SITE_LOGO=${JSON.stringify(siteLogo)};` +
        `window.SITE_LOCALE=${JSON.stringify(siteLocale)};` +
        `window.SITE_SLUG=${JSON.stringify(siteSlug)};` +
        `window.SITE_TITLE=${JSON.stringify(siteTitle)};` +
        `window.SITE_INFO_URL=${JSON.stringify(siteInfoUrl)};` +
        `window.SITE_INFO_LABEL=${JSON.stringify(siteInfoLabel)};` +
        `window.SITE_FEATURES=${JSON.stringify(siteFeatures)};</script>`;
      return html
        .replace(/__SITE_TITLE__/g, isAdmin ? adminTitle : siteTitle)
        .replace(/__BASE__/g, basePath)
        .replace('<head>', `<head>\n    ${injected}`)
        // Tenant theme is for the public site only — never the shared admin tool,
        // or the tenant's marketing CSS (fonts, colours, masthead wordmark) leaks
        // onto every admin control.
        .replace('<body>', (siteSlug && !isAdmin) ? `<body class="tenant-${siteSlug}">` : '<body>');
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
        admin: resolve(__dirname, 'admin.html'),
        rhyming: resolve(__dirname, 'rhyming.html')
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
