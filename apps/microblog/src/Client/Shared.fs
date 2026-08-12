module Client.Shared

open Feliz
open Feliz.Router
open Hedge.Interface
open Models.Api
open Client.Types

let private tagColors = [| "#e74c3c"; "#3498db"; "#2ecc71"; "#9b59b6"; "#f39c12"; "#1abc9c"; "#e91e63"; "#00bcd4" |]

let private tagColor (name: string) =
    let hash = name.ToCharArray() |> Array.fold (fun acc c -> int c + acc * 31) 0
    tagColors.[abs hash % tagColors.Length]

let tagPill (tag: string) =
    Html.span [
        prop.className "tag"
        prop.style [
            style.backgroundColor (tagColor tag)
            style.color "#fff"
            style.cursor.pointer
        ]
        prop.text tag
        prop.onClick (fun e ->
            e.stopPropagation ()
            Router.navigatePath ("tag", tag)
        )
    ]

let loading =
    Html.div [
        prop.className "loading"
        prop.text "Loading..."
    ]

let error (msg: string) dispatch =
    Html.div [
        prop.className "error"
        prop.children [
            Html.span [ prop.text msg ]
            Html.button [
                prop.text "Dismiss"
                prop.onClick (fun _ -> dispatch DismissError)
            ]
        ]
    ]

let feedItem (item: GetFeed.FeedItem) =
    let itemPath = item.Slug |> Option.defaultValue item.Id
    Html.article [
        prop.className "feed-item"
        prop.style [ style.cursor.pointer ]
        prop.onClick (fun _ -> Router.navigatePath itemPath)
        prop.children [
            Html.h2 [ prop.text item.Title ]
            match item.Extract with
            | Some (RichContent text) ->
                Html.p [ prop.className "extract";
                         prop.children [
                                   Html.span [ prop.text (RichText.extractPlainText text) ];
                                   match item.Image with
                                   | Some url ->
                                       Html.img [ prop.src url ]
                                   | None -> Html.none
                               ]
                ]
            | None -> Html.none
        ]
    ]

let avatar (url: string) =
    Html.img [
        prop.className "avatar"
        prop.src url
    ]

let private loginButton (provider: string) (label: string) =
    // Never round-trip back to an /auth/* page — after claiming it would dead-end
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

let private identitySwitcher (model: Model) dispatch =
    if not model.ShowIdentitySwitcher then Html.none
    else
        Html.div [
            prop.className "identity-switcher"
            prop.children [
                Html.h4 [ prop.text "Switch identity" ]
                let activeId = model.GuestSession.Identity |> Option.map (fun i -> i.Id)
                yield! model.Identities |> List.map (fun id ->
                    // ActivatedAt is history (most recent wins) — only the session's
                    // current identity is active, not everything ever activated
                    let isActive = Some id.Id = activeId
                    let isSelected = model.SelectedIdentity = Some id.Id
                    Html.div [
                        prop.className (
                            if isActive then "identity-option active"
                            elif isSelected then "identity-option selected"
                            else "identity-option selectable")
                        // Every row is selectable: the active one still has an
                        // action (disconnect), it just can't be switched to.
                        prop.onClick (fun _ -> dispatch (SelectIdentity id.Id))
                        prop.children [
                            avatar (if id.Picture <> "" then id.Picture else GuestSession.avatarForAuthor id.Name)
                            Html.div [
                                prop.className "identity-info"
                                prop.children [
                                    Html.span [ prop.className "identity-name"; prop.text id.Name ]
                                    Html.span [ prop.className "identity-provider"; prop.text (providerLabel id.Provider) ]
                                ]
                            ]
                                // Actions appear once a row is chosen
                            if isSelected then
                                if not isActive then
                                    Html.button [
                                        prop.className "btn-merge"
                                        prop.text "Merge"
                                        prop.title "Bring my comments with me"
                                        prop.onClick (fun e -> e.stopPropagation(); dispatch (RevertIdentity (id.Id, true)))
                                    ]
                                    Html.button [
                                        prop.className "btn-abandon"
                                        prop.text "Fresh"
                                        prop.title "Leave comments where they are"
                                        prop.onClick (fun e -> e.stopPropagation(); dispatch (RevertIdentity (id.Id, false)))
                                    ]
                                if id.Provider <> "anonymous" then
                                    Html.button [
                                        prop.className "btn-disconnect"
                                        prop.text "Disconnect"
                                        prop.title "Abandon this identity — its comments stay with it, and signing in again reclaims them"
                                        prop.onClick (fun e -> e.stopPropagation(); dispatch (DisconnectIdentity id.Id))
                                    ]
                        ]
                    ]
                )

                // Providers you haven't linked. Connected ones aren't repeated
                // here — they're already above as identities, with avatars.
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

let private identityView (model: Model) dispatch =
    let session = model.GuestSession
    Html.div [
        prop.className "identity-area"
        prop.children [
            // One control, one panel. Even with no identity yet the badge opens
            // it, so signing in is reachable without a second button.
            Html.div [
                prop.className "identity-badge"
                prop.onClick (fun _ -> dispatch ToggleIdentitySwitcher)
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

let navWithSession (model: Model) dispatch =
    Html.nav [
        prop.children [
            Html.a [
                prop.style [ style.cursor.pointer ]
                prop.onClick (fun _ -> Router.navigatePath "")
                prop.children [
                  Html.img [
                    prop.src "/public/darwinnews.png"
                  ]
                ]
            ]
            identityView model dispatch
        ]
    ]

let nav =
    Html.nav [
        prop.children [
            Html.a [
                prop.text "Hedge"
                prop.style [ style.cursor.pointer ]
                prop.onClick (fun _ -> Router.navigatePath "")
                prop.children [
                  Html.img [
                    prop.src "/public/darwinnews.png"
                  ]
                ]
            ]
        ]
    ]

