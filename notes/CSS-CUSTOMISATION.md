# Customising a hedge deployment's CSS

The one authoritative guide to re-theming a deployment. Restyling a tenant
should need only its own tenant file — no module CSS, no bundler edits, no other
tenant touched.

See also the per-app style READMEs: [apps/microblog/styles](../apps/microblog/styles/README.md)
and [apps/articles/styles](../apps/articles/styles/README.md).

## The layers (and who owns them)

CSS is assembled in this order; each layer overrides the previous one:

1. **App baseline + chrome** — reset, document baseline, nav/header, login,
   avatar shape. `apps/<app>/styles/base.css`.
2. **Shared component CSS** — identity control + comments renderer, owned by the
   library (`packages/content-client/{identity,comments}.css`).
3. **Module content defaults** — the feed/extract/day-divider/article-body look,
   owned by each content module and scoped under its root:
   `packages/modules/blog/blog.css` (`.blog-content …`) and
   `packages/modules/articles/articles.css` (`.article-content …`).
4. **Host chrome** — a host's own frame (Justat's two-column wood frame +
   sidebar, NDCT's hero), in the app + its tenant file.
5. **Tenant** — branding, colours, typography, deliberate overrides. Everything
   here is scoped to `body.tenant-<slug>`, so it only affects that deployment.

A tenant author works in layer 5 only.

## Two ways to customise

### 1. Semantic tokens (the easy path)

Set these custom properties on `body.tenant-<slug>` and the base/module CSS picks
them up. This is the smallest, most stable interface — prefer it for common
re-themes. Each has a fallback = the default look, so unset = unchanged.

| Token         | Effect                                         | Applies |
| ------------- | ---------------------------------------------- | ------- |
| `--font-body` | Body font family (whole site)                  | microblog + articles |
| `--font-head` | Headline font (`.feed-item h2`, `.article-title`) | microblog + articles |
| `--accent`    | Link colour                                    | articles (blog's accent is its red default — override via selectors) |

```css
/* apps/articles/styles/tenants/acme.css */
body.tenant-acme {
  --font-body: "Inter", system-ui, sans-serif;
  --font-head: "Fraunces", Georgia, serif;
  --accent: #b5121b;
}
```

That one block re-themes fonts + links across the site — no module or bundler
change. Tokens **cascade**, so they're inherited into both module roots on a
multi-module host (e.g. Justat), unless a more specific rule overrides.

### 2. Stable selectors (for deliberate, deeper looks)

Anything beyond the tokens is done with these documented, stable class hooks.
They out-specify the module defaults automatically (the `body.tenant-<slug>`
prefix adds an element + a class), so you never need `!important`.

**Module roots** (scope everything so it can't leak between modules on the shell):
- `.blog-content` — the blog module's content subtree.
- `.article-content` — the articles module's content subtree.

**Content component classes** (inside a module root):
`.feed-item` (+ `h2`), `.extract` (+ `img`, `span`), `.feed-day` /
`.feed-day-label`, `.item-detail` / `.article-detail`, `.article-title`,
`.body` / `.article-body`, `.tags` / `.tag`, `.owner-comment`.

**Comments** (shared renderer): `.comments`, `.comment-thread` (+ `.depth-N`,
`.root-comment`, `.collapsed`), `.comment-content`, `.comment-author`,
`.comment-body`, `.comment-meta`, `.comment-collapse-line`, `.comment-reply-btn`,
`.comment-form`, `.commenting-as`. One deployment rule on `.comments` affects
both modules; `.blog-content .comments` / `.article-content .comments` differ one
without the other.

**Identity control** (shared): `.identity-area`, `.identity-badge`,
`.identity-switcher`, `.identity-option`. Re-tint per tenant via
`body.tenant-<slug> .identity-area { color: … }`.

**App chrome:** `nav`, `header`, `.app`, `.login-options`; plus host-specific
(`.js-sidebar`/`.js-masthead`/`.js-sections` on Justat, `.ndct-hero` on NDCT).

```css
/* Give only the blog cards a tinted border on this deployment; articles unchanged. */
body.tenant-acme .blog-content .extract { border-left-color: #b5121b; }

/* A distinctive textured card (like justat) — scope it to the module + use assets. */
body.tenant-acme .article-content .feed-item {
  background: #efe9d3 url(/public/acme/card.png) no-repeat top center;
  background-size: 100% 100%; border: none;
}
```

## Behaviour hooks — do not restyle-then-break

Some attributes/ids drive behaviour, not looks; don't repurpose them:
`[data-fit-headline]` (bigText fitting), the rich-text editor mount ids
(`blog-comment-editor` / `article-comment-editor`), and the scroll sentinels.
Change presentation on the presentation classes above, not these.

## Precedence, in one line

baseline → shared components → module defaults → host chrome → **tenant**. A
tenant rule (`body.tenant-<slug> …`) always wins by specificity; tokens win by
being set on the tenant body. You should never need `!important` or to rely on
incidental DOM nesting.

## What does *not* belong in CSS

Structural changes — adding a sidebar, changing which modules compose, a
different page skeleton — are **host composition**, not tenant CSS. `HEDGE_SITE`
selects composition + client entry; `SITE_SLUG` selects presentation. A theme
never changes which modules load, and an unset `SITE_SLUG` never borrows a theme.

## Compatibility

The tokens and the class hooks listed here are the **public interface**. Changing
or removing one is a breaking change for tenant files — update all affected
tenants together, and keep this guide as the single source of truth.
