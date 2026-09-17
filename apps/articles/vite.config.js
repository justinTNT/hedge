import { defineConfig } from 'vite';
import { resolve } from 'path';
import { existsSync, readdirSync } from 'node:fs';

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
// Tenant selection (presentation). Selects the tenant stylesheet
// (styles/tenants/<slug>.css) and stamps `tenant-<slug>` on the public <body>.
// Independent of HEDGE_SITE (composition) below.
const siteSlug = process.env.SITE_SLUG || '';
// A set SITE_SLUG must name an existing tenant stylesheet; unset/empty means
// shared styles only (no tenant, no body class). Fail fast on anything else —
// never silently borrow a theme.
const tenantsDir = resolve(__dirname, 'styles/tenants');
if (siteSlug) {
  if (!/^[a-z0-9-]+$/.test(siteSlug)) {
    throw new Error(`[hedge] SITE_SLUG "${siteSlug}" is malformed — expected lowercase letters, digits, and hyphens.`);
  }
  if (!existsSync(resolve(tenantsDir, `${siteSlug}.css`))) {
    const known = readdirSync(tenantsDir).filter((f) => f.endsWith('.css')).map((f) => f.slice(0, -4)).sort();
    throw new Error(`[hedge] SITE_SLUG "${siteSlug}" has no stylesheet styles/tenants/${siteSlug}.css. Known tenants: ${known.join(', ')}.`);
  }
}
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
    // 'pre' so this runs BEFORE vite scans the HTML: the __CLIENT_MAIN__ entry
    // placeholder resolves to a real path (so rollup can find the client bundle), and
    // the injected <link>s (below) get bundled into hashed, cache-busted assets. CSS is
    // assembled per entry from styles/ (outside public/): index = base + selected tenant;
    // admin = its own set; other entries get none. Relative hrefs resolve at any BASE_PATH.
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
      // Deterministic order: shared base first, then the deployment override.
      const cssLinks =
        isAdmin ? '<link rel="stylesheet" href="./styles/admin.css">'
        : isIndex ? ('<link rel="stylesheet" href="./styles/base.css">'
            + (siteSlug ? `\n    <link rel="stylesheet" href="./styles/tenants/${siteSlug}.css">` : ''))
        : '';
      const headInject = [cssLinks, injected, isIndex ? ogMeta(siteTitle) : '']
        .filter(Boolean).join('\n    ');
      return html
        .replace(/__SITE_TITLE__/g, isAdmin ? adminTitle : siteTitle)
        .replace(/__CLIENT_MAIN__/g, clientMain)
        .replace(/__BASE__/g, basePath)
        .replace('<head>', `<head>\n    ${headInject}`)
        // Tenant class + theme are for the public site only — never the admin tool
        // (its marketing CSS would leak onto every control).
        .replace('<body>', (siteSlug && isIndex) ? `<body class="tenant-${siteSlug}">` : '<body>');
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
