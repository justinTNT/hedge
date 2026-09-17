namespace Content

// Shared comments presentation (CSS follow-on — comments reuse; see
// notes/COMMENTS-REUSE-proposal.md). ONE renderer for Blog and Articles: the
// tree helpers + thread + reply form. This is PURE PRESENTATION — state in,
// callbacks out. The owning content module keeps everything hard: the
// draft/DraftRev/collapse/reply state, the submit request, the live-event vs
// response append, route invalidation, and the editor create/dispose lifecycle.
// The module maps its own comment rows to `Comment` and supplies a caller-owned
// editor mount id. No module Model/Api type enters here, so the two modules
// can't collide and either can be tested on its own.

open Feliz
open Hedge.Interface

// The shared rich-text renderer (host Client.RichText) turns stored content into
// the same markup the editor writes; anything outside that schema is dropped.
module RichText = Client.RichText

module Comments =

    /// One comment as presentation data (module rows map to this).
    type Comment =
        { Id: string
          ParentId: string option
          Author: string
          /// Already resolved by the caller (the comment's picture, or the
          /// author fallback) — the view does no identity/credential lookup.
          AvatarUrl: string
          Body: RichContent }

    /// Which reply form, if any, is open. A small union instead of nested
    /// options so "root form" and "reply to comment X" are never ambiguous.
    type ActiveReply =
        | NoReply
        | ReplyRoot
        | ReplyTo of commentId: string

    /// Everything the shared comments section needs. State in, callbacks out.
    type Props =
        { Comments: Comment list
          Collapsed: Set<string>
          Active: ActiveReply
          CurrentAuthor: string
          CurrentAuthorAvatarUrl: string
          /// Caller-owned editor mount id — distinct per module so two comment
          /// views never grab the same element. The caller creates/destroys the
          /// editor against this same id.
          EditorId: string
          OnToggleCollapse: string -> unit
          /// None = the root "Leave a comment" form; Some cid = reply to cid.
          /// The caller's adapter supplies the parent content id.
          OnBeginReply: string option -> unit
          OnSubmit: unit -> unit }

    let private avatar (url: string) =
        Html.img [ prop.className "avatar"; prop.src url ]

    let private richBody (content: RichContent) =
        let (RichContent text) = content
        Html.div [
            prop.className "comment-body hamlet-rt-viewer"
            prop.dangerouslySetInnerHTML (RichText.toHtml text)
        ]

    let private filterRoot (comments: Comment list) =
        comments |> List.filter (fun c -> c.ParentId.IsNone)

    let private filterChildren parentId (comments: Comment list) =
        comments |> List.filter (fun c -> c.ParentId = Some parentId)

    let rec private countAllReplies parentId (comments: Comment list) =
        let children = filterChildren parentId comments
        children.Length + (children |> List.sumBy (fun c -> countAllReplies c.Id comments))

    let private replyForm (props: Props) (target: ActiveReply) =
        if props.Active = target then
            Html.div [
                prop.className "comment-form"
                prop.children [
                    Html.div [
                        prop.className "commenting-as"
                        prop.children [
                            avatar props.CurrentAuthorAvatarUrl
                            Html.span [ prop.text (sprintf "Commenting as %s" props.CurrentAuthor) ]
                        ]
                    ]
                    // The caller mounts/tears down its rich-text editor on this id.
                    Html.div [ prop.id props.EditorId ]
                    Html.button [ prop.text "Submit"; prop.onClick (fun _ -> props.OnSubmit ()) ]
                ]
            ]
        else
            Html.none

    let rec private commentView (props: Props) (depth: int) (comment: Comment) =
        let children = filterChildren comment.Id props.Comments
        let hasChildren = not children.IsEmpty
        let isCollapsed = Set.contains comment.Id props.Collapsed
        let isRoot = depth = 0
        let classes =
            [ "comment-thread"
              sprintf "depth-%d" (depth % 12)
              if isRoot then "root-comment"
              if isCollapsed then "collapsed" ]
            |> String.concat " "
        Html.div [
            prop.className classes
            prop.children [
                if not isRoot then
                    Html.div [
                        prop.className "comment-collapse-line"
                        prop.onClick (fun _ -> props.OnToggleCollapse comment.Id)
                    ]
                Html.div [
                    prop.className "comment-content"
                    prop.children [
                        Html.div [
                            prop.className "comment-author"
                            prop.children [
                                avatar comment.AvatarUrl
                                Html.span [ prop.text comment.Author ]
                            ]
                        ]
                        richBody comment.Body
                        Html.div [
                            prop.className "comment-meta"
                            prop.children [
                                if hasChildren then
                                    Html.button [
                                        prop.className "comment-collapse-toggle-inline"
                                        prop.text (if isCollapsed then "+" else "-")
                                        prop.onClick (fun _ -> props.OnToggleCollapse comment.Id)
                                    ]
                                if isCollapsed then
                                    Html.span [
                                        prop.className "comment-collapse-toggle-inline"
                                        prop.text (sprintf "(%d)" (countAllReplies comment.Id props.Comments))
                                    ]
                                if not isCollapsed then
                                    Html.button [
                                        prop.className "comment-reply-btn"
                                        prop.text "reply"
                                        prop.onClick (fun _ -> props.OnBeginReply (Some comment.Id))
                                    ]
                            ]
                        ]
                        replyForm props (ReplyTo comment.Id)
                    ]
                ]
                Html.div [
                    prop.className "comment-children"
                    prop.children (children |> List.map (commentView props (depth + 1)))
                ]
            ]
        ]

    /// The complete `.comments` section: heading, root threads, the root reply
    /// form, and the "Leave a comment" button.
    let view (props: Props) =
        Html.div [
            prop.className "comments"
            prop.children [
                if not props.Comments.IsEmpty then
                    Html.h3 [ prop.text (sprintf "Comments (%d)" props.Comments.Length) ]
                yield! filterRoot props.Comments |> List.map (commentView props 0)
                replyForm props ReplyRoot
                if props.Active = NoReply then
                    Html.button [
                        prop.className "comment-reply-btn"
                        prop.text "Leave a comment"
                        prop.onClick (fun _ -> props.OnBeginReply None)
                    ]
            ]
        ]
