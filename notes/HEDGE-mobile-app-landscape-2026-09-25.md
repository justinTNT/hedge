# Hedge and mobile apps: Expo, Capacitor and the alternatives

Date: 25 September 2026. Research and architectural assessment, not an implementation plan or a platform commitment. Based on the `hedge-native-plants` checkout and current primary documentation; no mobile build was attempted.

## Assessment

Expo is a credible way to give a Hedge app a native mobile client. Capacitor is the closer fit if the aim is to put the existing Fable/Feliz/HTML/CSS app on a phone with native capabilities. An installable progressive web app (PWA) is the smallest initial step. These choices leave Hedge's Worker, D1/R2 storage, identity records, grants and web admin useful.

For Native Plants today, investigate Capacitor first if an installed app is wanted, with a PWA as the lower-cost comparison. Prefer Expo if field capture, identification conversations, native navigation and phone-specific interactions become the primary product. Avoid converting Hedge's existing web hosts into a universal mobile UI framework as a prerequisite.

This is a fit judgment, not a measured performance ranking. Both web-based and native clients can implement offline workflows; neither supplies the application's synchronization rules automatically.

## Options

| Option | What it brings | Reuse in this repository | Main trade-off |
| --- | --- | --- | --- |
| Installable PWA | Website installed from the browser; service worker and local browser storage for offline features. | Existing F# client, HTML, CSS and same-origin web authentication. | Browser storage and background execution need careful testing on target phones; app installation and discovery differ from stores. |
| Capacitor | Bundled web app in a native container, with plugins for camera, files and other device functions. | Existing Fable/Feliz views, CSS, Elmish update logic and Vite build, with platform adapters. | Still a web-rendered UI; packaged origins, login, navigation and native lifecycle need deliberate integration. |
| Expo / React Native | Native UI components plus routing, native modules and development/build tooling. | Backend, model contracts, codecs and portable F# logic; native transport and UI work required. | A second presentation layer; Fable/Expo compatibility and bindings need proving with the pinned dependencies. |
| Fabulous / .NET MAUI | F# and Model-View-Update with MAUI controls. | Domain ideas and portable F# code; backend via HTTP. | Different runtime and UI toolchain; Fable JS interop, JS promises and current JSON/client code are not automatically .NET-compatible. |
| Flutter | Declarative UI in Dart with its own widget/rendering stack. | Hedge's backend and wire contract. | Much less direct reuse of the F# client; no specific requirement currently justifies this additional stack. |

Sources: [PWA architecture](https://developer.mozilla.org/en-US/docs/Web/Progressive_web_apps/Guides/What_is_a_progressive_web_app), [Capacitor overview](https://capacitorjs.com/docs), [Expo introduction](https://docs.expo.dev/get-started/create-a-project/), [Fabulous.MauiControls](https://github.com/fabulous-dev/Fabulous.MauiControls), [Flutter architecture](https://docs.flutter.dev/resources/architectural-overview).

Separate Swift/Kotlin applications would also consume Hedge's HTTP API, but would introduce still more platform-specific presentation work. There is no demonstrated need for that investment here. Bare React Native offers more direct ownership of native project setup; Expo is the more attractive starting point for a React Native investigation because it already supplies the common tooling and native modules. [React Native guidance](https://reactnative.dev/docs/getting-started), [Expo FAQ](https://docs.expo.dev/faq/).

## What already fits Hedge

The repository has a stronger mobile boundary than a browser-only frontend might suggest:

- [Hedge.Http](../packages/hedge/src/Hedge/Http.fs) defines a platform-independent `Transport`. [Generated Native Plants clients](../apps/native-plants/src/Client/generated/ClientGen.fs) receive that transport explicitly. A native adapter can retain the same request/codec/error contract.
- [Catalogue.fs](../apps/native-plants/src/Core/Catalogue.fs) holds search, typeahead, filtering and taxonomy calculations outside the UI. This is a good candidate for reuse in an Expo/Fable client.
- D1 remains the authoritative editable catalogue and contribution store; the existing admin stays on the web. A phone can load a published snapshot into memory for search, matching the original low-churn data design.
- Identity resolution, ownership and grants remain server concerns. App capabilities decide what a given verified subject may do; native presentation must not become a second authority.

This does not make every generated or app-owned endpoint portable without work. Contributions currently use a bespoke browser helper; binary uploads explicitly sit outside `Hedge.Http`'s JSON transport contract.

## Expo specifically

Expo supplies a React Native framework and optional EAS build/distribution services. EAS is optional and local native builds are supported; using Expo does not require moving the Hedge backend away from Cloudflare. [Expo FAQ](https://docs.expo.dev/faq/), [local development builds](https://docs.expo.dev/guides/local-app-development/).

For the field workflow it offers system camera/photo selection, persistent SQLite, authentication helpers and background tasks. These are useful building blocks for observation drafts and a synchronization queue. Background tasks remain scheduled by the OS, so uploads must also resume in the foreground. [Image picker](https://docs.expo.dev/versions/latest/sdk/imagepicker/), [SQLite](https://docs.expo.dev/versions/latest/sdk/sqlite/), [background tasks](https://docs.expo.dev/versions/latest/sdk/background-task/).

Fable produces JavaScript, and Fable React Native bindings exist. This establishes a possible route, not verified compatibility between Hedge's current Fable/Feliz/React pins and current Expo. A small compile/run prototype must prove imports, Metro bundling, the selected view bindings and device APIs. Staying in F# is possible in principle; a TypeScript native UI consuming a narrow compiled-F# interface is another option, with less end-to-end type sharing. [Fable JavaScript features](https://fable.io/docs/javascript/features.html), [Fable React Native bindings](https://github.com/fable-compiler/fable-react-native).

Ordinary native React Native views use native primitives, not Hedge's `Html.*` components or CSS selectors. Elmish's explicit state transitions can remain the paradigm, but browser commands and rendering need replacement. Expo's `use dom` components can embed existing web content in WebViews and support incremental migration. They have separate JS contexts and an asynchronous JSON bridge, so they do not make the existing app a shared native UI automatically. They may be useful for prose-heavy species accounts. [Expo web/native views](https://docs.expo.dev/workflow/web/), [DOM components](https://docs.expo.dev/guides/dom-components/).

## Why Capacitor deserves the first comparison

Native Plants already produces a static Vite site with `index.html`, the entry shape Capacitor expects. Its authored views and stylesheet can stay intact, while camera/file access and lifecycle handling sit behind small adapters. Ionic UI components are not required: Capacitor accepts an existing web framework. [Installation](https://capacitorjs.com/docs/getting-started), [framework independence](https://capacitorjs.com/), [camera plugin](https://capacitorjs.com/docs/apis/camera).

Package the app shell locally and call Hedge APIs. Simply displaying the live website remotely would not establish offline capability. Do not bundle the current public directory wholesale: on this checkout, `public/media` contains about 452.5 MiB of large photographs, 105.0 MiB of thumbnails and 3.2 MiB of other media. These are all generated files on disk, including unused assets, not the exact published or required download set.

Capacitor offers native file storage; its storage guidance points to additional solutions for database needs. Plugin selection and maintenance must be part of the spike if SQLite is chosen. A versioned public JSON snapshot plus an in-memory index may be enough for the catalogue itself. [Filesystem](https://capacitorjs.com/docs/apis/filesystem), [storage guidance](https://capacitorjs.com/docs/guides/storage).

## The real integration work, whichever renderer wins

### Authentication

The current client uses `window.HedgeGuest`, relative URLs and same-origin cookies. [Contribution writes](../apps/native-plants/src/Server/Contributions.fs) require the request's `Origin` to equal its URL origin. A packaged client must not simply turn that check off or forge browser headers. Capacitor has its own local origins; Expo has a native networking context. [Capacitor configuration](https://capacitorjs.com/docs/config).

Design a supported mobile sign-in/session handoff with system-browser or provider-SDK login, app return links, secure credential storage and explicit expiry/logout behavior. Keep the existing verified subject and grant model. The precise cookie/token mechanism remains to be selected and tested. Google rejects OAuth inside app-controlled embedded user agents; successful website login does not prove wrapped-app login. Expo OAuth testing requires a development build, not just Expo Go. [Google native OAuth](https://developers.google.com/identity/protocols/oauth2/native-app), [Expo authentication](https://docs.expo.dev/guides/authentication/).

### Offline field use

Distinguish three stores: public catalogue/downloaded images, the owner's local drafts and upload queue, and authoritative server records. The present `GetCatalogue` contains search cards, facets, glossary and references; complete species sections/gallery/map associations come from `GetPlant`. Caching only the catalogue endpoint would not create a complete offline guide.

The current app has no service worker/manifest in its authored source and clears public data after a revision-request failure. Personal responses are intentionally private/no-store, with UI cleared on logout. Offline behavior must therefore be designed explicitly: downloaded catalogue revisions, species details, selected image packs, local draft persistence and account-specific cleanup. Do not indiscriminately cache authenticated API responses.

For the [proposed field-note model](NATIVE-PLANTS-field-notes-and-identification.md), persist the note and its photo associations together locally, retain stable operation IDs, upload dependencies in order, and reconcile server revisions/quotas when reconnecting. The server may reject a queued sixth note/photo or a now-revoked reviewer action; keep the user's draft and explain the failure. Native storage is useful here, but it is not a sync engine.

PWAs can provide offline browsing and local drafts. Background Sync is not available across all major browsers, and WebKit documents storage quotas/eviction and persistence requests. An installed web app should be evaluated on real phones rather than dismissed as incapable. Home Screen web apps on iOS/iPadOS also support Web Push. [Offline web apps](https://developer.mozilla.org/en-US/docs/Web/Progressive_web_apps/Guides/Offline_and_background_operation), [Background Sync support](https://developer.mozilla.org/en-US/docs/Web/API/Background_Synchronization_API), [WebKit storage](https://webkit.org/blog/14403/updates-to-storage-policy/), [Web Push](https://webkit.org/blog/13878/web-push-for-web-apps-on-ios-and-ipados/).

### Photo capture and lifecycle

The current [upload helper](../apps/native-plants/src/Client/contribution-client.mjs) uses browser `Image`, object URLs and canvas to prepare images. Expo needs a native equivalent; Capacitor may retain portions but must handle native picker results and lifecycle restoration. Test large phone images, rotation, cancellation, temporary-file durability, interruption and retry. Preserve server-side media validation and private/public publication rules. The Capacitor camera documentation specifically calls out restoration after Android terminates the calling app. [Camera lifecycle](https://capacitorjs.com/docs/apis/camera).

## A bounded next investigation

Do not port the whole app to learn whether the path works. Build a disposable slice only after deciding to proceed:

1. Existing species search and one complete species account, with a small explicit offline media pack.
2. Google login returning to that species; private reads/writes, logout and owner isolation.
3. Camera or library selection, a persistent draft with photo association, restart in airplane mode, reconnect and upload once.
4. Repeat on a real iPhone and Android phone; check keyboard, back navigation, safe areas, image memory use and interrupted capture.

Start with Capacitor against the existing UI. If it meets the field-use requirements, stop there. If the actual experience warrants native screens, repeat the same slice in Expo and compare reuse, integration effort and interaction quality. Prove Fable/Expo compatibility before a larger UI commitment.

Keep app behavior and offline rules in Native Plants. Add framework transport/auth support only for generic protocol work demonstrated by the spike; extract reusable client adapters once their shape is known. This preserves Hedge's separation of framework, reusable libraries/modules and app while limiting another speculative refactoring cycle.
