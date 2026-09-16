module Articles.Client.Shell.Chrome

// The Justat frame — header (logo + Web Log + info + the shared identity area), one
// <main> for the hosted content, and the Justat sidebar (articles routes only). This is
// app-level chrome (the framework boundary keeps the shell + chrome in the app). The
// identity control itself is the shared Content.IdentityView (Stage 3 convergence); only
// the Justat-specific frame lives here.

module Shared = Articles.Client.Shared
module Identity = Content.Identity

open Feliz
open Articles.Client.Shell

/// justat.at's static sidebar, ported verbatim from the old app's baseplate. Tenant-specific for
/// now; a later per-tenant config would generalise it. CP-B: moved here from the shared articles
/// module (Articles.Client.Shared) — only the Justat shell renders it, so it's app-level chrome.
let private sidebarLink (href: string) (text: string) =
    Html.a [ prop.href href; prop.text text ]

let justatSidebar =
    Html.aside [
        prop.className "js-sidebar"
        prop.children [
            Html.div [
                prop.className "js-masthead"
                prop.children [
                    Html.a [ prop.href "mailto:just@justat.at"; prop.text "just@justat.at" ]
                ]
            ]
            Html.div [
                prop.className "js-sections"
                prop.children [
                    Html.section [
                        prop.children [
                            Html.h5 "About"
                            Html.p [
                                prop.children [
                                    Html.text "Yeah, I admit, it's a vanity blog. I never had one, til "
                                    sidebarLink "http://hipstrider.com" "hipstrider"
                                    Html.text " beat me to it. I do a bit of webdev work, and I've found this a useful place to test out new ideas in the wild."
                                ]
                            ]
                        ]
                    ]
                    Html.section [
                        prop.children [
                            Html.h5 "My sites"
                            Html.p [
                                prop.children [
                                    Html.text "For some years now I have maintained "
                                    sidebarLink "http://darwin.news" "a "
                                    sidebarLink "http://usba.se" "few "
                                    sidebarLink "http://wt.fail" "web "
                                    sidebarLink "http://mtmu.se" "logs, "
                                    Html.text "I have kept alive an old nuclear news "
                                    sidebarLink "http://ntne.ws/" "archive"
                                    Html.text " : and I have a couple of "
                                    sidebarLink "https://dont.saymay.be" "music "
                                    sidebarLink "https://dont.saymay.be" "sites."
                                    Html.text "."
                                ]
                            ]
                        ]
                    ]
                    Html.section [
                        prop.children [
                            Html.h5 "Contact me"
                            Html.p [
                                prop.children [
                                    Html.text "I'm contactable on "
                                    sidebarLink "https://www.linkedin.com/in/justin-tutty-850b76301/" "linkedin"
                                    Html.text "  and "
                                    Html.text "via SMS (0424-028-741) or email (see above)."
                                    Html.text " You can get your own "
                                    sidebarLink "https://darwin.email" "darwin.email"
                                ]
                            ]
                        ]
                    ]
                    Html.section [
                        prop.children [
                            Html.h5 "Links"
                            Html.p [
                                prop.children [
                                    sidebarLink "https://nonewgasnt.org.au/" "nonewgasnt.org.au"
                                    Html.text " | "
                                    sidebarLink "https://nowdochemtrails.net/" "nowdochemtrails.net"
                                    Html.text " | "
                                    sidebarLink "https://edarwin.au/" "edarwin.au"
                                    Html.text " | "
                                    sidebarLink "https://ausbases.au/" "ausbases.au"
                                ]
                            ]
                        ]
                    ]
                ]
            ]
        ]
    ]

/// Intercept only an unmodified primary click; let cmd/ctrl/shift/alt and non-left
/// buttons fall through so "open in a new tab" still works on these real hrefs.
let private onPlainClick (navigate: unit -> unit) =
    prop.onClick (fun (e: Browser.Types.MouseEvent) ->
        if e.button = 0 && not e.ctrlKey && not e.metaKey && not e.shiftKey && not e.altKey then
            e.preventDefault ()
            navigate ())

let private navWithSession (model: Identity.Model) (dispatchId: Identity.Msg -> unit) (navigateHome: unit -> unit) (navigateBlog: unit -> unit) =
    Html.nav [
        prop.children [
            Html.a [
                // Real href to home (right-clickable / shows a URL); onClick keeps it
                // in-SPA. Justat's theme hides the img and labels it via CSS.
                prop.href (Shared.basePath + "/")
                prop.style [ style.cursor.pointer ]
                onPlainClick navigateHome
                prop.children [ Html.img [ prop.src (Shared.basePath + Hedge.Tenant.config.Logo) ] ]
            ]
            // Blog is hosted in this same shell (Stage 2): a real href to /blog, but an
            // unmodified click navigates in-SPA. Static (Config), not a SITE_FEATURES flag.
            if Config.hostsBlog then
                Html.a [
                    prop.className "nav-blog"
                    prop.href (Shared.basePath + Config.blogPath)
                    onPlainClick navigateBlog
                    prop.text "Web Log"
                ]
            if Hedge.Tenant.config.InfoUrl <> "" then
                Html.a [
                    prop.className "nav-info"
                    prop.href Hedge.Tenant.config.InfoUrl
                    prop.text (if Hedge.Tenant.config.InfoLabel <> "" then Hedge.Tenant.config.InfoLabel else "Info")
                ]
            // The shared identity control (badge + switcher + connections).
            Content.IdentityView.identityView model dispatchId
        ]
    ]

/// The Justat frame: one header (nav + identity), one <main> for the hosted content,
/// and the Justat sidebar (shown on articles routes only — the shell owns this
/// conditional slot). `content` is the active module's content-only view.
let shell
    (idModel: Identity.Model)
    (dispatchId: Identity.Msg -> unit)
    (navigateHome: unit -> unit)
    (navigateBlog: unit -> unit)
    (showSidebar: bool)
    (content: ReactElement) =
    Html.div [
        prop.className "app"
        prop.children [
            Html.header [ navWithSession idModel dispatchId navigateHome navigateBlog ]
            Html.main [ content ]
            if showSidebar then justatSidebar else Html.none
        ]
    ]
