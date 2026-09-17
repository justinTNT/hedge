# Articles styles

Each Articles deployment ships **one** tenant stylesheet — its own visual
choices — on top of the shared base. The app assembles the selected tenant's
CSS at build time from `SITE_SLUG`; nothing else's CSS is shipped. Mirrors
`apps/microblog/styles/`.

## Two independent axes

- **`HEDGE_SITE`** selects *composition* + client entry: default/justat boots
  the unified shell (articles + blog in one document); `ndct` boots the articles
  standalone entry (no blog). It does **not** pick a theme.
- **`SITE_SLUG`** selects *presentation* (this base + one tenant file). It does
  **not** change which modules are composed.

They're orthogonal: an unset `SITE_SLUG` never silently borrows a theme from the
composition, and choosing a theme never changes the modules.

## Layout

```
styles/
  README.md
  base.css           shared layout + presentation defaults (public site)
  admin.css          admin.html's set (just the framework admin sheet, bundled)
  tenants/
    justat.css       justat.at        (wood-frame two-column; system fonts)
    ndct.css         nowdochemtrails.net (Massively hero; Merriweather/Source Sans)
```

- **base.css** owns the shared public-site defaults and `@import`s the library
  identity + comments component CSS (single source — no copies).
- **admin.css** is `admin.html`'s complete set — the framework admin stylesheet,
  `@import`ed from the library. No tenant stylesheet or font ever enters it.
- **tenants/`<slug>`.css** owns only that deployment's typography, colours,
  branding, textures, and deliberate overrides, all under `body.tenant-<slug>`.
  Tenant-specific font imports live here (only ndct pulls Google Fonts; justat is
  system-font). Tenant image textures stay in `public/justat/` + `public/ndct/`
  and are referenced by absolute `url(/public/…)`.

## Selection

`SITE_SLUG` selects the tenant for both dev and build.

| `SITE_SLUG`                   | Public site gets                        |
| ----------------------------- | --------------------------------------- |
| absent or empty               | base.css only; no tenant, no body class |
| `justat` / `ndct`             | base.css + that tenant file             |
| unknown / malformed / no file | build & dev **fail** with a clear error |

Adding a tenant = drop a `tenants/<slug>.css` file. No Vite edit, no base edit,
no other tenant edit.

## Commands (from `apps/articles`)

```sh
HEDGE_SITE=justat SITE_SLUG=justat npm run dev     # shell + justat theme
HEDGE_SITE=ndct   SITE_SLUG=ndct   npm run dev     # ndct standalone + ndct theme
SITE_SLUG= npm run dev                             # shell, shared-only (no tenant)
```

Switching `SITE_SLUG` (or `HEDGE_SITE`) requires restarting the dev server.
Editing the selected tenant file (or base.css) hot-reloads normally.

## Override order & cascade

Vite bundles `base.css` first, then the selected `tenants/<slug>.css`; tenant
rules (`body.tenant-<slug> …`) also out-specify the base `.class` rules, so
appearance doesn't depend on load order. Comment presentation comes from the
shared `comments.css` (via base) + articles' own `.comment-*` in base + tenant
colour tweaks.
