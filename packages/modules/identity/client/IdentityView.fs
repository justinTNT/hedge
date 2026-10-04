namespace Content

// Shared identity UI (unified shell, Stage 3 — convergence): the badge + switcher +
// connection buttons, rendered from Content.Identity's model and dispatching its Msg.
// Extracted from the Justat shell's chrome so every host renders the same identity
// control. The host supplies its own frame (nav/header/sidebar); this is only the
// identity area, so it drops into any chrome.

open Feliz

module GuestSession = Client.GuestSession

module IdentityView =

    /// A round avatar image (shared, so the identity UI carries no module dependency).
    let avatar (url: string) =
        Html.img [ prop.className "avatar"; prop.src (GuestSession.assetUrl url) ]

    let private loginButton (provider: string) (label: string) =
        let path = Browser.Dom.window.location.pathname
        let returnTo = if path.StartsWith "/auth/" then "/" else path
        let baseProps = [
            prop.className (sprintf "login-btn login-%s" provider)
            prop.href (sprintf "/api/auth/%s/login?returnTo=%s" provider (Fable.Core.JS.encodeURIComponent returnTo))
            prop.text label ]
        // On a bundled mobile build the href can't navigate the WebView to the API — intercept the click
        // and run the browser-OAuth deeplink flow instead. Web is unchanged (plain link redirect).
        let props =
            if GuestSession.isMobile then
                baseProps @ [ prop.onClick (fun (e: Browser.Types.MouseEvent) -> e.preventDefault(); GuestSession.signInThenReload provider) ]
            else baseProps
        Html.a props

    let private providerLabel (provider: string) =
        match provider with
        | "google" -> "Google"
        | "github" -> "GitHub"
        | "microsoft" -> "Microsoft"
        | "facebook" -> "Facebook"
        | "linkedin" -> "LinkedIn"
        | "email" -> "Email"
        | "anonymous" -> "Anonymous"
        | p -> p

    /// Passwordless email: an input + submit, then a "check your inbox" state. Web only — the mobile
    /// app hides email (the mailed-link → deeplink handoff is a separate, deferred flow).
    let private emailConnect (model: Identity.Model) (dispatch: Identity.Msg -> unit) =
        match model.MagicLink with
        | Identity.MlSent ->
            Html.div [
                prop.className "connect-email"
                prop.children [
                    Html.p [ prop.className "email-sent"; prop.text "Check your inbox for a sign-in link — open it in this browser." ]
                    Html.button [
                        prop.className "email-again"
                        prop.text "Use a different email"
                        prop.onClick (fun _ -> dispatch (Identity.SetEmailInput ""))
                    ]
                ]
            ]
        | _ ->
            Html.div [
                prop.className "connect-email"
                prop.children [
                    Html.input [
                        prop.className "email-input"
                        prop.type' "email"
                        prop.placeholder "you@example.com"
                        prop.value model.EmailInput
                        prop.onChange (fun (v: string) -> dispatch (Identity.SetEmailInput v))
                    ]
                    Html.button [
                        prop.className "login-btn login-email"
                        prop.disabled (model.MagicLink = Identity.MlSending)
                        prop.text (if model.MagicLink = Identity.MlSending then "Sending…" else "Email me a link")
                        prop.onClick (fun _ -> dispatch Identity.RequestMagicLink)
                    ]
                    match model.MagicLink with
                    | Identity.MlFailed err -> Html.p [ prop.className "email-error"; prop.text err ]
                    | _ -> Html.none
                ]
            ]

    let private identitySwitcher (model: Identity.Model) (dispatch: Identity.Msg -> unit) =
        if not model.ShowIdentitySwitcher then Html.none
        elif model.AvailableProviders.IsEmpty
             && model.Identities |> List.forall (fun i -> i.Provider = "anonymous") then
            // No OAuth configured *and* nothing but anonymous identities: no providers to connect and
            // nothing to switch to, so the normal switcher would be an empty, dead panel (regression
            // on anonymous-only tenants like usba.se). Say plainly this is anonymous. (If a real
            // identity exists — e.g. OAuth was removed after someone linked — fall through to the
            // normal switcher so it can still be managed.)
            let name =
                match model.GuestSession.Identity with
                | Some identity -> identity.Name
                | None -> model.GuestSession.DisplayName
            let note =
                if System.String.IsNullOrWhiteSpace name
                then "No sign-in here — comment under a name we generate for you."
                else sprintf "No sign-in here — you're commenting as %s." name
            Html.div [
                prop.className "identity-switcher"
                prop.children [
                    Html.h4 [ prop.text "Engaging anonymously" ]
                    Html.p [ prop.className "switcher-note"; prop.text note ]
                ]
            ]
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
                                avatar (if id.Picture <> "" then id.Picture else GuestSession.avatarForAuthor id.Name)
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
                    // email is a pseudo-provider rendered as an input, not a link-button; hide it in the
                    // mobile app (web-only v1). OAuth providers render as buttons.
                    let oauthProviders = unconnected |> List.filter (fun p -> p <> "email")
                    let showEmail = List.contains "email" unconnected && not GuestSession.isMobile
                    if not oauthProviders.IsEmpty || showEmail then
                        Html.div [
                            prop.className "switcher-connect"
                            prop.children [
                                Html.h4 [ prop.text "Add a connection" ]
                                Html.div [
                                    prop.className "connect-options"
                                    prop.children (oauthProviders |> List.map (fun p -> loginButton p (providerLabel p)))
                                ]
                                if showEmail then emailConnect model dispatch
                            ]
                        ]
                ]
            ]

    /// The identity area: the badge (avatar + name, toggles the switcher) and the
    /// switcher itself. A host drops this into its own header/nav.
    let identityView (model: Identity.Model) (dispatch: Identity.Msg -> unit) =
        let session = model.GuestSession
        Html.div [
            prop.className "identity-area"
            prop.children [
                Html.div [
                    prop.className "identity-badge"
                    prop.onClick (fun _ -> dispatch Identity.ToggleIdentitySwitcher)
                    prop.children [
                        avatar session.AvatarUrl
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
