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
                        if not isActive then prop.onClick (fun _ -> dispatch (SelectIdentity id.Id))
                        prop.children [
                            avatar (if id.Picture <> "" then id.Picture else GuestSession.avatarForAuthor id.Name)
                            Html.div [
                                prop.className "identity-info"
                                prop.children [
                                    Html.span [ prop.className "identity-name"; prop.text id.Name ]
                                    Html.span [ prop.className "identity-provider"; prop.text (providerLabel id.Provider) ]
                                ]
                            ]
                            // Merge/fresh only appear once this identity is chosen
                            if isSelected && not isActive then
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
                        ]
                    ]
                )
            ]
        ]

let private connectionsPane (model: Model) dispatch =
    if not model.ShowConnections then Html.none
    else
        Html.div [
            prop.className "connections-pane"
            prop.children [
                Html.h4 [ prop.text "Connections" ]
                yield! [ "github", "GitHub"; "google", "Google" ] |> List.map (fun (provider, label) ->
                    let connected = model.Identities |> List.exists (fun i -> i.Provider = provider)
                    Html.div [
                        prop.className "connection-row"
                        prop.children [
                            if connected then
                                Html.span [ prop.className "connection-label"; prop.text label ]
                                Html.span [ prop.className "connection-status"; prop.text "Connected" ]
                            else
                                loginButton provider label
                        ]
                    ]
                )
            ]
        ]

let private identityView (model: Model) dispatch =
    let session = model.GuestSession
    Html.div [
        prop.className "identity-area"
        prop.children [
            match session.Identity with
            | Some identity ->
                Html.div [
                    prop.className "identity-badge"
                    prop.onClick (fun _ -> dispatch ToggleIdentitySwitcher)
                    prop.children [
                        avatar session.AvatarUrl
                        Html.span [ prop.text identity.Name ]
                    ]
                ]
            | None ->
                avatar session.AvatarUrl
                Html.span [ prop.className "anon-name"; prop.text session.DisplayName ]
            Html.button [
                prop.className "connections-btn"
                prop.text "Connections"
                prop.onClick (fun _ -> dispatch ToggleConnections)
            ]
            connectionsPane model dispatch
            if session.Identity.IsSome then identitySwitcher model dispatch
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

