# CSS follow-on: hosts, component ownership, customisation, and coexistence

Status: IMPLEMENTED 2026-09-17. All four outcomes landed as reviewed slices after the comments
extraction (`c7fb5c9`): (1) articles tenant separation `3c6a977`; (2)+(4) module content ownership +
shell coexistence `5e73fcd` (blog) + `4f334cd` (articles/justat/ndct, `.blog-content`/`.article-content`
roots, composition-aware assembly); (3) semantic tokens + `notes/CSS-CUSTOMISATION.md` guide `f98212f`.
Also promoted microblog's admin skin to the canonical framework sheet `344f689`. Verified per slice
(builds + `./test.sh` + bundle/DOM fixtures); justat/blog now renders the blog default look (agreed).
NOT deployed — needs a running-shell browser acceptance pass (justat / justat/blog / ndct / microblog
tenants + admin, before/after) before any deploy.

Recorded: 17 September 2026.

Predecessors: [Microblog tenant CSS work order](TENANT-CSS-work-order.md) and [shared comments presentation](COMMENTS-REUSE-proposal.md). Background: [deployment CSS authoring investigation](CSS-MODULARITY-investigation.md).

## Purpose and scope

Make the tenant authoring experience work across hosts, with usable module defaults and predictable customisation. A deployment author should edit that deployment's visual choices without repairing shared components, learning other tenants' CSS, or managing dependency order.

This plan covers four agreed outcomes:

| Area | Required outcome |
| --- | --- |
| Other hosts | Extend the landed tenant convention to Articles, including Justat and NDCT, and identify any other hosts needing the same treatment. |
| Component ownership | Put component defaults with their owning module or library; keep app composition and tenant branding with their respective owners. |
| Customisation | Provide a small, documented set of theme properties and stable selectors that supports existing deployments. |
| Coexistence | Blog, Articles, identity, and rich-text presentation work together without accidental selector collisions or route-dependent styling. |

Preserve the predecessor's selection contract: unset or empty `SITE_SLUG` selects no tenant CSS; a known slug selects one theme; an invalid or missing configured theme fails clearly. Existing named deployments retain their appearance and behavior.

## Review after the tenant split and comments extraction

Use the actual implementation and its validation evidence as the starting point. At review:

1. Record both landed changes, their directory convention, dependency order, selection behavior, shared comments owner, and any remaining authoring friction. Remove work already satisfied by those changes.
2. Confirm the affected hosts and consumers, especially shared styles used by standalone Microblog, the Justat shell, and standalone NDCT.
3. Agree the concrete component ownership map and initial customisation hooks from real rules and tenant differences. Check for any deliberate shared styling currently masquerading as a collision.
4. Confirm the implementation slices and visual acceptance examples below. Revise this plan where the split supplies better evidence.

Review this draft against the predecessors' results. It does not trigger implementation automatically or expand their scope.

## 1. Extend the convention to other hosts

Start with [Articles](../apps/articles), whose stylesheet currently combines app defaults, Justat, and NDCT presentation. Adopt the landed Microblog structure and build behavior: shared app styles, separate admin adjustments, and individual tenant files outside the copied public directory. Keep tenant font imports with the themes that use them, and bundle only the selected theme.

Preserve Articles' two host shapes. `HEDGE_SITE` selects module composition and client entry; `SITE_SLUG` selects presentation. An unset slug must not silently choose a theme from the composition, and choosing a theme must not change which modules are composed. Keep public themes out of admin.

Check the remaining apps for mixed deployment styles and consumers of any shared CSS changed here. Extend the convention where it solves that problem; record unaffected hosts without manufacturing tenant files or migrating their working CSS delivery again. Broader host scope discovered during this inventory belongs in the review before implementation.

**Acceptance:** Justat, NDCT, and unthemed Articles builds use the same selection rules as Microblog. Each theme can be edited independently. Development and production agree, unused tenant CSS is absent from output, and existing public/admin appearance is preserved. Exercise both root and supported prefixed deployment paths.

## 2. Put component defaults with their owners

Follow [Hedge's framework boundary](MONOREPO.md) and [the implemented shell ownership](UNIFIED-SHELL.md):

| Owner | CSS responsibility |
| --- | --- |
| Framework | Generic generated/admin UI and its supported hooks. |
| Feature modules | Their content components, layout within those components, interaction states, and usable defaults. |
| Ordinary libraries | The shared components they implement, including existing identity and rich-text UI. |
| App/host | Document baseline, shell layout, chrome placement, composition, and stylesheet assembly. |
| Tenant | Branding, typography, colours, and deliberate presentation overrides. |

Map rules against the markup they style, beginning with [Blog](../packages/modules/blog/src/Client), [Articles](../packages/modules/articles/src/Client), and the post-split app baselines. Extract module-owned defaults into module-owned CSS entries. Keep app chrome, such as Justat's sidebar and NDCT's hero, with the app; their deployment-specific appearance belongs with the theme.

Build on the existing [identity CSS](../packages/content-client/identity.css), [rich-text CSS](../packages/rich-text/styles.css), and the agreed comments extraction. Comments should have one shared renderer and stylesheet; avoid splitting their defaults back into Blog and Articles. Comment-owned avatar styles stay scoped to `.comments`. Similar declarations elsewhere alone do not justify a new common package. No identity server/schema extraction is required.

The app assembles the style entries required by its composition once, in a documented order. Tenant authors do not import module dependencies. Module defaults must work without a tenant theme, and feature CSS must not set unrelated document-wide styles. Keep stylesheet inclusion at the existing build/composition boundary; no runtime style registry is needed.

**Acceptance:** the same Blog component works in Microblog and Justat using one source of defaults; Articles works in Justat and NDCT likewise. Tenant files contain no duplicated generic component fixes. Empty themes retain usable content, controls, focus states, and editors. Intentional host differences remain explicit.

## 3. Define supported customisation

Inventory existing tenant differences and define only the useful public interface they demonstrate. Start with a small set of semantic custom properties for such concerns as text, surfaces, accent colour, and typography, with component-specific properties where meanings differ. Names and exact coverage are review decisions, not a commitment to expose every declaration.

Document stable component roots and selectors for changes beyond those properties. Preserve the agreed comments hooks: `.comments` for shared styling, `.blog-content .comments` for Blog overrides, and `.article-content .comments` for Articles overrides. Explain where a tenant can adjust one module without changing another, which values intentionally inherit across the site, and where a requested structural change belongs in host composition rather than CSS.

Establish predictable precedence between baseline, component defaults, host layout, and tenant overrides. Avoid requiring authors to add repeated tenant prefixes, depend on incidental DOM nesting, or accumulate `!important`. Preserve existing appearance while simplifying specificity where the real examples require it.

Publish a short guide with examples for changing site colours and typography, customising one module, and making one existing distinctive layout work. Link it from each affected app's styling README; keep one authoritative explanation of shared hooks. Update all affected consumers together when changing existing selectors, and identify public hooks as compatibility obligations for later changes.

**Acceptance:** an author can perform those examples in their tenant file without changing module CSS or bundler code. Demonstrate common choices in two visually different deployments, plus a module-specific override that leaves its sibling unchanged.

## 4. Make module styles coexist

Audit shared names including `.feed-item`, `.extract`, `.loading`, `.error`, `.avatar`, and comment/editor selectors. Determine whether they express intentionally shared presentation or independent component details. Use component roots or explicit names to isolate independent rules; do not rename the entire repository by convention alone.

Keep state-driven classes and attributes derived from client state. Preserve behavior hooks such as `data-fit-headline`, editor references, and scroll observers when changing presentation selectors. CSS changes must not alter host routing, identity authority, or module lifetimes.

Validate both modules' styles loaded in the same document, including the real Justat route sequence Articles → Blog → Articles and direct entry to each route. Use a small local fixture with both component trees visible where the production shell cannot demonstrate simultaneous isolation. Check inherited properties and broad element selectors as well as matching class names.

**Acceptance:** adding the other module's stylesheet does not change a component's appearance. Reversing the order of the independent module styles, while retaining the agreed host/theme precedence, has no effect on their defaults. Route changes do not leave stale presentation behind. A deliberate tenant override for Blog leaves Articles unchanged, and shared identity/editor defaults remain usable in both contexts.

## Delivery and validation

After review, implement in small reviewable slices: Articles tenant separation; component ownership and the necessary collision fixes; then the supported customisation guide and remaining adjustments. Ownership, naming, and tenant selector updates that must change together belong in the same slice. Every slice should leave affected hosts buildable.

Capture before/after evidence for Microblog, Justat, and NDCT, using their real configurations and representative content. Include unthemed/empty-theme cases, feed and detail views, comments, identity, editors, long headlines, narrow layouts, dark mode, keyboard focus, and loading/error/empty states. Reuse the tenant split's selection and artifact checks rather than duplicating them.

Run affected production builds and existing relevant checks. Compile changed F# views when selectors or roots move. Add focused automated coverage for selection or cross-component behavior where it protects a real contract; use browser comparisons for visual outcomes rather than tests that repeat CSS declarations. Check other consumers whenever a shared library's styles change.

Completion requires evidence for all four outcomes, an ownership map, a short authoring guide, and a record of any remaining friction. No production deployment is required.

## Outside this plan

General deployment image/asset packaging, new-site scaffolding, identity-module extraction, a design-system rewrite, and adopting a named CSS methodology are outside scope. CSS-referenced assets and font imports still need to work in affected builds. Techniques such as layers or scoping can support a demonstrated need, but choosing a technique is not a separate deliverable or prerequisite.
