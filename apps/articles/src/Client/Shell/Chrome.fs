module Articles.Client.Shell.Chrome

// The Justat frame — header (logo + Web Log + info + identity), one <main> for the
// hosted content, and the Justat sidebar. A lifted copy of the articles module's
// navWithSession + identity switcher UI, rewired to the shell-owned Identity model/msg
// and shell navigation (unified shell, Stage 1). Model-free chrome pieces (avatar,
// justatSidebar, basePath) are reused from the module's Shared; the identity UI is
// copied because it reads the model. Deleted from the modules at Stage 3 convergence.

module GuestSession = Client.GuestSession
module Shared = Articles.Client.Shared

open Feliz
open Articles.Client.Shell

let private loginButton (provider: string) (label: string) =
    let path = Browser.Dom.window.location.pathname
    let returnTo = if path.StartsWith "/auth/" then "/" else path
    Html.a [
        prop.className (sprintf "login-btn login-%s" provider)
        prop.href (sprintf "/api/auth/%s/login?returnTo=%s" provider (Fable.Core.JS.encodeURIComponent returnTo))
        prop.text label
    ]

let private providerLabel (provider: string) =
    match provider with
    | "google" -> "Google"
    | "github" -> "GitHub"
    | "microsoft" -> "Microsoft"
    | "facebook" -> "Facebook"
    | "anonymous" -> "Anonymous"
    | p -> p

let private identitySwitcher (model: Identity.Model) (dispatch: Identity.Msg -> unit) =
    if not model.ShowIdentitySwitcher then Html.none
    else
        Html.div [
            prop.className "identity-switcher"
            prop.children [
                Html.h4 [ prop.text "Switch identity" ]
                let activeId = model.GuestSession.Identity |> Option.map (fun i -> i.Id)
                yield! model.Identities |> List.map (fun id ->
                    let isActive = Some id.Id = activeId
                    let isSelected = model.SelectedIdentity = Some id.Id
                    Html.div [
                        prop.className (
                            if isActive then "identity-option active"
                            elif isSelected then "identity-option selected"
                            else "identity-option selectable")
                        prop.onClick (fun _ -> dispatch (Identity.SelectIdentity id.Id))
                        prop.children [
                            Shared.avatar (if id.Picture <> "" then id.Picture else GuestSession.avatarForAuthor id.Name)
                            Html.div [
                                prop.className "identity-info"
                                prop.children [
                                    Html.span [ prop.className "identity-name"; prop.text id.Name ]
                                    Html.span [ prop.className "identity-provider"; prop.text (providerLabel id.Provider) ]
                                ]
                            ]
                            if isSelected then
                                if not isActive then
                                    Html.button [
                                        prop.className "btn-merge"
                                        prop.text "Merge"
                                        prop.title "Bring my comments with me"
                                        prop.onClick (fun e -> e.stopPropagation(); dispatch (Identity.RevertIdentity (id.Id, true)))
                                    ]
                                    Html.button [
                                        prop.className "btn-abandon"
                                        prop.text "Fresh"
                                        prop.title "Leave comments where they are"
                                        prop.onClick (fun e -> e.stopPropagation(); dispatch (Identity.RevertIdentity (id.Id, false)))
                                    ]
                                if id.Provider <> "anonymous" then
                                    Html.button [
                                        prop.className "btn-disconnect"
                                        prop.text "Disconnect"
                                        prop.title "Abandon this identity — its comments stay with it, and signing in again reclaims them"
                                        prop.onClick (fun e -> e.stopPropagation(); dispatch (Identity.DisconnectIdentity id.Id))
                                    ]
                        ]
                    ]
                )

                let unconnected =
                    model.AvailableProviders
                    |> List.filter (fun p -> model.Identities |> List.forall (fun i -> i.Provider <> p))
                if not unconnected.IsEmpty then
                    Html.div [
                        prop.className "switcher-connect"
                        prop.children [
                            Html.h4 [ prop.text "Add a connection" ]
                            Html.div [
                                prop.className "connect-options"
                                prop.children (unconnected |> List.map (fun p -> loginButton p (providerLabel p)))
                            ]
                        ]
                    ]
            ]
        ]

let private identityView (model: Identity.Model) (dispatch: Identity.Msg -> unit) =
    let session = model.GuestSession
    Html.div [
        prop.className "identity-area"
        prop.children [
            Html.div [
                prop.className "identity-badge"
                prop.onClick (fun _ -> dispatch Identity.ToggleIdentitySwitcher)
                prop.children [
                    Shared.avatar session.AvatarUrl
                    Html.span [
                        prop.text (
                            match session.Identity with
                            | Some identity -> identity.Name
                            | None -> session.DisplayName)
                    ]
                ]
            ]
            identitySwitcher model dispatch
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
            // Blog is now hosted in this same shell (Stage 2): a real href to /blog, but
            // an unmodified click navigates in-SPA. Static (Config), not a SITE_FEATURES flag.
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
            identityView model dispatchId
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
            if showSidebar then Shared.justatSidebar else Html.none
        ]
    ]
