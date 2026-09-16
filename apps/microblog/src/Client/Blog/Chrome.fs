module Blog.Client.Host.Chrome

// darwin.news's frame (CP-B convergence): the blog module is this app's PRIMARY module, so
// this host owns the chrome — header (logo + optional info button + the shared identity area)
// around one <main> for the hosted blog content. The identity control is the shared
// Content.IdentityView (one copy every host renders); only the darwin.news-specific frame
// lives here. Mirrors the Justat shell's Chrome, single-module.

module Shared = Blog.Client.Shared
module Identity = Content.Identity

open Feliz

/// Intercept only an unmodified primary click; let cmd/ctrl/shift/alt and non-left buttons
/// fall through so "open in a new tab" still works on the real href.
let private onPlainClick (navigate: unit -> unit) =
    prop.onClick (fun (e: Browser.Types.MouseEvent) ->
        if e.button = 0 && not e.ctrlKey && not e.metaKey && not e.shiftKey && not e.altKey then
            e.preventDefault ()
            navigate ())

let private nav (idModel: Identity.Model) (dispatchId: Identity.Msg -> unit) (navigateHome: unit -> unit) =
    Html.nav [
        prop.children [
            Html.a [
                // Real href to home (right-clickable / shows a URL); an unmodified click stays
                // in-SPA. Blog is the primary module here, so home is the module root.
                prop.href (Shared.basePath + "/")
                prop.style [ style.cursor.pointer ]
                onPlainClick navigateHome
                prop.children [ Html.img [ prop.src (Shared.basePath + Hedge.Tenant.config.Logo) ] ]
            ]
            // Prominent link to the tenant's external info/companion page, when set — a full
            // navigation (href), not SPA routing. Styling preserved from the old module nav.
            if Hedge.Tenant.config.InfoUrl <> "" then
                Html.a [
                    prop.className "nav-info"
                    prop.href Hedge.Tenant.config.InfoUrl
                    prop.style [
                        style.backgroundColor "#b8352c"
                        style.color "#ffffff"
                        style.paddingTop (length.em 0.4)
                        style.paddingBottom (length.em 0.4)
                        style.paddingLeft (length.em 0.95)
                        style.paddingRight (length.em 0.95)
                        style.borderRadius (length.px 6)
                        style.fontWeight.bold
                        style.textDecoration.none
                    ]
                    prop.text (if Hedge.Tenant.config.InfoLabel <> "" then Hedge.Tenant.config.InfoLabel else "Info")
                ]
            // The shared identity control (badge + switcher + connections).
            Content.IdentityView.identityView idModel dispatchId
        ]
    ]

/// The darwin.news frame: one header (nav + identity) around the hosted blog content.
let frame
    (idModel: Identity.Model)
    (dispatchId: Identity.Msg -> unit)
    (navigateHome: unit -> unit)
    (content: ReactElement) =
    Html.div [
        prop.className "app"
        prop.children [
            Html.header [ nav idModel dispatchId navigateHome ]
            Html.main [ content ]
        ]
    ]
