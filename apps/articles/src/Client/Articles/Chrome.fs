module Articles.Client.Host.Chrome

// ndct's frame (CP-B convergence): articles is this app's PRIMARY module, and on ndct it runs
// standalone (no shell — ndct composes articles only). This host owns the chrome: header (logo +
// optional Web Log/info + the shared identity area) around one <main>, plus ndct's home hero.
// The identity control is the shared Content.IdentityView. Justat uses the Shell instead, so
// justatSidebar is NOT here — it lives in Shell/Chrome. Mirrors the shell's Chrome, single-module.

module Shared = Articles.Client.Shared
module Identity = Content.Identity

open Feliz

/// Intercept only an unmodified primary click; let cmd/ctrl/shift/alt and non-left buttons
/// fall through so "open in a new tab" still works on the real href.
let private onPlainClick (navigate: unit -> unit) =
    prop.onClick (fun (e: Browser.Types.MouseEvent) ->
        if e.button = 0 && not e.ctrlKey && not e.metaKey && not e.shiftKey && not e.altKey then
            e.preventDefault ()
            navigate ())

/// ndct's full-screen intro banner (HTML5UP "Massively" look): the contrails hero with the
/// title + subtitle. Shown on the home feed only. Moved out of the shared module (Articles.Client
/// .Shared) so the shared module no longer slug-branches — this app-level host owns ndct's chrome.
let ndctHero =
    Html.section [
        prop.className "ndct-hero"
        prop.children [
            Html.div [
                prop.className "ndct-hero-inner"
                prop.children [
                    Html.h1 [ prop.className "ndct-hero-title"; prop.text "Now Do Chemtrails" ]
                    Html.p [ prop.className "ndct-hero-sub"; prop.text "theories > news" ]
                ]
            ]
            Html.a [ prop.className "ndct-hero-more"; prop.href "#feed"; prop.text "↓" ]
        ]
    ]

let private nav (idModel: Identity.Model) (dispatchId: Identity.Msg -> unit) (navigateHome: unit -> unit) =
    Html.nav [
        prop.children [
            Html.a [
                // Real href to home; an unmodified click stays in-SPA (articles is primary).
                prop.href (Shared.basePath + "/")
                prop.style [ style.cursor.pointer ]
                onPlainClick navigateHome
                prop.children [ Html.img [ prop.src (Shared.basePath + Hedge.Tenant.config.Logo) ] ]
            ]
            // The blog module's path-mount is a separate host (a real navigation, not SPA), shown
            // only where blog is composed. ndct has no blog, so this stays hidden there.
            if Hedge.Tenant.hasFeature "blog" then
                Html.a [
                    prop.className "nav-blog"
                    prop.href (Shared.basePath + "/blog")
                    prop.text "Web Log"
                ]
            if Hedge.Tenant.config.InfoUrl <> "" then
                Html.a [
                    prop.className "nav-info"
                    prop.href Hedge.Tenant.config.InfoUrl
                    prop.text (if Hedge.Tenant.config.InfoLabel <> "" then Hedge.Tenant.config.InfoLabel else "Info")
                ]
            // The shared identity control (badge + switcher + connections).
            Content.IdentityView.identityView idModel dispatchId
        ]
    ]

/// The ndct frame: the home hero (when shown), one header (nav + identity), one <main>.
let frame
    (idModel: Identity.Model)
    (dispatchId: Identity.Msg -> unit)
    (navigateHome: unit -> unit)
    (showHero: bool)
    (content: ReactElement) =
    Html.div [
        prop.className "app"
        prop.children [
            if showHero then ndctHero else Html.none
            Html.header [ nav idModel dispatchId navigateHome ]
            Html.main [ content ]
        ]
    ]
