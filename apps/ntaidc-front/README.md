# ntaidc-front

Static front page for **nt-ai-dc.info** and **www.nt-ai-dc.info** — an assets-only Cloudflare
Worker (`ntaidc-front`). No F#, no D1, no build step.

- The blog moved to **blog.nt-ai-dc.info** (worker `ntaidc`, in `apps/microblog`).
- **ares.nt-ai-dc.info** is a separate Cloudflare Pages project — untouched.

## Content (not committed — it ages out)

The front page tracks the current consultation and is replaced each cycle, so the HTML is **not**
kept in git. `npm run deploy` copies the current files from:

```
~/Documents/NEPAgentix/investigations/Getting It Right/
  getting-it-right-builder.html  ->  public/index.html   (served at /)
  lodge.html                     ->  public/lodge.html   (served at /lodge.html)
```

Both files are self-contained (inline images, CDN fonts, external gov links). Their internal links
assume this layout: the builder links to `lodge.html`, and lodge links back with `href="./"`.

## Swap the front page

Replace the two source files above, then:

```
npm run deploy
```

Unknown paths return 404 (no SPA fallback). To retire the site entirely, delete the worker
(`wrangler delete`) or point the domains elsewhere.
