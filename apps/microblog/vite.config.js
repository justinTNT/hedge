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
// Per-tenant CSS hook: adds `tenant-<slug>` to <body> so styles.css can scope
// deploy-specific rules (e.g. body.tenant-usbase nav img { width: 50% }).
const siteSlug = process.env.SITE_SLUG || '';

/// Resolves the __BASE__ / __SITE_TITLE__ placeholders in the HTML entry
/// points and hands the client its runtime config on window.
function siteConfig() {
  return {
    name: 'hedge-site-config',
    transformIndexHtml(html, ctx) {
      const isAdmin = ctx.filename.endsWith('admin.html');
      const injected =
        `<script>window.BASE_PATH=${JSON.stringify(basePath)};` +
        `window.SITE_LOGO=${JSON.stringify(siteLogo)};</script>`;
      return html
        .replace(/__SITE_TITLE__/g, isAdmin ? adminTitle : siteTitle)
        .replace(/__BASE__/g, basePath)
        .replace('<head>', `<head>\n    ${injected}`)
        .replace('<body>', siteSlug ? `<body class="tenant-${siteSlug}">` : '<body>');
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
