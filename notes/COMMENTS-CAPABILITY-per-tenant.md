# Per-tenant "comments enabled" capability (ROADMAP)

Status: **planned, not built.** Roadmap card, 2026-09-17.

## Motivation

Some tenants should not take comments at all — first case: **nonukes** (`archive.ntne.ws`, a
microblog deployment, `SITE_SLUG=nonukes`), an archive-style blog. This is a deliberate per-tenant
*capability*, NOT an auth/guest-secret concern: withholding `GUEST_SECRET` would only break guests
(ugly 500s, and it kills identity too), which is the wrong tool. The right level mirrors the existing
per-tenant capabilities `CaptureEnabled` (snapshot capture) and the client `SITE_FEATURES` toggles.

## Design (mirror CaptureEnabled)

Server is the authority (a direct POST must be refused even if the UI is hidden); the client hide is
cosmetic.

1. **Server gate** — add `CommentsEnabled: bool` to `Blog.Services` (next to `CaptureEnabled`), and
   early-return a disabled response in the blog `submitComment` handler when `not
   services.CommentsEnabled` — exactly the shape of `Snapshots.fs:56`
   (`if not services.CaptureEnabled then …`). Return 404 (route effectively absent) or 403; match
   whatever the snapshot-disabled path returns for consistency.
2. **Bind per tenant** — `Server.ModuleServices.blog` sets `CommentsEnabled = (env.COMMENTS_ENABLED
   <> "false")` (default ON). Add `COMMENTS_ENABLED: string` to `Server.Env`. NB the articles app's
   `ModuleServices` builds `Blog.Services` too (justat composes blog) — set it there as well; and its
   `ModuleServices.ndct` uses the articles module only, so no blog field there.
3. **Per-tenant value** — microblog `wrangler.toml` already has `[env.<tenant>.vars]` blocks, so:
   ```toml
   [env.nonukes.vars]
   COMMENTS_ENABLED = "false"
   ```
   (default-on everywhere else; no change needed for the other tenants.)
4. **Client hide** — hide the comment form + reply/🖼 controls when disabled, driven off the same
   flag exposed to the client (piggyback on `SITE_FEATURES` — `window.SITE_FEATURES` via
   `vite.config.js` — or a dedicated `window.COMMENTS_ENABLED`). Cosmetic only; the server var
   enforces it. The comment section renders through `Content.Comments`; gate the form part of it.

## Notes

- Reusable — it's a general capability (`CommentsEnabled`), not a nonukes special case; any tenant
  can turn comments off via its `vars` block.
- If a comment-free **articles** tenant is ever wanted, give `Articles.Services` the same field +
  guard; nonukes only needs the blog module.
- Fully separable from the signed-guest-cookie work (`notes/SIGNED-GUEST-COOKIES-work-order.md`);
  touches: `Blog.Services`, blog `submitComment`, both apps' `ModuleServices` + `Server.Env`,
  microblog `wrangler.toml`, and the client comment-form render.
