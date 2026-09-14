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
        Html.img [ prop.className "avatar"; prop.src url ]

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
