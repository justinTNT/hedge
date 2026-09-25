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

type Model = {Epoch:int;Data:Review option;Key:string;Busy:bool;Error:string option;Images:Map<string,string>;Zoom:string option;Page:int}
type Msg = Load | Key of string | UseKey | Loaded of int * Result<Review,string> | Act of ReviewChange
         | ImageLoaded of int * string * Result<string,string> | Show of string | Close | TurnPage of int
let empty epoch = {Epoch=epoch;Data=None;Key="";Busy=false;Error=None;Images=Map.empty;Zoom=None;Page=0}
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
        {model with Data=Some data;Busy=false;Error=None},
        data.Photos |> Array.map(fun p->Cmd.OfPromise.either (fun ()->image p.Photo.Thumbnail model.Key) () (fun url->ImageLoaded(epoch,p.Photo.Id,Ok url)) (fun ex->ImageLoaded(epoch,p.Photo.Id,Error ex.Message))) |> Cmd.batch
    | Loaded(epoch,Error error) when epoch=model.Epoch -> {model with Data=None;Busy=false;Error=Some error},Cmd.none
    | Loaded _ -> model,Cmd.none
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

let view canManageCatalogue model dispatch =
    let button label action = Client.Personal.btn label model.Busy action dispatch
    Html.main [prop.className "review-page section";prop.children [
        Html.span [prop.className "eyebrow";prop.text "Editorial review"]
        Html.h1 "Contributions to the guide"
        Html.p "Read corrections and select offered photographs for species pages. Personal notes and photographs that haven’t been offered stay private."
        if canManageCatalogue then
            Html.a [prop.href "/admin";prop.text "Manage catalogue and curator grants →"]
        button "Refresh review queue" Load
        if model.Busy then Html.p [prop.role "status";prop.text "Loading contributions…"]
        match model.Error with Some error -> Html.p [prop.role "alert";prop.text error] | None -> ()
        match model.Data with
        | None -> ()
        | Some data ->
            Html.section [prop.children [
                Html.h2 "Corrections"
                if data.Notes.Length=0 then Html.p "No corrections on this page."
                for item in data.Notes do
                    let note=item.Note
                    Html.article [prop.className "review-note";prop.key note.Id;prop.children [
                        Html.a [prop.href("/plants/"+item.PlantId);prop.text item.PlantName]
                        Html.p [prop.className "note-text";prop.text note.Text]
                        Html.small(if note.Read then "Read" else "Unread")
                        button (if note.Read then "Mark unread" else "Mark read") (Act(CorrectionRead(note.Id,note.Revision,not note.Read)))
                    ]]
            ]]
            Html.section [prop.children [
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
            Html.div [prop.className "lightbox lightbox-photograph";prop.role "dialog";prop.custom("aria-modal",true);prop.ariaLabel "Offered photograph";prop.onClick(fun _->dispatch Close);prop.children [
                Html.button [prop.className "lightbox-close";prop.autoFocus true;prop.ariaLabel "Close photograph";prop.text "×";prop.onClick(fun _->dispatch Close)]
                Html.img [prop.src url;prop.alt "Offered photograph";prop.onClick(fun e->e.stopPropagation())]
            ]]
    ]]
