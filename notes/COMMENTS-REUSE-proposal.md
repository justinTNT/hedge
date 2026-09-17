# Shared comments presentation — options and proposal

Status: IMPLEMENTED 2026-09-17 (Option B). Shared renderer `Content.Comments`
(`packages/content-client/Comments.fs` + `comments.css`); Blog + Articles consume it
via presentation adapters, each keeping its own state/requests/editor lifecycle and a
distinct editor id (`blog-comment-editor` / `article-comment-editor`). Module roots
`.blog-content` / `.article-content` added. The Articles collapse-hide bug is fixed
(shared `.comment-thread.collapsed > .comment-children { display:none }`). Verified:
all client builds + `./test.sh` green (reorder-fixtures behavior preserved), comment CSS
inlined once in both apps' bundles (no duplication; microblog skin + articles indent +
tenant overrides preserved), and a DOM fixture confirming collapse hides descendants.
Not deployed. Precedes the [CSS follow-on](CSS-UPLIFT-follow-on.md).

Recorded: 17 September 2026. Source inspected through `a9ef223`, after the Microblog tenant split.

## Recommendation

Extract one shared comments renderer, its tree helpers, and its component CSS into the existing ordinary `packages/content-client` library. Both Blog and Articles consume it through small presentation adapters. Keep comment persistence, typed content relationships, request handling, and application state with their content modules.

Provide these stable author-facing selectors, as requested:

```css
/* A deployment's common comment presentation. */
.comments {
  color: #444;
  font-size: 0.95rem;
}

/* Deliberate differences within that same deployment. */
.blog-content .comments {
  color: #334;
}

.article-content .comments {
  color: #544;
}
```

`.comments` belongs to the reusable component. `.blog-content` and `.article-content` belong to their respective content modules. These public names remain plain CSS hooks; ordinary tenant customisation does not require generated names, repeated tenant prefixes, or a second comments implementation.

This is a bounded prerequisite for the wider CSS ownership work. It does not block [signed guest cookies](SIGNED-GUEST-COOKIES-work-order.md), require identity modularisation, or introduce a new backend feature.

## Evidence from the current implementation

| Observation | Implication |
| --- | --- |
| The 106-line helper/thread/reply-form sections in [Blog Item](../packages/modules/blog/src/Client/Pages/Item.fs) and [Articles Item](../packages/modules/articles/src/Client/Pages/Item.fs) differ only at `response.Item.Id` versus `response.Post.Id`. | There is a concrete shared renderer to extract, including root/child filtering, descendant counts, author/body display, collapse controls, and reply form. |
| Both detail views also assemble the same comments heading, root list, and “Leave a comment” control. | Share the complete comments section, leaving each page's content rendering intact. |
| Microblog's [base CSS](../apps/microblog/styles/base.css) supplies `.comment-thread.collapsed > .comment-children { display: none; }`; [Articles CSS](../apps/articles/public/styles.css) only indents `.comment-children`. Both render children even when collapsed. | Source inspection identifies a functional omission in Articles' styling. Reproduce it in a browser as the first acceptance fixture; it is not just a naming issue. |
| The editor lifecycle adapters are equivalent apart from comments and surrounding helpers. Both use the fixed `RichText.commentEditorId`. | Duplication exists here too, but merging their mutable flags into one shared singleton would make ownership worse. Parameterise the editor mount without absorbing its lifecycle into the renderer. |
| Blog appends a new comment through its live event; Articles also appends from the submit response and deduplicates subsequent events. Both guard draft clearing with `DraftRev`. | The entire page update function is not interchangeable. Preserve these behaviors during a presentation extraction. |
| Comment models have typed `ItemId`/`PostId` foreign keys and typed parent-comment references. Each module supplies its own attribution SQL. | Keeping domain ownership local preserves existing schema, API, and attribution boundaries. |
| Both module `contentView` functions currently return fragments inside a host-owned `<main>`. | Add the requested module roots at this boundary, rather than making every host recreate them. |

This study compared source, stylesheet coverage, project composition, and existing reordered-completion fixtures. It did not implement an extraction or perform a new live-browser acceptance run.

## Options considered

| Option | What it shares | Benefits | Costs and judgment |
| --- | --- | --- | --- |
| A. Shared CSS only | Component styles; rendering stays duplicated. | Small change; can repair collapse styling immediately. | Future markup and accessibility changes still require two edits. Useful as a narrow bug fix, insufficient as the planned ownership boundary. |
| **B. Shared presentation library** | Renderer, tree helpers, reply-form markup, component CSS; modules supply state and callbacks. | Removes the proven duplication, makes markup/styles one unit, preserves domain and request contracts. | Requires small DTO adapters and shared dependency wiring. **Recommended for this work.** |
| C. Shared stateful client component | B plus a comments Elmish child model/update and editor lifecycle adapter. | Could centralise draft, collapse, and reply-state transitions. | Must reconcile request ownership, draft revisions, route resets, live events, and disposal. Defer until a concrete behavioral change needs this shared owner; B can evolve into it without changing storage. |
| D. Independent comments feature module | Client plus loading/submission policy, server operations, and a storage/target contract. | Useful if comments become an independently composed feature, for example with their own moderation workflow across content types. | Requires explicit target authorization, typed relationship strategy, attribution participation, and compatibility decisions. A universal `(targetKind, targetId)` table would not preserve the present foreign keys automatically. Per-target adapters could preserve storage, but add substantial contracts. Present duplication alone does not justify this scope. |

“Shared library” here still means one owned, reusable component with a supported interface. It does not place comment-specific behavior in the Hedge framework, nor make either content module depend on the other. A separate package directory can be introduced later if distribution or dependencies warrant it; it is not needed to establish ownership now.

## Proposed boundary

### Shared presentation

Proposed files, following the repository's current file-linking convention:

```text
packages/content-client/
  Comments.fs       # presentation types, tree helpers, view
  comments.css      # required component defaults and states
```

Use a small explicit input record. The intended interface is:

| Input | Meaning |
| --- | --- |
| Comment list | Presentation records containing ID, parent ID, author name, resolved avatar URL, and rich content. Preserve existing order. |
| Collapsed IDs | Read-only snapshot of the owning module's collapse state. |
| Active reply | Explicitly distinguish no open form, a thread-root reply, and a reply to a comment. A small union avoids ambiguous nested options. |
| Current author display | Name/avatar supplied by the caller. The component does not initialize identity or read browser credentials. |
| Editor element ID | Stable, caller-owned mount ID used by both the view and its editor adapter. |
| Callbacks | Toggle collapse, begin reply, and submit; adapters map these to the module's existing messages. |

The parent content ID is captured by the module's callback adapter. It does not need to become a generic target string inside the view. The shared presentation record is not a new wire format or schema type; module APIs and their typed references stay intact.

Continue using the existing rich-text renderer for comment bodies. Do not substitute raw stored content as HTML. Compute fallback avatar URLs before passing display records in. The view should neither import generated Blog/Articles clients nor depend on their `Model` types.

Keep tree operations together and preserve comment ordering, reply counts, and collapse behavior. Use stable keys for comment nodes. Do not add sorting, paging, moderation, or a speculative tree-performance framework during extraction. Any existing malformed-tree behavior discovered by tests should be recorded explicitly rather than silently redefining the data contract.

### State, effects, and editor lifetime

Each content module retains `CommentDraft`, `DraftRev`, `ReplyingTo`, `CollapsedComments`, request outcomes, live-event handling, and route invalidation. It owns editor creation and disposal through the existing rich-text adapter. The shared view renders from supplied state and emits callbacks; it does not store a second draft or issue requests.

Replace the hardcoded editor ID at the touched call sites with a stable ID supplied for that mounted comments instance. Use the same ID for rendering, creating, and destroying the editor. Do not put a shared `commentEditorActive` flag into `Comments.fs`. This lets Blog and Articles comment views coexist without pointing both adapters at `#comment-editor`, while preserving the shell's ordered disposal.

A broader guarantee of arbitrary multiple live instances of the same content module is outside this change: existing module-level socket/editor handles require a separate lifetime review. The shared renderer itself must have no singleton state and must work twice in a fixture.

Existing upload endpoints and adapters remain in their current owners. If signed-cookie work lands first, consume its session-readiness behavior; this extraction must neither bypass it nor duplicate cookie/session policy. Uploads continue working.

## Styling and module roots

Wrap each module's complete `contentView` once in a layout-neutral block carrying `.blog-content` or `.article-content`. Include its loading, error, feed, detail, and other content states. Retain the host's single `<main>` and keep header, identity control, and sidebar outside these wrappers. Audit margin collapse, sizing, sticky elements, and existing host selectors when adding the block; do not assume a new DOM ancestor is visually inert.

The shared renderer emits one `.comments` section. Keep existing internal `.comment-*` hooks where practical so tenant rules remain compatible. The public styling model is:

1. The library supplies usable component defaults and required state styling.
2. App integration supplies any existing host-wide presentation differences.
3. The selected tenant styles `.comments`, then uses `.blog-content .comments` or `.article-content .comments` where it wants a difference.

Default typography and foreground colour should inherit through the `.comments` root wherever appropriate, so the example above actually affects the contents. For subparts with intentionally distinct presentation, document the small set of supported descendant hooks, such as `.comments .comment-meta`. Avoid library selectors or inline declarations that force tenants to beat excessive specificity. No cascade-layer or CSS-methodology adoption is required.

Scope comment-owned avatar rules under `.comments`; changing them must not restyle the shared identity badge. Identity's avatar and a comment author's avatar may use the same underlying image without sharing all layout rules.

Microblog's rainbow depth lines and spacing and Articles' simpler indentation are existing presentation differences, not grounds for two renderers. Extract structural/functional rules into `comments.css`; retain intentional visual differences as app defaults or tenant overrides as appropriate. Do not unconditionally import the whole Microblog comment skin into Articles. Preserve appearance except for explicitly recorded fixes such as making collapse actually hide descendants.

Collapsed descendants must be hidden and removed from keyboard interaction, while expanding restores the existing reply/editor state. Keep collapse state declarative, use appropriate button semantics and expanded-state attributes, and ensure required hiding is supplied by the component. Do not change from hiding to unmounting an active editor subtree without preserving its lifecycle contract.

The app includes component CSS once through its existing Vite assembly, before deployment overrides. Microblog can import the source through its new base entry. Articles can use a source import from its current app stylesheet; its tenant-file migration remains in the later CSS plan. No generated CSS copy is required.

## Implementation sequence for review

1. **Capture the existing contract.** Record representative thread/reply screenshots in Microblog, Justat Articles, Justat Blog, and NDCT. Reproduce the missing collapse rule. Confirm the intended common/module-specific selector examples.
2. **Introduce the shared view and adapters.** Move the duplicate section/helpers, add the module roots, and migrate both consumers together. Compile the shared source once before module `.client.props` imports in consuming projects; update fixture projects such as [ReorderFixtures](../test/ReorderFixtures/ReorderFixtures.fsproj) too. Avoid double-linking it when both modules are composed.
3. **Move its required CSS and preserve visual variants.** Wire one component stylesheet per app. Remove duplicate functional rules, retain intentional presentation overrides, isolate avatar rules, and wire consistent editor IDs without changing submission semantics.
4. **Verify and document.** Publish the component's input/ownership contract and styling examples. Update [MODULES D3](MODULES.md) to clarify that content modules own comments while delegating presentation to this library. Record the landed result for the CSS follow-on.

Steps 2 and 3 form one behavior-complete extraction: do not leave a deployed intermediate state with new markup but missing required styles. No schema migration, endpoint change, generation redesign, or production deployment is required.

## Acceptance evidence

- Both modules call the same comments renderer; their pages contain only presentation adapters and their own content view. The component compiles without importing either module's API or model.
- One deployment rule on `.comments` affects both modules. A `.blog-content .comments` override affects only Blog; `.article-content .comments` affects only Articles. Prove this with both roots present, then through actual Justat route switches. Defaults are usable without a tenant theme.
- Root replies, nested replies, descendant counts, collapse/expand, author/avatar display, rich text, and “Leave a comment” work in all host shapes. Hidden descendants cannot receive focus. Editing, collapsing/expanding, and close/reopen do not lose the current draft or leave a broken editor.
- Two renderer instances with separate state/callbacks and editor IDs do not share collapse state, target the wrong reply, or overwrite the other editor mount. This is a component test, not a claim that every module lifecycle already supports multiple instances.
- Existing [reordered-completion fixtures](../test/ReorderFixtures/Program.fs) stay green: a late success cannot clear newer draft text, navigation invalidates stale reads, and Articles retains response/event deduplication. Exercise Blog's event-driven append and Articles' response-driven append without changing their policies.
- Fresh and returning visitors can still comment and upload through toolbar/paste/drop, including after the signed-cookie change if present. Navigation disposes the outgoing editor before the incoming one starts. Test direct item links, module switches, and the existing `/st` deployment base.
- Build Microblog, composed Articles/Justat, and standalone NDCT; run affected Fable builds and relevant existing checks. Confirm generated schemas/API surfaces are unchanged. Inspect CSS output for one shared component baseline and preserved tenant selection/admin isolation.
- Compare narrow/desktop layouts and relevant dark themes, including identity controls beside the comments fixture. Keep tests focused on behavior and actual integration; do not snapshot every CSS declaration.

## Sequencing and stop condition

The intended sequence is **landed tenant split → review and implement the agreed comments extraction → review/implement the wider CSS follow-on using that result**. Signed guest cookies remain independently prioritised; neither comments reuse nor CSS ownership is a prerequisite for them. Identity modularisation remains separate.

This proposal is complete when its options and boundary are reviewed. The eventual implementation is complete when both modules share the presentation and required styles, the requested selectors work, existing domain/request contracts remain intact, and the acceptance evidence is recorded. Shared state-machine or full feature-module extraction is not required to close that work.
