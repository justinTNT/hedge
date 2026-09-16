# Unified Elmish shell for justat.at — design input (reconciled)

> **Canonical plan:** [UNIFIED-SHELL.md](UNIFIED-SHELL.md). This proposal has been
> reconciled into that staged plan. The text below preserves the original rationale
> and illustrative skeleton; it is not a second implementation specification.
> Follow the canonical plan for accepted interfaces, lifetime guarantees, identity
> handoff, release gates, and convergence work.

**Status: historical design input.** Planning/local implementation can proceed;
deployment and migration gates are defined in the canonical plan. Its staged delivery
supersedes the original blanket review gate and the implementation choices below where
they differ.

## Why

Today justat.at (`apps/articles`, default `HEDGE_SITE`; ndct is the override) serves the
`articles` module at `/` and the `blog` module at `/blog` as **two separate Fable/Elmish
bundles**. Consequences:
- Crossing `/` ↔ `/blog` is a **full page reload** (two `Program.run`s on the same `#app`).
- Identity is **re-fetched every load**; the identity model + switcher re-hydrate.
- The chrome + identity stack are **duplicated** across both modules' `Shared.fs`/`App.fs`
  (byte-identical), and site chrome (`ndctHero`, `justatSidebar`, "Web Log" link) lives
  *inside* the shared modules gated on `Slug`/`hasFeature` — the wrong layer.

Goal: one Elmish shell hosting both module components under one router, chrome + identity
loaded once, so `/` ↔ `/blog` is seamless SPA navigation.

## Scope decision (the central fork, resolved)

**justat-only shell now, with strictly ADDITIVE changes to the two shared modules.**
`blog` is live+primary in `apps/microblog` (darwin.news + 5 tenants) and `articles` runs
standalone in ndct, so *removing* identity/chrome from the modules breaks those
single-component sites. Achieving "identity once" by deletion forces touching every site
(the universal-host end-state `MODULES.md` prescribes as convergence). The smallest correct
design: the shell owns identity + chrome (app-level, `apps/articles/src/Client/Shell/`) and
hosts both modules as content-only; the modules gain additive seams and keep running
standalone byte-unchanged. Transitional cost: the identity slab exists in three places
(module×2 + shell) — already duplicated between the two modules today; convergence deletes
it. **Do not delete from the modules in this task.**

Framework boundary (locked, `ROADMAP.md`): the shell + chrome are app code, never
`packages/hedge`. The `Mount` primitive stays framework (still serves microblog `/rhymes`).

## Composition mechanics — shell skeleton (first Cmd.map in the repo)

```fsharp
module Articles.Client.Shell.Shell   // apps/articles/src/Client/Shell/Shell.fs
module A  = Articles.Client
module B  = Blog.Client
module Id = Articles.Client.Shell.Identity

type ModuleId = Articles | Blog
type Model = { Active: ModuleId; Articles: A.Types.Model; Blog: B.Types.Model; Identity: Id.Model }
type Msg =
    | ArticlesMsg of A.Types.Msg
    | BlogMsg     of B.Types.Msg
    | IdentityMsg of Id.Msg
    | UrlChanged  of string list

let init () =
    A.Shared.setRoutingBase ""          // articles primary
    B.Shared.setRoutingBase "/blog"     // blog secondary
    let route = Shell.routeOf (Router.currentUrl ())
    let aModel, _ = A.App.init ()        // discard module init Cmds; shell re-drives
    let bModel, _ = B.App.init ()
    let idModel, idCmd = Id.init route   // sync + providers + claim, ONCE
    let active, routeCmd =
        match route with
        | "blog" :: rest -> Blog,     Cmd.ofMsg (BlogMsg     (B.Types.UrlChanged rest))
        | _              -> Articles, Cmd.ofMsg (ArticlesMsg (A.Types.UrlChanged route))
    { Active = active; Articles = aModel; Blog = bModel; Identity = idModel },
    Cmd.batch [ Cmd.map IdentityMsg idCmd; routeCmd ]

let update msg model =
    match msg with
    | UrlChanged raw ->
        match Shell.routeOf raw with
        | ["auth";"claim"] | ["auth";"claim";_] as _ ->
            let idm, idc = Id.handleClaim model.Identity
            { model with Identity = idm }, Cmd.map IdentityMsg idc
        | route ->
            let target, sub =
                match route with
                | "blog" :: rest -> Blog,     BlogMsg     (B.Types.UrlChanged rest)
                | _              -> Articles, ArticlesMsg (A.Types.UrlChanged route)
            let teardown =                    // tear the OUTGOING module down on switch
                if target = model.Active then Cmd.none
                elif model.Active = Articles then Cmd.map ArticlesMsg A.App.teardownCmd
                else Cmd.map BlogMsg B.App.teardownCmd
            { model with Active = target }, Cmd.batch [ teardown; Cmd.ofMsg sub ]
    | ArticlesMsg m -> let am, ac = A.App.update m model.Articles
                       { model with Articles = am }, Cmd.map ArticlesMsg ac
    | BlogMsg m     -> let bm, bc = B.App.update m model.Blog
                       { model with Blog = bm }, Cmd.map BlogMsg bc
    | IdentityMsg m ->
        let idm, idc, sessionChanged = Id.update m model.Identity   // 3rd = session changed?
        let feed =
            match sessionChanged with
            | Some s -> Cmd.batch [ Cmd.ofMsg (ArticlesMsg (A.Types.GotSessionSync s))
                                    Cmd.ofMsg (BlogMsg     (B.Types.GotSessionSync s)) ]
            | None   -> Cmd.none
        { model with Identity = idm }, Cmd.batch [ Cmd.map IdentityMsg idc; feed ]

let view model dispatch =
    React.router [ router.pathMode
                   router.onUrlChanged (Shell.routeSegments >> UrlChanged >> dispatch)
                   router.children [ Chrome.shell model dispatch ] ]
```

Both modules `init` eagerly but their init `Cmd`s are discarded; the shell re-drives the
active module via `UrlChanged` and owns identity. The inactive module is idle until visited.

## Identity lift → `apps/articles/src/Client/Shell/Identity.fs`

Lift the byte-identical slab: `IdentityListItem` + fields GuestSession/Identities/
AvailableProviders/ShowIdentitySwitcher/SelectedIdentity/PendingClaimFocus; Msgs
GotSessionSync/RevertIdentity/GotRevertIdentity/LoadIdentities/GotIdentities/GotProviders/
ToggleIdentitySwitcher/DisconnectIdentity/GotDisconnect/SelectIdentity; the 4 identical Cmds
(revert/disconnect/loadProviders/loadIdentities) + getQueryParam/parseClaimFromRoute over
the non-prefixed `/api/auth/*` endpoints + `Client.GuestSession`. `Id.init` fires sync +
providers once; `Id.handleClaim` handles `["auth";"claim"]` once. `Id.update` returns
`Model*Cmd*(GuestSession option)` so the shell fans `GotSessionSync` to both modules.
Modules keep `GuestSession` in their Model (comment forms need it) but never render their
own switcher in-shell; in this justat-only plan they shed nothing (duplicated into the shell).

## Chrome lift → `apps/articles/src/Client/Shell/Chrome.fs`

Lift `navWithSession` (+ identity UI: loginButton/providerLabel/identitySwitcher/
identityView/avatar), `ndctHero`, `justatSidebar`. The "Web Log" link + home link become
in-SPA `navigateTo` (not full-reload `href`). `Chrome.shell` renders chrome once, swaps only
`<main>` by active module. **Open UI question:** today `/blog` shows no sidebar; unified
chrome would show `justatSidebar` on `/blog` too — natural but a visible change; gate to
`Active = Articles` if undesired.

## Routing contract

Replace the global `window.MOUNT_BASE` with a settable per-module base defaulting to the
global (standalone unchanged):
```fsharp
let mutable private routingBaseOverride : string option = None
let setRoutingBase b = routingBaseOverride <- Some b
[<Emit("window.MOUNT_BASE || ''")>] let private mountBaseGlobal : string = jsNative
let private mountBase () = defaultArg routingBaseOverride mountBaseGlobal
```
Make baseSegments/stripBase/routeOf/navigateTo read it dynamically (functions, not eager
`let`). `apiPrefix` already handled by split-gen (ClientGen calls `/api/<m>/*`). The one
shell router peels `"blog"::rest` → blog, `["auth";"claim"...]` → shell, else → articles.

## Teardown sequencing

Promote each module's inline `UrlChanged` cleanup (currentWsClose, commentEditorActive,
blog ownerCommentEditorActive) to a public `teardownCmd`. On a cross-module switch the shell
runs the OUTGOING module's `teardownCmd` (effects only) before the incoming `UrlChanged` — so
the outgoing WS closes + editors are destroyed (no leaked sockets / dup NewComment handlers).

## Build collapse (justat only; ndct + microblog preserved)

- `Client.fsproj`: entry conditional — ndct → `Articles/Main.fs` (unchanged); justat →
  `Shell/Main.fs` + shell sources.
- `index.html`: justat → `/dist/client/Shell/Main.js`.
- Delete for justat: `blog.html`, the `blog` rollup input in `vite.config.js`, `Blog/Main.fs`.
- `Server/Worker.fs`: remove the `/blog` Mount (keep the primitive). `/api/blog/*` still
  dispatched by `Server.Routes` (no API change); GET `/blog[/*]` falls to the SPA fallback
  serving the shell (verify deep links + coordinate `Meta.fs` OG for `/blog`).
- No change to gen-modules.json, schema, migrations, wrangler bindings, wire/codec.

## Staging (each buildable + test.sh green)

- **Stage 0 — additive seams (no-op).** Add `contentView` (split `<main>` out of `appView`),
  public `teardownCmd`, settable routing base to each module. Standalone byte-identical.
- **Stage 1 — shell hosts articles ONLY (first deployable).** Add Shell/{Identity,Chrome,
  Shell,Main}.fs; blog stays on the `/blog` bundle. De-risks identity/chrome/routing.
- **Stage 2 — add blog + seamless SPA (goal).** Router peels `"blog"::rest`; fan
  GotSessionSync; teardown on switch; "Web Log"/home in-SPA; collapse the build. Verify:
  no reload; identity not re-fetched; one WS socket (outgoing closes on switch); `/blog/<slug>`
  + `/blog/tag/<t>` deep links; OAuth claim; ndct + microblog unchanged.
- **Stage 3 — optional tidy.** Do NOT migrate ndct/microblog onto the shell (convergence).

## Risks
- First `Cmd.map` in the repo — mitigated by Stage 1 (one module first).
- WS/editor leak on switch — verify with DevTools WS panel.
- Session desync into blog — verify a post-merge comment on a `/blog` item shows the new id.
- Chrome DOM must stay class-identical (`.identity-area`, `.identity-switcher`, `.nav-blog`,
  `.ndct-hero`, `.js-sidebar`) — copy markup verbatim.
- `/blog` SPA fallback after Mount removal — verify deep links serve the shell.
- Transitional triplication of the identity slab — accepted; deleted at convergence.

## Critical files
- `apps/articles/src/Client/Client.fsproj`, `index.html`, `blog.html`, `vite.config.js`
- `packages/modules/{articles,blog}/src/Client/App.fs` (contentView + teardownCmd)
- `packages/modules/{articles,blog}/src/Client/Shared.fs` (settable routing base; lifted UI)
- `apps/articles/src/Server/Worker.fs` (remove /blog Mount; keep primitive)
- New: `apps/articles/src/Client/Shell/{Shell,Identity,Chrome,Main}.fs`
