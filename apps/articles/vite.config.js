import { defineConfig } from 'vite';
import { resolve } from 'path';

// -- Per-deployment configuration --
// Set at build time so one branch can produce every tenant's site:
//   BASE_PATH=/x SITE_TITLE=... SITE_SLUG=... npm run build
const basePath = (process.env.BASE_PATH || '').replace(/\/$/, '');
const siteTitle = process.env.SITE_TITLE || 'Articles';
const adminTitle = process.env.ADMIN_TITLE || 'Articles Admin';
const siteLogo = process.env.SITE_LOGO || '/public/logo.png';
// BCP-47 locale for date formatting (read via Hedge.Tenant). "" = viewer's own locale.
const siteLocale = process.env.SITE_LOCALE || 'en-AU';
// Per-tenant feature flags (comma list, e.g. "blog") — read via Hedge.Tenant.hasFeature.
const siteFeatures = process.env.SITE_FEATURES || '';
// Per-tenant CSS hook: adds `tenant-<slug>` to <body> so styles.css can scope
// deploy-specific rules.
const siteSlug = process.env.SITE_SLUG || '';
// Which modules this site composes (see Server/Client fsproj + gen-modules.<site>.json).
// ndct is articles-only, so it doesn't bundle the blog shell.
const hedgeSite = process.env.HEDGE_SITE || '';
// Absolute origin (e.g. https://justat.at) for Open Graph / Twitter card URLs.
// Crawlers don't run JS and won't reliably resolve a relative og:image, so social
// previews need absolute URLs — set per tenant in the deploy script. Empty in dev,
// where the tags fall back to root-relative (harmless — no one shares a dev URL).
const siteOrigin = (process.env.SITE_ORIGIN || '').replace(/\/$/, '');
// Optional one-line description for the social-preview card. Omitted when unset.
const siteDescription = process.env.SITE_DESCRIPTION || '';

const htmlEsc = (s) => String(s)
  .replace(/&/g, '&amp;').replace(/"/g, '&quot;')
  .replace(/</g, '&lt;').replace(/>/g, '&gt;');

/// Builds the Open Graph + Twitter card <meta> tags for the public site. og:image /
/// og:url are absolute when siteOrigin is set; the image path mirrors how the client
/// renders the logo (basePath + SITE_LOGO).
function ogMeta(title) {
  const pageUrl = `${siteOrigin}${basePath}/`;
  const imageUrl = `${siteOrigin}${basePath}${siteLogo}`;
  const tags = [
    `<meta property="og:site_name" content="${htmlEsc(title)}">`,
    `<meta property="og:title" content="${htmlEsc(title)}">`,
    `<meta property="og:type" content="website">`,
    `<meta property="og:url" content="${htmlEsc(pageUrl)}">`,
    `<meta property="og:image" content="${htmlEsc(imageUrl)}">`,
    `<meta name="twitter:card" content="summary_large_image">`,
    `<meta name="twitter:title" content="${htmlEsc(title)}">`,
    `<meta name="twitter:image" content="${htmlEsc(imageUrl)}">`,
  ];
  if (siteDescription) {
    tags.splice(5, 0, `<meta property="og:description" content="${htmlEsc(siteDescription)}">`);
    tags.push(`<meta name="twitter:description" content="${htmlEsc(siteDescription)}">`);
  }
  return tags.join('\n    ');
}

/// Resolves the __BASE__ / __SITE_TITLE__ placeholders in the HTML entry points
/// and hands the client its runtime config on window.
function siteConfig() {
  return {
    name: 'hedge-site-config',
    // 'pre' so the __CLIENT_MAIN__ entry placeholder is resolved to a real path BEFORE
    // vite scans <script src> for rollup inputs (otherwise the build can't resolve it).
    transformIndexHtml: {
    order: 'pre',
    handler(html, ctx) {
      const isAdmin = ctx.filename.endsWith('admin.html');
      // Social-preview tags on the public front page only (not admin / other entries).
      const isIndex = ctx.filename.endsWith('index.html');
      const injected =
        `<script>window.BASE_PATH=${JSON.stringify(basePath)};` +
        `window.SITE_LOGO=${JSON.stringify(siteLogo)};` +
        `window.SITE_LOCALE=${JSON.stringify(siteLocale)};` +
        `window.SITE_FEATURES=${JSON.stringify(siteFeatures)};` +
        `window.SITE_SLUG=${JSON.stringify(siteSlug)};` +
        `window.SITE_TITLE=${JSON.stringify(siteTitle)};</script>`;
      // Client entry per site: ndct keeps the articles standalone entry; every other
      // site (Justat/default) boots the unified shell. Only index.html carries the
      // placeholder, so this is a no-op for admin.html / blog.html.
      const clientMain = hedgeSite === 'ndct' ? 'Articles/Main.js' : 'Shell/Main.js';
      const headInject = isIndex ? `${injected}\n    ${ogMeta(siteTitle)}` : injected;
      return html
        .replace(/__SITE_TITLE__/g, isAdmin ? adminTitle : siteTitle)
        .replace(/__CLIENT_MAIN__/g, clientMain)
        .replace(/__BASE__/g, basePath)
        .replace('<head>', `<head>\n    ${headInject}`)
        // Tenant theme is for the public site only — never the shared admin tool,
        // or the tenant's marketing CSS leaks onto every admin control.
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
      // The unified shell (Stage 2) hosts blog in-document, so there is no separate
      // blog bundle — index.html (shell) + admin.html are the only entries.
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
