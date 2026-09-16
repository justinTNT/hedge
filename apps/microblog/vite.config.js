import { defineConfig } from 'vite';
import { resolve } from 'path';
import { existsSync, readdirSync } from 'node:fs';

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
// Tenant selection. Selects the tenant stylesheet (styles/tenants/<slug>.css)
// and stamps `tenant-<slug>` on the public <body> so those rules scope to it.
const siteSlug = process.env.SITE_SLUG || '';
// A set SITE_SLUG must name an existing tenant stylesheet; unset/empty means
// shared styles only (no tenant, no body class). Fail fast on anything else —
// never silently fall back to a default theme.
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
// Per-tenant feature flags (comma list, e.g. "bigText") — read via Hedge.Tenant.
const siteFeatures = process.env.SITE_FEATURES || '';
// Optional external info/companion page (e.g. a campaign page on Pages). When set,
// the client shows a prominent link to it (see Blog.Client.Shared).
const siteInfoUrl = process.env.SITE_INFO_URL || '';
const siteInfoLabel = process.env.SITE_INFO_LABEL || '';
// Absolute origin (e.g. https://darwin.news) for Open Graph / Twitter card URLs.
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
/// renders the logo (basePath + SITE_LOGO — see Blog.Client.Shared).
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

/// Resolves the __BASE__ / __SITE_TITLE__ placeholders in the HTML entry
/// points and hands the client its runtime config on window.
function siteConfig() {
  return {
    name: 'hedge-site-config',
    // 'pre' so this runs BEFORE vite scans the HTML for assets — the injected <link>s (below)
    // are then bundled into hashed, cache-busted assets. CSS is assembled per entry from
    // styles/ (outside public/): index gets base + the selected tenant, admin gets its own
    // self-contained set, rhyming keeps its inline styles. Relative hrefs resolve regardless
    // of BASE_PATH (root or /st).
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
        `window.SITE_SLUG=${JSON.stringify(siteSlug)};` +
        `window.SITE_TITLE=${JSON.stringify(siteTitle)};` +
        `window.SITE_INFO_URL=${JSON.stringify(siteInfoUrl)};` +
        `window.SITE_INFO_LABEL=${JSON.stringify(siteInfoLabel)};` +
        `window.SITE_FEATURES=${JSON.stringify(siteFeatures)};</script>`;
      // Deterministic order: shared base first, then the deployment override.
      const cssLinks =
        isAdmin ? '<link rel="stylesheet" href="./styles/admin.css">'
        : isIndex ? ('<link rel="stylesheet" href="./styles/base.css">'
            + (siteSlug ? `\n    <link rel="stylesheet" href="./styles/tenants/${siteSlug}.css">` : ''))
        : '';   // rhyming.html: self-contained inline styles — no theme attached.
      const headInject = [cssLinks, injected, isIndex ? ogMeta(siteTitle) : '']
        .filter(Boolean).join('\n    ');
      return html
        .replace(/__SITE_TITLE__/g, isAdmin ? adminTitle : siteTitle)
        .replace(/__BASE__/g, basePath)
        .replace('<head>', `<head>\n    ${headInject}`)
        // Tenant class + theme are for the public site only — never the admin tool
        // (its marketing CSS would leak onto every control) or the standalone rhyming page.
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
