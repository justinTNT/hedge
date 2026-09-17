# Microblog tenant CSS separation

Status: IMPLEMENTED 2026-09-17 (commit `58abafc`). Verified: 66-assertion build
matrix (all 7 tenants + absent/empty + invalid/malformed-fail + idealist
`BASE_PATH=/st`), `./test.sh` green, authoring isolation (one-tenant edit leaves
other tenants byte-identical), and a browser render of usbase. Not deployed
(none required); code-only, no DB migration.

Follow-up (post-review): an independent CSS review (`agent-b-css-review.md`)
found that the initial 66-assertion matrix cleaned `_site` between builds, so it
missed a whole-output isolation gap — `build:site` wrote to `_site${BASE_PATH}`
but never cleaned `_site`, so a root build then a `/st` build left BOTH tenants'
CSS in the deployable tree Wrangler uploads. Fixed by cleaning `_site` at the
start of `build:site` (root redirect preserved), verified with a both-directions
cross-prefix regression check. That whole-output isolation acceptance item is
now genuinely satisfied. (The same review also fixed a shared identity-badge
hover-contrast regression — see `agent-b-css-review.md`.)

Recorded: 17 September 2026.

## Outcome

Give each Microblog deployment one uncluttered stylesheet containing its own visual choices. The app supplies shared styles and assembles only the selected tenant's CSS. An unset `SITE_SLUG` selects **nothing**: the public site receives shared styles, with no tenant stylesheet or tenant body class.

This is a bounded implementation slice of the [deployment CSS authoring investigation](CSS-MODULARITY-investigation.md). It does not depend on completing that investigation, choosing a CSS methodology, or extracting the [identity module](IDENTITY-MODULE-investigation.md). Preserve existing named deployments' appearance while improving where their CSS is authored and what is shipped.

## Selection contract

Use the existing `SITE_SLUG` for both local development and production builds; introduce no separate theme setting.

| Configuration | Public index styling |
| --- | --- |
| `SITE_SLUG` absent or empty | Shared styles only; no tenant selected. |
| `SITE_SLUG=darwinnews` | Shared styles plus `tenants/darwinnews.css`. |
| Any other known slug | Shared styles plus exactly that tenant's file. |
| Unknown, malformed, or missing-file slug | Fail development startup and build with a useful error; never silently choose another theme. |

Resolve valid slug names against the tenant directory using a small local convention. Adding a tenant stylesheet must not require editing Vite's code. Keep the body class and runtime `window.SITE_SLUG` consistent with the selection; retain existing classes needed by behavior such as the Nonukes archive notice.

Update the existing Darwin News `deploy` script to pass `SITE_SLUG=darwinnews` explicitly, and check other Darwin News build entry points and documentation for the previous implicit default. Check runtime consumers of the slug before introducing that explicit value. Title, logo, origin, and feature configuration remain independent: selecting no CSS theme does not erase their existing defaults.

## Authoring surface and ownership

Use this structure, outside `public/`:

```text
apps/microblog/styles/
  README.md
  base.css
  admin.css
  tenants/
    darwinnews.css
    usbase.css
    mtmuse.css
    wtfail.css
    idealist.css
    ntaidc.css
    nonukes.css
```

- `base.css` owns Microblog's shared layout and presentation defaults. A deployment with no theme remains usable.
- `admin.css` owns Microblog's admin-specific adjustments, alongside the existing framework admin stylesheet and required shared dependencies.
- Each tenant file owns only that deployment's typography, colours, branding, and deliberate layout/component overrides. Tenant-specific font imports belong here too.
- Existing library and framework styles retain their owners. Import the existing [identity stylesheet](../packages/content-client/identity.css) through app-owned assembly; do not create another editable copy. Preserve required rich-text styles.

Distinguish genuine shared defaults from Darwin News branding currently embedded in the common stylesheet. Move deployment-specific choices into `darwinnews.css`, preserving any defaults other tenants currently depend on. Do not invent cosmetic differences merely to fill the file: it may be small or empty if Darwin News needs no overrides. An empty tenant file is valid, distinct from a configured file that is missing.

The developer edits their tenant file without importing shared dependencies or understanding other tenants. Avoid requiring repeated tenant prefixes for new rules. Existing `body.tenant-*` selectors may remain where removing them would change specificity; a wholesale selector rename is not a prerequisite.

## Implementation steps

### 1. Extract styles with their current behavior

Inventory and split [public/styles.css](../apps/microblog/public/styles.css) into shared, admin, and tenant files. Capture representative before screenshots first. Separate the global Google Fonts import so only tenants using those fonts request them.

Move Nonukes' inline presentation from [index.html](../apps/microblog/index.html) into its tenant file. Its banner markup currently exists on every deployment and is hidden by CSS. Preserve that visibility gate independently of loading the Nonukes stylesheet, using hidden/conditional markup or a small shared hiding rule. Verify both the banner and once-per-browser modal still appear only on Nonukes; retain dismissal behavior.

Account for the existing cascade, especially shared rules currently appearing after tenant blocks, dark mode, and narrow-screen overrides. Simply moving every tenant rule to the end can change appearance even when declarations are untouched. Resolve those differences deliberately without introducing a new styling system.

### 2. Select and bundle at the app boundary

Extend the existing [Vite HTML transformation](../apps/microblog/vite.config.js), which already runs before asset processing, to include the shared entry and optional selected tenant entry for `index.html`. Keep a documented, deterministic order: required shared dependencies and base styles, then deployment overrides. Let Vite process these entries into its normal bundled assets, including relative CSS asset references.

Give [admin.html](../apps/microblog/admin.html) an explicit dependency set: framework admin styles, required shared/component styles, and app admin adjustments. No tenant stylesheet or tenant font import may enter its CSS dependency graph. Audit which common rules its current appearance relies on before removing the monolithic link.

Keep [rhyming.html](../apps/microblog/rhyming.html)'s existing standalone presentation. Do not automatically attach the main site's theme to every HTML entry.

Use the same selection and validation in dev and production. Switching `SITE_SLUG` requires restarting the dev server; editing the selected file should use the existing CSS reload workflow.

### 3. Stop shipping unused CSS

Update [package scripts](../apps/microblog/package.json) and HTML references to retire `public/styles.css` and the generated `public/identity.css` copy when imports use the library source directly. The current `cp -r public/*` step would otherwise publish every stylesheet placed there, even if HTML references only one.

Verify the complete deployable output contains only the selected tenant's CSS and its referenced font/style assets, alongside shared and independent-entry CSS. Check raw copied files as well as bundled assets. Ensure successive builds do not retain a previous tenant's CSS, including when switching between root and `/st` outputs.

Existing public logos and unrelated assets are outside this slice; changing their copying strategy is not required to establish CSS selection. Do not claim all tenant images have been isolated by this change.

### 4. Document the everyday workflow

Add a short `styles/README.md` covering ownership, override order, supported selectors/properties already used by the themes, and commands run from `apps/microblog`, for example:

```sh
SITE_SLUG=usbase npm run dev
SITE_SLUG=usbase npm run build
SITE_SLUG= npm run dev
```

Explain how to add a tenant file and select it through deployment configuration. Restyling a tenant should require only its file and any assets it directly references. A new theme does not itself provision a deployment.

## Verification and completion

Build all seven named selections, plus absent and explicitly empty `SITE_SLUG`. Verify invalid and missing-file selections fail clearly. Include the actual Idealist configuration with `BASE_PATH=/st`.

Check production artifacts and browser requests for unselected theme rules, font imports, raw CSS copies, stale output, and broken prefixed URLs. Check that admin loads no tenant CSS and Rhyming retains its presentation. Exercise sequential tenant builds as well as clean outputs.

Compare before/after views for each named tenant at desktop and narrow widths. Cover feed and item views, comments, identity controls, editor, long headlines and headline fitting, dark mode, and Nonukes banner/modal behavior. Check admin tables, forms, and key entry. Use existing checks and focused build/selection assertions; do not add tests that merely restate CSS declarations.

The authoring acceptance exercise is to change one tenant's colour or typography in its file, preview it, and build it. No shared file, bundler code, or other tenant file should need editing, and another tenant's build should remain unchanged. Also verify the no-slug and empty-theme cases remain usable without tenant font requests.

Record validation results and any remaining authoring friction. Completion means distinct editable tenant files, explicit selection, shared-only behavior when unset, and demonstrable exclusion of unused tenant CSS. No production deployment is required to complete this work order.

## Boundaries

This work is Microblog-only. Articles migration, repository-wide CSS conventions, CUBE/BEM adoption, cascade layers, CSS Modules, framework/scaffold APIs, broader asset packaging, and identity-module extraction remain separate decisions. Do not make them prerequisites for this split.

The [CSS follow-on plan](CSS-UPLIFT-follow-on.md) covers other hosts, component ownership, supported customisation, and module coexistence. Review that draft after this split lands; it does not expand this work order.
