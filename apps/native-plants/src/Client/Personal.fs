module Client.Personal

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Elmish
open Models.Contributions

[<Import("uuid", "./contribution-client.mjs")>]
let uuid () : string = jsNative
[<Emit("$0.target.files[0]")>]
let selectedFile (event:obj) : obj = jsNative
[<Emit("$0.target.value = ''")>]
let resetFile (event:obj) : unit = jsNative
[<Emit("window.confirm($0)")>]
let confirm (message:string) : bool = jsNative

type Draft = { Id:string; Revision:int; Text:string; Purpose:string; PhotoIds:string list }
type PhotoDraft = { Id:string; Revision:int; Caption:string; Photographer:string; Offered:bool }
type Model = {
    PlantId:string; Epoch:int; Data:Personal option; Loading:bool; Busy:bool; Error:string option
    Draft:Draft option; PhotoDraft:PhotoDraft option; Notice:string option
    PendingAction:string; PendingId:string; FeedbackInGallery:bool
}
type Msg =
    | Load | Loaded of int * Result<Personal,string>
    | NewNote | EditNote of Note | NoteText of string | Correction of bool | CancelNote | SaveNote
    | DeleteNote of Note | EditPhoto of Photo | Caption of string | Photographer of string | Offered of bool
    | CancelPhoto | SavePhoto | DeletePhoto of Photo | Hero of string | Upload of obj
    | Saved of int * string * Result<Personal,string>
    | PhotoLimit
    | NewEntry of string | ToggleAttachment of string | UploadAttachment of obj
    | NotePurpose of string | Withdraw of Note | AttachmentLimit | Preview of Photo

[<Emit("requestAnimationFrame(()=>document.getElementById($0)?.scrollIntoView({block:'center',behavior:'smooth'}))")>]
let scrollEditor (id:string) : unit = jsNative
let focus id = Cmd.ofEffect(fun _ -> scrollEditor id)
let empty epoch = {PlantId="";Epoch=epoch;Data=None;Loading=false;Busy=false;Error=None;Draft=None;PhotoDraft=None;Notice=None;PendingAction="";PendingId="";FeedbackInGallery=false}
let load model = Cmd.OfPromise.either (fun ()->Client.ContributionsApi.personal model.PlantId |> Client.ContributionsApi.forView) () (fun data->Loaded(model.Epoch,Ok data)) (fun ex->Loaded(model.Epoch,Error ex.Message))
let enter plantId epoch =
    let model={empty epoch with PlantId=plantId;Loading=true}
    model,load model
let save label command model =
    match model.Data with
    | None -> model,Cmd.none
    | Some current ->
        let action,id,gallery =
            match command with
            | Models.Contributions.SaveNote(id,_,_,_) | SaveEntry(id,_,_,_,_) -> "saveNote",id,false
            | Models.Contributions.DeleteNote(id,_) -> "deleteNote",id,false
            | UpdatePhoto(id,_,_,_,_) -> "savePhoto",id,true
            | Models.Contributions.DeletePhoto(id,_) -> "deletePhoto",id,true
            | SelectHero id -> "hero",id,true
        let next={model with Busy=true;Error=None;Notice=None;Epoch=model.Epoch+1;Loading=false;PendingAction=action;PendingId=id;FeedbackInGallery=gallery}
        next,Cmd.OfPromise.either
            (fun ()->Client.ContributionsApi.change model.PlantId current.ViewerToken command |> Client.ContributionsApi.forView) ()
            (fun data->Saved(next.Epoch,label,Ok data)) (fun ex->Saved(next.Epoch,label,Error ex.Message))

let private notesFull model = model.Data |> Option.forall(fun d->d.NoteCapacity.Used>=d.NoteCapacity.Limit)
let private photosFull model = model.Data |> Option.forall(fun d->d.PhotoCapacity.Used>=d.PhotoCapacity.Limit)

let private noteLimit model =
    match model.Data with
    | None -> model,Cmd.none
    | Some data -> {model with Error=Some(sprintf "You can keep %i notes per species. Delete a note to add another." data.NoteCapacity.Limit);Notice=None;FeedbackInGallery=false},Cmd.none
let private photoLimit model =
    match model.Data with
    | None -> model,Cmd.none
    | Some data -> {model with Error=Some(sprintf "You can keep %i photos per species. Delete one of your photos to add another." data.PhotoCapacity.Limit);Notice=None;FeedbackInGallery=true},Cmd.none

let update msg model =
    match msg with
    | Load when model.PlantId="" || model.Loading || model.Busy -> model,Cmd.none
    | Load -> let m={model with Loading=true;Epoch=model.Epoch+1} in m,load m
    | Loaded(epoch,Ok data) when epoch=model.Epoch ->
        let changed=model.Data |> Option.exists(fun old->old.ViewerToken<>data.ViewerToken)
        {model with Data=Some data;Loading=false;Error=None
                    Draft=(if changed then None else model.Draft);PhotoDraft=(if changed then None else model.PhotoDraft)},Cmd.none
    | Loaded(epoch,Error error) when epoch=model.Epoch ->
        {model with Data=None;Loading=false;Draft=None;PhotoDraft=None;Error=Some error},Cmd.none
    | Loaded _ | Saved _ when model.PlantId="" -> model,Cmd.none
    | Saved(epoch,label,Ok data) when epoch=model.Epoch ->
        let draft=model.Draft |> Option.filter(fun d->not(List.contains model.PendingAction ["saveNote";"deleteNote"] && d.Id=model.PendingId))
        let draft =
            if model.PendingAction="uploadAttachment" then
                draft |> Option.map(fun d->{d with PhotoIds=d.PhotoIds @ [model.PendingId] |> List.distinct})
            else draft
        let photoDraft=model.PhotoDraft |> Option.filter(fun d->not(List.contains model.PendingAction ["savePhoto";"deletePhoto"] && d.Id=model.PendingId))
        {model with Data=Some data;Busy=false;Draft=draft;PhotoDraft=photoDraft;PendingAction="";PendingId="";Error=None;Notice=Some label},Cmd.none
    | Saved(epoch,_,Error error) when epoch=model.Epoch ->
        {model with Busy=false;Error=Some error;Notice=None},Cmd.none
    | Loaded _ | Saved _ -> model,Cmd.none
    | _ when model.Busy -> model,Cmd.none
    | NewNote | NewEntry _ when notesFull model -> noteLimit model
    | NewNote | NewEntry _ ->
        let purpose=match msg with NewEntry purpose -> purpose | _ -> "private"
        {model with Draft=Some{Id=uuid();Revision=0;Text="";Purpose=purpose;PhotoIds=[]};PhotoDraft=None;Notice=None;Error=None;FeedbackInGallery=false},focus "note-editor"
    | EditNote note -> {model with Draft=Some{Id=note.Id;Revision=note.Revision;Text=note.Text;Purpose=note.Purpose;PhotoIds=note.Photos |> List.map(fun p->p.Id)};PhotoDraft=None;Notice=None;Error=None;FeedbackInGallery=false},focus "note-editor"
    | NoteText text -> {model with Draft=model.Draft |> Option.map(fun d->{d with Text=text})},Cmd.none
    | Correction value ->
        {model with Draft=model.Draft |> Option.map(fun d->{d with Purpose=(if value then "correction" else "private");PhotoIds=[]})},Cmd.none
    | NotePurpose purpose ->
        {model with Draft=model.Draft |> Option.map(fun d->{d with Purpose=purpose;PhotoIds=if purpose="private" then [] else d.PhotoIds})},Cmd.none
    | ToggleAttachment id ->
        let draft=model.Draft |> Option.map(fun d ->
            if d.Purpose="private" then d
            else {d with PhotoIds=if List.contains id d.PhotoIds then List.filter((<>) id) d.PhotoIds else d.PhotoIds @ [id]})
        {model with Draft=draft},Cmd.none
    | Withdraw note -> save "Submission withdrawn. Your text is now a private note." (SaveEntry(note.Id,note.Revision,note.Text,"private",[])) model
    | Preview _ -> model,Cmd.none
    | CancelNote -> {model with Draft=None},Cmd.none
    | SaveNote ->
        match model.Draft with
        | Some d when d.Revision=0 && notesFull model -> noteLimit model
        | Some d when d.Purpose="identification" && d.PhotoIds.IsEmpty ->
            {model with Error=Some "An ID request needs at least one photograph.";Notice=None;FeedbackInGallery=false},Cmd.none
        | Some d -> save (if d.Purpose="private" then "Private note saved." else purposeLabel d.Purpose+" submitted.") (SaveEntry(d.Id,d.Revision,d.Text,d.Purpose,d.PhotoIds)) model
        | None -> model,Cmd.none
    | DeleteNote note -> save "Note deleted." (Models.Contributions.DeleteNote(note.Id,note.Revision)) model
    | EditPhoto photo ->
        {model with Draft=None;PhotoDraft=Some{Id=photo.Id;Revision=photo.Revision;Caption=photo.Caption;Photographer=photo.Photographer;Offered=photo.Offered};Notice=None;Error=None;FeedbackInGallery=true},focus "photo-editor"
    | Caption value -> {model with PhotoDraft=model.PhotoDraft |> Option.map(fun d->{d with Caption=value})},Cmd.none
    | Photographer value -> {model with PhotoDraft=model.PhotoDraft |> Option.map(fun d->{d with Photographer=value})},Cmd.none
    | Offered value -> {model with PhotoDraft=model.PhotoDraft |> Option.map(fun d->{d with Offered=value})},Cmd.none
    | CancelPhoto -> {model with PhotoDraft=None},Cmd.none
    | SavePhoto ->
        match model.PhotoDraft with
        | Some d -> save "Photograph details saved." (UpdatePhoto(d.Id,d.Revision,d.Caption,d.Photographer,d.Offered)) model
        | None -> model,Cmd.none
    | DeletePhoto photo -> save "Your photograph was removed." (Models.Contributions.DeletePhoto(photo.Id,photo.Revision)) model
    | Hero id -> save (if id="" then "Using the site’s hero photograph." else "Your hero photograph is selected.") (SelectHero id) model
    | PhotoLimit -> photoLimit model
    | AttachmentLimit -> let m,c=photoLimit model in {m with FeedbackInGallery=false},c
    | Upload _ | UploadAttachment _ when photosFull model ->
        let m,c=photoLimit model
        {m with FeedbackInGallery=match msg with UploadAttachment _ -> false | _ -> true},c
    | Upload file | UploadAttachment file ->
        match model.Data with
        | None -> model,Cmd.none
        | Some _ when (match msg,model.Draft with UploadAttachment _,Some d -> d.Purpose="private" | UploadAttachment _,None -> true | _ -> false) -> model,Cmd.none
        | Some current ->
            let attachment=match msg with UploadAttachment _ -> true | _ -> false
            let id=uuid()
            let next={model with Busy=true;Error=None;Notice=Some "Preparing and uploading your photograph…";Epoch=model.Epoch+1;Loading=false;PendingAction=(if attachment then "uploadAttachment" else "upload");PendingId=id;FeedbackInGallery=not attachment}
            next,Cmd.OfPromise.either (fun ()->Client.ContributionsApi.upload model.PlantId id file current.ViewerToken |> Client.ContributionsApi.forView) ()
                (fun data->Saved(next.Epoch,"Photograph added.",Ok data))
                (fun ex->Saved(next.Epoch,"",Error ex.Message))

let apiPhoto (photo:Photo) : Models.Api.Photo =
    {Id="personal-"+photo.Id;Image=photo.Image;Thumbnail=photo.Thumbnail;Caption=photo.Caption;Photographer=photo.Photographer}

/// Only the detail projection changes. Catalogue cards and other visitors keep the editor's hero.
let compose (personal:Personal option) (plant:Models.Api.PlantDetail) =
    match personal with
    | None -> plant
    | Some data ->
        let existing=plant.Photos |> List.map(fun p->p.Id) |> Set.ofList
        let displayed (p:Photo) =
            plant.Photos |> List.tryFind(fun published->published.Id=p.PublicPhotoId) |> Option.defaultWith(fun ()->apiPhoto p)
        let extras=data.Photos |> Array.filter(fun p->not(Set.contains p.PublicPhotoId existing)) |> Array.map apiPhoto |> Array.toList
        let hero=data.Photos |> Array.tryFind(fun p->p.Id=data.HeroPhotoId) |> Option.map displayed |> Option.orElse plant.Card.Photo
        {plant with Card={plant.Card with Photo=hero};Photos=plant.Photos @ extras}

let ownPhoto model (photo:Models.Api.Photo) =
    model.Data |> Option.bind(fun d->d.Photos |> Array.tryFind(fun p->"personal-"+p.Id=photo.Id || (p.PublicPhotoId<>"" && p.PublicPhotoId=photo.Id)))
let btn (label:string) disabled action dispatch = Html.button [prop.type' "button";prop.disabled disabled;prop.text label;prop.onClick(fun _->dispatch action)]
let controls model photo dispatch =
    match ownPhoto model photo with
    | None -> Html.none
    | Some p ->
        let hero=model.Data |> Option.exists(fun d->d.HeroPhotoId=p.Id)
        Html.div [prop.className "personal-photo-controls";prop.children [
            Html.span [prop.className "personal-badge";prop.text(if p.PublicPhotoId<>"" then "Your photo · included in the guide" elif p.Offered then "Your photo · offered to the guide" else "Your photo · private")]
            btn (if hero then "Use site hero" else "Make my hero") model.Busy (Hero(if hero then "" else p.Id)) dispatch
            btn "Photo details" model.Busy (EditPhoto p) dispatch
        ]]

let private icon (paths:string list) =
    Html.span [prop.className "action-icon";prop.ariaHidden true;prop.children [
        Svg.svg [svg.viewBox(0,0,24,24);svg.width 24;svg.height 24;svg.fill "none";svg.stroke "currentColor"
                 svg.strokeWidth 1.7;svg.strokeLineCap "round";svg.strokeLineJoin "round"
                 svg.children [for path in paths do Svg.path [svg.d path]]]
    ]]
let private cameraPlus () = icon ["M13.5 4h-4L7 7H4a2 2 0 0 0-2 2v10a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-8";"M15 13a3 3 0 1 1-6 0 3 3 0 0 1 6 0";"M20 2v6m-3-3h6"]
let private pencil () = icon ["m16 3 5 5-12 12-6 1 1-6Z";"m14 5 5 5"]
let private trash () = icon ["M3 6h18M9 6V3h6v3M5 6l1 15h12l1-15M10 10v7M14 10v7"]
let private iconButton label icon disabled action =
    Html.button [prop.type' "button";prop.className "icon-button";prop.ariaLabel label;prop.title label
                 prop.disabled disabled;prop.onClick(fun _->action());prop.children [icon]]

[<Emit("document.getElementById($0)?.click()")>]
let private openPicker (id:string) : unit = jsNative

let uploadTile model dispatch =
    let inputId="photo-upload-"+model.PlantId
    Html.figure [prop.className "photo-upload-slot";prop.children [
        iconButton "Add a photograph" (cameraPlus()) (model.Busy || model.Data.IsNone)
            (fun ()->if photosFull model then dispatch PhotoLimit else openPicker inputId)
        Html.input [prop.id inputId;prop.type' "file";prop.hidden true;prop.accept "image/jpeg,image/png,image/webp"
                    prop.disabled(model.Busy || model.Data.IsNone);prop.ariaLabel "Choose a photograph"
                    prop.onChange(fun (e:Browser.Types.Event)->let f=selectedFile e in resetFile e; if not(isNull f) then dispatch(Upload f))]
    ]]

let galleryFeedback model =
    Html.div [prop.className "gallery-feedback";prop.children [
        if model.FeedbackInGallery then
            match model.Error with Some error -> Html.p [prop.role "alert";prop.text error] |None -> ()
            match model.Notice with Some notice -> Html.p [prop.role "status";prop.text notice] |None -> ()
    ]]

let responses (note:Note) =
    Html.div [prop.className "identification-responses";prop.children [
        for response in note.Responses do
            let current=note.Purpose="identification" && response.Revision=note.Revision
            Html.article [prop.key response.Id;prop.className("id-outcome id-outcome-"+response.Outcome+(if current then "" else " previous-response"));prop.children [
                Html.strong(outcomeLabel response.Outcome)
                Html.small(" · "+response.ReviewerName+(if current then "" else " · earlier version"))
                if response.AlternativePlantId<>"" then
                    Html.a [prop.href("/plants/"+response.AlternativePlantId);prop.text response.AlternativeName]
                if response.Text<>"" then Html.p [prop.className "note-text";prop.text response.Text]
                if not current then Html.details [prop.children [Html.summary "Original request";Html.p [prop.className "note-text";prop.text response.SubmittedText]]]
            ]]
    ]]

let view model login dispatch =
    Html.section [prop.className "personal-content";prop.id "your-observations";prop.children [
        Html.div [prop.className "notebook-heading";prop.children [
            Html.h2 "Field notes"
            if model.Data.IsSome then
                Html.div [prop.className "personal-actions";prop.children [
                    btn "Private Note" model.Busy NewNote dispatch
                    btn "Correction" model.Busy (NewEntry "correction") dispatch
                    btn "ID request" model.Busy (NewEntry "identification") dispatch
                ]]
        ]]
        match model.Data with
        | None ->
            Html.p [prop.role "status";prop.text(if model.Loading then "Opening your notebook…" else "Your notebook is unavailable.")]
            if not model.Loading then
                btn "Try again" false Load dispatch
                Html.button [prop.type' "button";prop.text "Login";prop.onClick(fun _->login())]
        | Some data ->
            match model.Draft with
            | Some draft ->
                Html.form [prop.className "note-editor";prop.id "note-editor";prop.onSubmit(fun e->e.preventDefault();dispatch SaveNote);prop.children [
                    Html.label [prop.children [Html.span "Your note";Html.textarea [prop.value draft.Text;prop.maxLength 6000;prop.rows 5;prop.disabled model.Busy;prop.onChange(NoteText >> dispatch);prop.autoFocus true]]]
                    Html.label [prop.children [
                        Html.span "Purpose"
                        Html.select [prop.value draft.Purpose;prop.disabled model.Busy;prop.onChange(NotePurpose >> dispatch);prop.children [
                            for purpose in ["private";"correction";"identification"] do Html.option [prop.value purpose;prop.text(purposeLabel purpose)]
                        ]]
                    ]]
                    let audience =
                        match draft.Purpose with
                        | "correction" -> "Your text and selected photos will be shared with curators."
                        | "identification" -> "Your text and selected photos will be shared with identifiers. Include at least one photo."
                        | _ -> "Only you can read this note."
                    Html.p [prop.className "entry-audience";prop.text audience]
                    if draft.Purpose<>"private" then
                        Html.fieldSet [prop.className "attachment-picker";prop.disabled model.Busy;prop.children [
                            Html.legend "Photographs"
                            Html.div [prop.className "attachment-options";prop.children [
                                for photo in data.Photos do
                                    let selected=List.contains photo.Id draft.PhotoIds
                                    Html.button [prop.key photo.Id;prop.type' "button";prop.className(if selected then "attachment-choice selected" else "attachment-choice")
                                                 prop.ariaLabel((if selected then "Unlink " else "Attach ")+(if photo.Caption="" then "photograph" else photo.Caption))
                                                 prop.custom("aria-pressed",selected);prop.onClick(fun _->dispatch(ToggleAttachment photo.Id))
                                                 prop.children [Html.img [prop.src photo.Thumbnail;prop.alt photo.Caption];Html.span(if selected then "Selected" else "Select")]]
                                let inputId="attachment-upload-"+model.PlantId
                                iconButton "Upload and attach a photograph" (cameraPlus()) model.Busy
                                    (fun ()->if photosFull model then dispatch AttachmentLimit else openPicker inputId)
                                Html.input [prop.id inputId;prop.type' "file";prop.hidden true;prop.accept "image/jpeg,image/png,image/webp";prop.ariaLabel "Upload an attachment"
                                            prop.onChange(fun (e:Browser.Types.Event)->let file=selectedFile e in resetFile e; if not(isNull file) then dispatch(UploadAttachment file))]
                            ]]
                            Html.small "Uploads also join your photo roll. Sharing with reviewers does not offer a photo for the public guide."
                        ]]
                    Html.div [prop.className "personal-actions";prop.children [Html.button [prop.type' "submit";prop.disabled(model.Busy || draft.Text.Trim()="");prop.text(if draft.Purpose="private" then "Save note" else "Submit")];btn "Cancel" model.Busy CancelNote dispatch]]
                ]]
            | None -> ()
            for note in data.Notes do
                Html.article [prop.key note.Id;prop.className "personal-note";prop.children [
                    Html.p [prop.className "note-text";prop.text note.Text]
                    let status =
                        match note.Purpose with
                        | "correction" -> if note.Read then "Correction · read by a curator" else "Correction · awaiting review"
                        | "identification" -> if note.Responses |> List.exists(fun r->r.Revision=note.Revision) then "ID request · answered" else "ID request · awaiting identification"
                        | _ -> "Private Note"
                    Html.small status
                    Html.div [prop.className "note-attachments";prop.children [
                        for photo in note.Photos do
                            let thumbnail=Html.img [prop.src photo.Thumbnail;prop.alt(if photo.Caption="" then "Attached photograph" else photo.Caption)]
                            if photo.Width>=200 && photo.Height>=200 then
                                Html.button [prop.key photo.Id;prop.type' "button";prop.ariaLabel "View photograph";prop.onClick(fun _->dispatch(Preview photo));prop.children [thumbnail]]
                            else thumbnail
                    ]]
                    responses note
                    if note.Purpose<>"private" then
                        Html.button [prop.type' "button";prop.className "withdraw-note";prop.disabled model.Busy;prop.text "Withdraw"
                                     prop.onClick(fun _->if confirm "Withdraw this submission? The text becomes a private note; photos remain in your roll and earlier responses remain in your notebook." then dispatch(Withdraw note))]
                    Html.div [prop.className "note-actions";prop.children [
                        iconButton "Edit note" (pencil()) model.Busy (fun ()->dispatch(EditNote note))
                        iconButton "Delete note" (trash()) model.Busy (fun ()->if confirm "Delete this note?" then dispatch(DeleteNote note))
                    ]]
                ]]
            if data.Notes.Length=0 && model.Draft.IsNone then Html.p [prop.className "personal-empty";prop.text "Record what you notice: flowers, fruit, a place, a question."]
        if not model.FeedbackInGallery then
            match model.Error with Some error -> Html.p [prop.role "alert";prop.text error] |None -> ()
            match model.Notice with Some notice -> Html.p [prop.role "status";prop.text notice] |None -> ()
    ]]

let photoEditor model dispatch =
    Html.div [prop.className "gallery-photo-editor";prop.children [
        match model.Data with
        | None -> ()
        | Some data ->
            match model.PhotoDraft with
            | Some draft ->
                let published=data.Photos |> Array.exists(fun p->p.Id=draft.Id && p.PublicPhotoId<>"")
                Html.form [prop.className "photo-editor";prop.id "photo-editor";prop.onSubmit(fun e->e.preventDefault();dispatch SavePhoto);prop.children [
                    Html.h3 "Your photograph"
                    Html.label [prop.children [Html.span "Caption";Html.input [prop.value draft.Caption;prop.maxLength 500;prop.disabled model.Busy;prop.onChange(Caption >> dispatch);prop.autoFocus true]]]
                    Html.label [prop.children [Html.span "Photographer credit (optional)";Html.input [prop.value draft.Photographer;prop.maxLength 160;prop.disabled model.Busy;prop.onChange(Photographer >> dispatch)]]]
                    Html.label [prop.className "check-field";prop.children [Html.input [prop.type' "checkbox";prop.isChecked draft.Offered;prop.disabled(model.Busy || published);prop.onChange(Offered >> dispatch)];Html.span "Offer this photograph and its credit for the public guide"]]
                    Html.p(if published then "This photograph has been included in the guide. These edits affect your private copy; contact the site owner about the public copy." else "Offering lets reviewers see it. It becomes public only if a reviewer selects it for the species page.")
                    Html.div [prop.className "personal-actions";prop.children [
                        Html.button [prop.type' "submit";prop.disabled model.Busy;prop.text "Save photo details"]
                        btn "Cancel" model.Busy CancelPhoto dispatch
                        Html.button [prop.type' "button";prop.disabled model.Busy;prop.text "Delete my photo";prop.onClick(fun _->if confirm (if published then "Delete your private copy? The selected public photograph will remain in the guide." else "Delete this photograph?") then data.Photos |> Array.tryFind(fun p->p.Id=draft.Id) |> Option.iter(DeletePhoto >> dispatch))]]]
                ]]
            | None -> ()
    ]]
