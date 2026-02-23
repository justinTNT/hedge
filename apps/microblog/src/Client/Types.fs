module Client.Types

open Elmish
open Hedge.Interface
open Models.Api
open Client.ClientGen

type ItemForm = {
    Title: string
    Link: string
    Tags: string
}

type Model = {
    Route: string list
    Feed: GetFeed.Response option
    CurrentItem: GetItem.Response option
    TagItems: GetItemsByTag.Response option
    IsLoading: bool
    Error: string option
    GuestSession: GuestSession.GuestSessionData
    ItemForm: ItemForm
    CollapsedComments: Set<string>
    ReplyingTo: {| ItemId: string; ParentId: string option |} option
}

type Msg =
    | UrlChanged of string list
    | LoadFeed
    | GotFeed of Result<GetFeed.Response, string>
    | LoadItem of string
    | GotItem of Result<GetItem.Response, string>
    | DismissError
    | ConnectEvents of string
    | DisconnectEvents
    | GotEvent of Models.Ws.NewCommentEvent
    | EventError of string
    | LoadTagItems of string
    | GotTagItems of Result<GetItemsByTag.Response, string>
    | SubmitComment
    | GotSubmitComment of Result<SubmitComment.Response, string>
    | SetNewItemTitle of string
    | SetNewItemLink of string
    | SetNewItemTags of string
    | SubmitItem
    | GotSubmitItem of Result<SubmitItem.Response, string>
    | ToggleCollapse of string
    | SetReplyTo of itemId: string * parentId: string option
    | CancelReply

let emptyItemForm = { Title = ""; Link = ""; Tags = "" }
