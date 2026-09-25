module Client.Review

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Elmish
open Models.Contributions

[<Import("reviewImage", "./contribution-client.mjs")>]
let image (url:string) (key:string) : JS.Promise<string> = jsNative
[<Import("releaseImage", "./contribution-client.mjs")>]
let release (url:string) : unit = jsNative

type ReplyDraft = {Id:string;Revision:int;Outcome:string;Text:string;Alternative:string}
type Model = {Draft:ReplyDraft option;Epoch:int;Data:Review option;Key:string;Busy:bool;Error:string option;Images:Map<string,string>;Zoom:string option;Page:int}
type Msg = Load | Key of string | UseKey | Loaded of int * Result<Review,string> | Act of ReviewChange
         | ImageLoaded of int * string * Result<string,string> | Show of string | Close | TurnPage of int
         | Respond of Note | Outcome of string | ReplyText of string | Alternative of string | CancelReply | SubmitReply
let empty epoch = {Draft=None;Epoch=epoch;Data=None;Key="";Busy=false;Error=None;Images=Map.empty;Zoom=None;Page=0}
let clear model =
    model.Images |> Map.iter(fun _ url->release url)
    model.Zoom |> Option.iter release
    empty (model.Epoch+1)
let load model = Cmd.OfPromise.either
                    (fun ()->Client.ContributionsApi.review model.Key model.Page |> Client.ContributionsApi.forView) ()
                    (fun data->Loaded(model.Epoch,Ok data)) (fun ex->Loaded(model.Epoch,Error ex.Message))
let enter key epoch = let m={empty epoch with Key=key;Busy=true} in m,load m
let update msg model =
    match msg with
    | Key value -> {model with Key=value},Cmd.none
    | Load when model.Busy -> model,Cmd.none
    | Load | UseKey ->
        let next={clear model with Key=model.Key;Busy=true;Page=model.Page}
        next,load next
    | TurnPage page ->
        let next={clear model with Key=model.Key;Busy=true;Page=max 0 page}
        next,load next
    | Loaded(epoch,Ok data) when epoch=model.Epoch ->
        {model with Data=Some data;Busy=false;Error=None;Draft=None},
        Array.append (data.Photos |> Array.map(fun p->p.Photo)) (data.Notes |> Array.collect(fun n->List.toArray n.Note.Photos))
        |> Array.distinctBy(fun p->p.Id)
        |> Array.map(fun p->Cmd.OfPromise.either (fun ()->image p.Thumbnail model.Key) () (fun url->ImageLoaded(epoch,p.Id,Ok url)) (fun ex->ImageLoaded(epoch,p.Id,Error ex.Message))) |> Cmd.batch
    | Loaded(epoch,Error error) when epoch=model.Epoch -> {model with Data=None;Busy=false;Error=Some error},Cmd.none
    | Loaded _ -> model,Cmd.none
    | Respond _ | Outcome _ | ReplyText _ | Alternative _ | CancelReply | SubmitReply when model.Busy -> model,Cmd.none
    | Respond note -> {model with Draft=Some{Id=note.Id;Revision=note.Revision;Outcome="confirmed";Text="";Alternative=""};Error=None},Cmd.none
    | Outcome outcome -> {model with Draft=model.Draft |> Option.map(fun d->{d with Outcome=outcome;Alternative=""})},Cmd.none
    | ReplyText text -> {model with Draft=model.Draft |> Option.map(fun d->{d with Text=text})},Cmd.none
    | Alternative value -> {model with Draft=model.Draft |> Option.map(fun d->{d with Alternative=value})},Cmd.none
    | CancelReply -> {model with Draft=None},Cmd.none
    | SubmitReply ->
        match model.Draft with
        | None -> model,Cmd.none
        | Some d when d.Outcome="alternative" && d.Alternative="" -> {model with Error=Some "Choose an alternative species."},Cmd.none
        | Some d ->
            let next={clear model with Key=model.Key;Busy=true;Page=model.Page;Draft=model.Draft}
            next,Cmd.OfPromise.either
                (fun ()->Client.ContributionsApi.reviewChange model.Key model.Page (Identify(d.Id,d.Revision,d.Outcome,d.Text,d.Alternative)) |> Client.ContributionsApi.forView) ()
                (fun data->Loaded(next.Epoch,Ok data)) (fun ex->Loaded(next.Epoch,Error ex.Message))
    | Act _ when model.Busy -> model,Cmd.none
    | Act command ->
        let next={clear model with Key=model.Key;Busy=true;Page=model.Page}
        next,Cmd.OfPromise.either
            (fun ()->Client.ContributionsApi.reviewChange model.Key model.Page command |> Client.ContributionsApi.forView) ()
            (fun data->Loaded(next.Epoch,Ok data)) (fun ex->Loaded(next.Epoch,Error ex.Message))
    | ImageLoaded(epoch,id,Ok url) when epoch=model.Epoch ->
        if id="zoom" then
            model.Zoom |> Option.iter release
            {model with Zoom=Some url},Cmd.none
        else {model with Images=Map.add id url model.Images},Cmd.none
    | ImageLoaded(_,_,Ok url) -> release url;model,Cmd.none
    | ImageLoaded(epoch,_,Error error) when epoch=model.Epoch -> {model with Error=Some error},Cmd.none
    | ImageLoaded _ -> model,Cmd.none
    | Show url -> model,Cmd.OfPromise.either (fun ()->image url model.Key) () (fun url->ImageLoaded(model.Epoch,"zoom",Ok url)) (fun ex->ImageLoaded(model.Epoch,"zoom",Error ex.Message))
    | Close -> model.Zoom |> Option.iter release;{model with Zoom=None},Cmd.none

let view (access:Capabilities) (plants:Models.Api.PlantCard list) model dispatch =
    let button label action = Client.Personal.btn label model.Busy action dispatch
    Html.main [prop.className "review-page section";prop.children [
        Html.span [prop.className "eyebrow";prop.text "Contribution review"]
        Html.h1 "Contributions to the guide"
        Html.p "Review the entries shared with your role. Attached photos stay private to the author and the relevant reviewers."
        if access.CanEditCatalogue then
            Html.a [prop.href "/admin";prop.text "Manage catalogue and role grants →"]
        button "Refresh review queue" Load
        if model.Busy then Html.p [prop.role "status";prop.text "Loading contributions…"]
        match model.Error with Some error -> Html.p [prop.role "alert";prop.text error] | None -> ()
        match model.Data with
        | None -> ()
        | Some data ->
            for purpose,title,allowed in ["correction","Corrections",access.CanReview;"identification","Identification requests",access.CanIdentify] do
                if allowed then
                    let entries=data.Notes |> Array.filter(fun item->item.Note.Purpose=purpose)
                    Html.section [prop.children [
                        Html.h2 title
                        if entries.Length=0 then Html.p "No entries on this page."
                        for item in entries do
                            let note=item.Note
                            Html.article [prop.className "review-note";prop.key note.Id;prop.children [
                                Html.a [prop.href("/plants/"+item.PlantId);prop.text item.PlantName]
                                Html.p [prop.className "note-text";prop.text note.Text]
                                Html.div [prop.className "note-attachments";prop.children [
                                    for photo in note.Photos do
                                        match Map.tryFind photo.Id model.Images with
                                        | None -> Html.span "Loading photograph…"
                                        | Some url ->
                                            let thumbnail=Html.img [prop.src url;prop.alt(if photo.Caption="" then "Attached photograph" else photo.Caption)]
                                            if photo.Width>=200 && photo.Height>=200 then
                                                Html.button [prop.type' "button";prop.ariaLabel "View photograph";prop.onClick(fun _->dispatch(Show photo.Image));prop.children [thumbnail]]
                                            else thumbnail
                                ]]
                                if purpose="correction" then
                                    Html.small(if note.Read then "Read" else "Unread")
                                    button (if note.Read then "Mark unread" else "Mark read") (Act(CorrectionRead(note.Id,note.Revision,not note.Read)))
                                else
                                    Client.Personal.responses note
                                    if note.Responses |> List.exists(fun r->r.Revision=note.Revision) |> not then
                                        match model.Draft with
                                        | Some draft when draft.Id=note.Id ->
                                            Html.form [prop.className "identification-editor";prop.onSubmit(fun e->e.preventDefault();dispatch SubmitReply);prop.children [
                                                Html.label [prop.children [Html.span "Identification";Html.select [prop.value draft.Outcome;prop.disabled model.Busy;prop.onChange(Outcome >> dispatch);prop.children [
                                                    for outcome in ["confirmed";"alternative";"rejected";"unknown"] do
                                                        Html.option [prop.value outcome;prop.text(outcomeLabel outcome)]
                                                ]]]]
                                                if draft.Outcome="alternative" then
                                                    Html.label [prop.children [Html.span "Alternative species";Html.select [prop.value draft.Alternative;prop.disabled model.Busy;prop.onChange(Alternative >> dispatch);prop.children [
                                                        Html.option [prop.value "";prop.text "Choose a species…"]
                                                        for plant in plants do
                                                            if plant.Id<>item.PlantId then Html.option [prop.value plant.Id;prop.text plant.ScientificName]
                                                    ]]]]
                                                Html.label [prop.children [Html.span "Response (optional)";Html.textarea [prop.value draft.Text;prop.maxLength 3000;prop.rows 3;prop.disabled model.Busy;prop.onChange(ReplyText >> dispatch)]]]
                                                Html.div [prop.className "personal-actions";prop.children [
                                                    Html.button [prop.type' "submit";prop.disabled model.Busy;prop.text "Send identification"]
                                                    button "Cancel" CancelReply
                                                ]]
                                            ]]
                                        | _ -> button "Respond" (Respond note)
                            ]]
                    ]]
            if access.CanReview then Html.section [prop.children [
                Html.h2 "Offered photographs"
                if data.Photos.Length=0 then Html.p "No offered photographs on this page."
                Html.div [prop.className "review-photos";prop.children [
                    for item in data.Photos do
                        let photo=item.Photo
                        Html.article [prop.key photo.Id;prop.className "review-photo";prop.children [
                            Html.a [prop.href("/plants/"+item.PlantId);prop.text item.PlantName]
                            match model.Images |> Map.tryFind photo.Id with
                            | Some url -> Html.img [prop.src url;prop.alt photo.Caption]
                            | None -> Html.p "Loading photograph…"
                            Html.p photo.Caption
                            if photo.Photographer<>"" then Html.small("Photo: "+photo.Photographer)
                            if photo.Width>=200 && photo.Height>=200 then button "View photograph" (Show photo.Image)
                            button "Include on species page" (Act(PromotePhoto(photo.Id,photo.Revision)))
                        ]]
                ]]
            ]]
            Html.nav [prop.className "personal-actions";prop.ariaLabel "Review queue pages";prop.children [
                if data.Page>0 then button "Previous page" (TurnPage(data.Page-1))
                Html.span("Page "+string(data.Page+1))
                if data.HasMore then button "Next page" (TurnPage(data.Page+1))
            ]]
        match model.Zoom with
        | None -> ()
        | Some url ->
            Html.div [prop.className "lightbox lightbox-photograph";prop.role "dialog";prop.custom("aria-modal",true);prop.ariaLabel "Submitted photograph";prop.onClick(fun _->dispatch Close);prop.children [
                Html.button [prop.className "lightbox-close";prop.autoFocus true;prop.ariaLabel "Close photograph";prop.text "×";prop.onClick(fun _->dispatch Close)]
                Html.img [prop.src url;prop.alt "Submitted photograph";prop.onClick(fun e->e.stopPropagation())]
            ]]
    ]]
