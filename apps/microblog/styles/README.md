# Microblog styles

Each Microblog deployment ships **one** tenant stylesheet — its own visual
choices — on top of the shared base. The app assembles the selected tenant's
CSS at build time from `SITE_SLUG`; nothing else's CSS is shipped.

## Layout

```
styles/
  README.md          this file
  base.css           shared layout + presentation defaults (public site)
  admin.css          admin.html's own dependency set (framework admin + chrome + app admin)
  tenants/
    darwinnews.css   darwin.news       (the shared default look — intentionally near-empty)
    usbase.css       usba.se
    mtmuse.css       mtmu.se
    wtfail.css       wt.fail
    idealist.css     id-ea.li/st
    ntaidc.css       nt-ai-dc.info
    nonukes.css      archive.ntne.ws
```

- **base.css** owns everything shared by the public site: reset, feed, extract
  cards, day dividers, comments, tags, avatars, nav/login, mobile. It also
  `@import`s the shared identity control CSS from the library
  (`packages/content-client/identity.css`) — do not copy that file. A
  deployment with **no** tenant selected renders on base.css alone.
- **admin.css** is `admin.html`'s complete, self-contained set: the framework
  admin stylesheet (`@import`ed from the library), the small shared chrome the
  admin markup uses, and Microblog's `.admin-*` rules. It never loads a tenant
  stylesheet or a tenant font.
- **tenants/`<slug>`.css** owns only that deployment's typography, colours,
  branding, and deliberate overrides. Tenant-specific font imports live here
  too (only usbase + mtmuse pull Google Fonts). Everything is scoped under
  `body.tenant-<slug>` so it can only affect its own site.

## Selection

`SITE_SLUG` selects the tenant, for both dev and build — there is no separate
theme setting.

| `SITE_SLUG`                     | Public site gets                         |
| ------------------------------- | ---------------------------------------- |
| absent or empty                 | base.css only; no tenant, no body class  |
| a known slug (e.g. `usbase`)    | base.css + `tenants/usbase.css`          |
| unknown / malformed / no file   | build & dev **fail** with a clear error  |

Adding a tenant = drop a `tenants/<slug>.css` file and deploy with that
`SITE_SLUG`. No Vite edit, no base edit, no other tenant edit. A new theme does
not itself provision a deployment (that's the wrangler env + DB).

## Commands (run from `apps/microblog`)

```sh
SITE_SLUG=usbase npm run dev      # dev the usba.se theme
SITE_SLUG=usbase npm run build    # build it
SITE_SLUG= npm run dev            # shared-only (no tenant)
```

Switching `SITE_SLUG` requires **restarting** the dev server (it's read at
startup). Editing the *selected* tenant file (or base.css) hot-reloads normally.

## Override order & cascade

Vite bundles `base.css` first, then the selected `tenants/<slug>.css`, so tenant
rules come after base and win. Tenant selectors are `body.tenant-<slug> …`, which
also out-specify the base `.class` rules, so appearance does not depend on load
order for those. The one equal-specificity area is the dormant `html.dark …`
block in base.css: Microblog does not toggle `html.dark` today, but if it ever
does, the tenant theme (loaded last) wins — the intended precedence.

Do **not** move the intentionally per-deployment content look (extract cards,
day dividers, the red default) out of base into a "shared" abstraction beyond
what's here — tenants differ by design (darwin's red cards vs mtmuse's slate).

## Customising a tenant

See [notes/CSS-CUSTOMISATION.md](../../../notes/CSS-CUSTOMISATION.md) — the
authoritative guide to the semantic tokens (`--font-body`, `--font-head`,
`--accent`) and the stable selector hooks. Blog content lives under the
`.blog-content` root and is owned by `packages/modules/blog/blog.css`.
