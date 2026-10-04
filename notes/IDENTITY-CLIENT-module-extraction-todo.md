# TODO: finish the identity-client module extraction

**Status:** deferred follow-up to the boundaries refactor (branch `hedge-boundaries-refactor`).
**Updated:** 2026-10-04.
**Companion:** [boundaries refactor plan](refactor_plan_hedge_boundaries.md) (Track 2 did the identity/mobile *server* contracts; this finishes the *client* half).

## Why

Track 2B gave the identity HTTP endpoints typed, generated **server** dispatch (`IdentityHttp.RouteContract`) and a generated **client** (`IdentityHttp.ClientGen`). The server side is wired; the client is not — the identity UI (`Content.Identity` / `Content.IdentityView`) still calls `/api/auth/{revert,disconnect,identities}` through hand-written `Client.Api.postJsonRaw` + hand encoders/decoders. The reason it wasn't finished with Track 2: the identity UI lives in the **generic** `ContentClient` library, which cannot name the identity module's wire contract without either a dependency inversion or an injection seam — both rejected (see below). The right fix is to stop treating the identity UI as generic content-client code and give it its own identity-owned client library, mirroring the server-side module extraction.

## What's true today (verified 2026-10-04, don't re-derive)

- **The cut is clean.** `packages/content-client/Identity.fs` and `IdentityView.fs` reference none of `Comments`/`HostContext`/`ClaimGlue`, and none of those reference them. No internal content-client rewiring needed.
- **Hosts won't need code changes.** All consumers use `module Identity = Content.Identity` / `Content.IdentityView.identityView …`:
  `apps/microblog/src/Client/Blog/{Chrome,Host}.fs`; `apps/articles/src/Client/{Articles/Chrome,Articles/Host,Shell/App,Shell/Chrome,Shell/Types}.fs`. If the relocated files keep `namespace Content`, every one of these resolves unchanged across the new assembly boundary.
- **Only 3 calls migrate** (the IdentityHttp contract): `/api/auth/revert`, `/api/auth/disconnect`, `GET /api/auth/identities`. These stay hand-written because they are **framework** endpoints, out of the module contract: `/api/auth/providers`, `/api/auth/me`, `/api/auth/email` (magic link), logout. (`/api/auth/activate` has no live client caller.)
- **Precedent:** `apps/microblog/src/Client/Curator/App.fs` already drives a generated client (`Alerts.ClientGen.createClient Client.Api.browserTransport`) with no hand-written wire code — the target shape.
- **Transports already exist** in `Client.Api` (now `Hedge.Client`): `browserTransport`, `capacitorTransport`, `extensionTransport`, `selectTransport`.

## The real cost: codecs-assembly promotion

`IdentityHttp.ClientGen` opens `IdentityHttp.Codecs`, and those codecs are compiled **per-app** today (`identity.http.codecs.props` → each app's `Codecs` project, for the server dispatch). The app's Client project already has that copy in its reference closure. A shared `Identity.Client` library compiling its *own* copy would make `IdentityHttp.Codecs` ambiguous (two referenced assemblies defining the same module) → compile error. So the codecs must become a **single shared assembly** first.

## Steps (full relocation)

1. **Promote the IdentityHttp codecs+client to a shared assembly.** New `packages/modules/identity/http/IdentityHttp.fsproj` compiling `generated/Codecs.fs` + `generated/ClientGen.fs`, referencing `IdentityModels` + `Hedge`.
   - App `Codecs` projects: drop the `identity.http.codecs.props` import, add a `ProjectReference` to `IdentityHttp.fsproj`. The app Server's `RouteContract` (compiled via `identity.http.server.props`) still sees `IdentityHttp.Codecs` transitively through the Codecs project.
   - Net: exactly one `IdentityHttp.Codecs`/`ClientGen` assembly; both the server-side Codecs composition and the new client library reference the same one → no duplicate-module conflict.
2. **New `Identity.Client` library** (home: `packages/modules/identity/client/`). `git mv` `content-client/Identity.fs` + `IdentityView.fs` into it, **keep `namespace Content`**. References: `IdentityHttp.fsproj` (step 1), `Hedge.Client`, `Hedge`, `ContentHostContext`/`RichText` only if actually used (check — IdentityView uses `Client.GuestSession`; likely not RichText). Packages: Feliz, Fable.Elmish, Thoth.Json, Thoth.Fetch, Fable.Browser.Dom, Fable.Core.
3. **Shrink `ContentClient`.** Remove the two `<Compile>` lines; it keeps `ClaimGlue`/`Identity[View]`? — **decide on `ClaimGlue`**: it's identity-adjacent OAuth-return glue (`namespace Content`), currently generic. Leaning keep in `ContentClient` (return-nav is host-generic); revisit if `Identity.Client` ends up the only user.
4. **Re-wire the two app Client projects** (`apps/microblog/src/Client/Client.fsproj`, `apps/articles/src/Client/Client.fsproj`): add a `ProjectReference` to `Identity.Client`. (They already reference `ContentClient` for `Comments`/`HostContext`.) No other consumer uses the identity component. The `identity.css` stays delivered by the existing Vite/`prep:lib` pipeline — do not move it.
5. **Migrate the 3 calls** in the relocated `Identity.fs`: build `IdentityHttp.ClientGen.createClient Client.Api.browserTransport`; replace the `postJsonRaw`/`fetchJsonRaw` Cmds for revert/disconnect/identities with the typed client functions; delete `encodeRevert`/`encodeDisconnect` and the hand decoder for the identities list. Map `GetIdentities.Response` (`IdentityListItem` — has `id/provider/name/picture/email/activatedAt`, matches the component's model) onto the Elmish model. Adapt the Cmd error branches from raw-JSON failure to `Result<_, Hedge.Http.ApiError>`.

## Verify

- `./test.sh` green (module surfaces, client build matrix, fixtures).
- Plan §6.2 Fable+vite by hand: microblog full `npm run build`; articles Fable client (default + ndct).
- Exercise the identity UI flows in a browser: switch/activate, disconnect, list — same wire bytes, same renewal-cookie behaviour. Framework calls (providers/me/email) still hand-written and working.

## Rejected alternatives (do not re-propose)

- **Dependency inversion** — `ContentClient` (generic presentation) referencing `IdentityModels` (a specific module's wire+schema). Wrong direction; drags identity persistence records into a client library's closure.
- **Injection** — passing `IdentityHttp.ClientGen` functions into `Content.Identity`. Nearly worthless: the injected signatures *are* `IdentityHttp.Api.*` types the component still can't name without referencing the contract, so you get transport plumbing (which `postJsonRaw` already does) without the typed contract. Strictly dominated.

## Cheaper middle path (if full relocation isn't worth it)

Relocate the UI (steps 2–4, ~half a day, no codecs promotion), have `Identity.Client` reference **`IdentityModels` for the `IdentityHttp.Api.*` record types only**, and type the 3 hand-written payloads against them (`{ identityId=…; merge=… } : IdentityHttp.Api.Revert.Request`) while keeping the existing transport. Correct dependency direction, no codecs conflict, compile-time field-name safety — you just forgo the generated path/transport wiring. Captures most of the value for ~half the cost and none of the step-1 server rewiring.
