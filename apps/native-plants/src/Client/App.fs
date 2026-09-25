module Client.App

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Elmish
open Models.Api
open NativePlants.Catalogue

type Page = Home | Browse | Taxonomy | Detail of string | About | GlossaryPage | ReferencesPage | ReviewPage

type Model = {
    Page: Page; Query: SearchPlants.Query; Data: GetCatalogue.Response option; Plant: PlantDetail option
    Error: string option; DetailError: string option; Search: string; Suggest: bool; Active: int
    Limit: int; FiltersOpen: bool; Zoom: Photo option
    Auth: Client.Auth.Model; Access: Client.Access.Model
    Personal: Client.Personal.Model; Review: Client.Review.Model
    Request: int; DetailRequest: int; Polling: bool; Loading: bool; PhotoSizes: Map<string,int*int>
}
type Msg =
    | Loaded of int * Result<GetCatalogue.Response,Hedge.Http.ApiError>
    | PlantLoaded of int * string * Result<GetPlant.Response,Hedge.Http.ApiError>
    | Revised of Result<GetRevision.Response,Hedge.Http.ApiError>
    | Navigate of string | LocationChanged | Refresh
    | SearchChanged of string | SearchFocus | SearchKey of string | SubmitSearch
    | FilterChanged of string * string | ClearFilters | More | ToggleFilters | Zoom of Photo option
    | PhotoMeasured of string * int * int
    | Auth of Client.Auth.Msg
    | Personal of Client.Personal.Msg
    | Review of Client.Review.Msg
    | Access of Client.Access.Msg

let api = Client.ClientGen.createClient Client.Api.browserTransport
[<Emit("window.location.pathname")>]
let path () : string = jsNative
[<Emit("new URLSearchParams(window.location.search).get($0)")>]
let param (key: string) : string = jsNative
[<Emit("window.history.pushState({}, '', $0)")>]
let push (url: string) : unit = jsNative
[<Emit("window.scrollTo({top:0,behavior:'instant'})")>]
let top () : unit = jsNative
[<Emit("encodeURIComponent($0)")>]
let enc (s:string) : string = jsNative
[<Emit("document.title = $0")>]
let title (s:string) : unit = jsNative
[<Emit("(()=>{try{return decodeURIComponent(window.location.hash.slice(1))}catch{return ''}})()")>]
let fragment () : string = jsNative
[<Emit("document.getElementById($0)?.scrollIntoView({block:'start',behavior:'instant'})")>]
let scrollToId (id:string) : unit = jsNative
let sectionId (label:string) =
    "section-" + System.Text.RegularExpressions.Regex.Replace(label.ToLowerInvariant(),"[^a-z0-9]+","-").Trim('-')
let restoreFragment () =
    let id=fragment()
    if id<>"" then scrollToId (if id.StartsWith("section-") then sectionId(id.Substring(8)) else id)
[<Emit("window.addEventListener('popstate',()=> $0());setInterval(()=>{if(!document.hidden)$1()},15000);document.addEventListener('visibilitychange',()=>{if(!document.hidden)$1()});document.addEventListener('keydown',e=>{if(e.key==='Escape')$2()})")>]
let listen (location:unit->unit) (refresh:unit->unit) (escape:unit->unit) : unit = jsNative

let opt (s:string) = if isNull s || s.Trim()="" then None else Some s
let readQuery () : SearchPlants.Query =
    { Q=opt(param "q");Family=opt(param "family");Genus=opt(param "genus");Form=opt(param "form")
      Sun=opt(param "sun");Water=opt(param "water");Feature=opt(param "feature");Wildlife=opt(param "wildlife")
      Endemic=opt(param "endemic");Photos=opt(param "photos");Photographer=opt(param "photographer") }
let route () =
    let bits=(path()).Trim('/').Split('/')
    if bits.[0]="plants" && bits.Length>1 then Detail bits.[1]
    elif bits.[0]="plants" then Browse
    elif bits.[0]="taxonomy" then Taxonomy
    elif bits.[0]="about" then About
    elif bits.[0]="glossary" then GlossaryPage
    elif bits.[0]="references" then ReferencesPage
    elif bits.[0]="review" then ReviewPage else Home
let queryUrl (q: SearchPlants.Query) =
    let ps=["q",q.Q;"family",q.Family;"genus",q.Genus;"form",q.Form;"sun",q.Sun;"water",q.Water;"feature",q.Feature;"wildlife",q.Wildlife;"endemic",q.Endemic;"photos",q.Photos;"photographer",q.Photographer]
    let qs=ps |> List.choose (fun (k,v) -> v |> Option.map (fun x -> k+"="+enc x)) |> String.concat "&"
    "/plants"+(if qs="" then "" else "?"+qs)
let plantUrl (p:PlantCard) = "/plants/"+p.Id+"/"+p.Slug
let load request = Cmd.OfPromise.either api.getCatalogue () (fun r -> Loaded(request,r)) (fun e -> Loaded(request,Error(Hedge.Http.TransportFailure e.Message)))
let loadPlant request id = Cmd.OfPromise.either api.getPlant id (fun r -> PlantLoaded(request,id,r)) (fun e -> PlantLoaded(request,id,Error(Hedge.Http.TransportFailure e.Message)))
let currentPlant request = function Detail id -> loadPlant request id | _ -> Cmd.none
let revision = Cmd.OfPromise.either api.getRevision () Revised (fun e -> Revised(Error(Hedge.Http.TransportFailure e.Message)))
[<Emit("new Promise(resolve=>{const image=new Image();image.onload=()=>resolve([$0,image.naturalWidth,image.naturalHeight]);image.onerror=()=>resolve([$0,0,0]);image.src=$0;})")>]
let measurePhoto (url:string) : JS.Promise<string*int*int> = jsNative
let canEnlargePhoto width height = width>=200 && height>=200
let canViewPhoto model (photo:Photo) =
    model.PhotoSizes |> Map.tryFind photo.Image |> Option.exists(fun (w,h)->canEnlargePhoto w h)
let init () =
    let q=readQuery()
    let p=route()
    let auth,authCmd=Client.Auth.init()
    {Personal=Client.Personal.empty 0;Review=Client.Review.empty 0;Auth=auth;Access=Client.Access.empty 0;Page=p;Query=q;Data=None;Plant=None;Error=None;DetailError=None;Search=Option.defaultValue "" q.Q;Suggest=false;Active= -1;Limit=36;FiltersOpen=false;Zoom=None;Request=1;DetailRequest=1;Polling=false;Loading=true;PhotoSizes=Map.empty},
    Cmd.batch [load 1;currentPlant 1 p;Cmd.map Auth authCmd;Cmd.ofMsg(Access Client.Access.Refresh)
               Cmd.ofEffect(fun dispatch ->
                   listen (fun ()->dispatch LocationChanged) (fun ()->dispatch Refresh;dispatch(Auth Client.Auth.Refresh)) (fun ()->dispatch(Zoom None);dispatch(Auth Client.Auth.Close);dispatch(Review Client.Review.Close))
                   Client.Auth.listen (fun ()->dispatch(Auth Client.Auth.SessionCleared)) (fun ()->dispatch(Auth Client.Auth.Refresh))
                   Client.Access.listen (fun ()->dispatch(Access Client.Access.Refresh)) |> ignore)]
let suggested model = model.Data |> Option.map (fun d -> suggestions model.Search d.Plants) |> Option.defaultValue []
let setFilter key value (q:SearchPlants.Query) =
    match key with
    | "family" -> {q with Family=opt value;Genus=None}
    | "genus" -> {q with Genus=opt value}
    | "form" -> {q with Form=opt value}
    | "sun" -> {q with Sun=opt value}
    | "water" -> {q with Water=opt value}
    | "feature" -> {q with Feature=opt value}
    | "wildlife" -> {q with Wildlife=opt value}
    | "endemic" -> {q with Endemic=opt value}
    | "photos" -> {q with Photos=opt value}
    | "photographer" -> {q with Photographer=opt value}
    | _ -> q
let changeRoute model =
    let p=route()
    let q=readQuery()
    title "Native Plants of Northern Australia"
    let request=model.DetailRequest+1
    let personal,personalCmd =
        match p with Detail id when Client.Auth.signedIn model.Auth -> Client.Personal.enter id (model.Personal.Epoch+1) | _ -> Client.Personal.empty (model.Personal.Epoch+1),Cmd.none
    let review=Client.Review.clear model.Review
    let access=if p=ReviewPage then Client.Access.clear model.Access else model.Access
    let reviewCmd=if p=ReviewPage then Cmd.ofMsg(Access Client.Access.Refresh) else Cmd.none
    {model with Personal=personal;Review=review;Access=access;Page=p;Query=q;Search=Option.defaultValue "" q.Q;Plant=None;DetailError=None;Suggest=false;Active= -1;Limit=36;Zoom=None;DetailRequest=request},
    Cmd.batch [currentPlant request p;Cmd.map Personal personalCmd;reviewCmd]
let update msg model =
    match msg with
    | Loaded(request,Ok data) when request=model.Request -> {model with Data=Some data;Error=None;Loading=false},Cmd.none
    | Loaded(request,Error _) when request=model.Request -> {model with Data=None;Loading=false;Error=Some "We couldn’t load the plant collection. Please try again."},Cmd.none
    | Loaded _ -> model,Cmd.none
    | PlantLoaded(request,id,Ok data) when model.Page=Detail id && request=model.DetailRequest ->
        title (data.Plant.Card.ScientificName+" · Native Plants")
        let measurements = data.Plant.Photos |> List.filter(fun p -> not(Map.containsKey p.Image model.PhotoSizes))
                           |> List.map(fun p -> Cmd.OfPromise.perform measurePhoto p.Image PhotoMeasured)
        {model with Plant=Some data.Plant;DetailError=None},Cmd.batch measurements
    | PlantLoaded(request,id,Error _) when model.Page=Detail id && request=model.DetailRequest -> {model with Plant=None;DetailError=Some "This plant account is unavailable."},Cmd.none
    | PlantLoaded _ -> model,Cmd.none
    | Refresh when model.Loading || model.Polling -> model,Cmd.none
    | Refresh when model.Data.IsNone ->
        let r=model.Request+1
        let d=model.DetailRequest+1
        {model with Request=r;DetailRequest=d;Loading=true},Cmd.batch [load r;currentPlant d model.Page]
    | Refresh -> {model with Polling=true},revision
    | Revised(Ok r) when model.Data |> Option.exists(fun d->d.Revision=r.Revision) -> {model with Polling=false},Cmd.none
    | Revised(Ok _) ->
        let r=model.Request+1
        let d=model.DetailRequest+1
        {model with Request=r;DetailRequest=d;Loading=true;Polling=false},Cmd.batch [load r;currentPlant d model.Page]
    | Revised(Error _) -> {model with Polling=false;Data=None;Plant=None;DetailRequest=model.DetailRequest+1;Error=Some "The collection couldn’t be refreshed. Please try again.";DetailError=Some "The plant account couldn’t be refreshed."},Cmd.none
    | Navigate url -> push url;top();changeRoute model
    // A native fragment navigation keeps the rendered account available to the browser.
    | LocationChanged when route()=model.Page && readQuery()=model.Query -> model,Cmd.none
    | LocationChanged -> changeRoute model
    | SearchChanged s -> {model with Search=s;Suggest=true;Active= -1},Cmd.none
    | SearchFocus -> {model with Suggest=true},Cmd.none
    | SearchKey "Escape" -> {model with Suggest=false;Active= -1},Cmd.none
    | SearchKey "ArrowDown" -> {model with Active=min (List.length(suggested model)-1) (model.Active+1);Suggest=true},Cmd.none
    | SearchKey "ArrowUp" -> {model with Active=max -1 (model.Active-1)},Cmd.none
    | SearchKey "Enter" | SubmitSearch ->
        match if model.Suggest && model.Active>=0 then suggested model |> List.tryItem model.Active else None with
        | Some p -> model,Cmd.ofMsg(Navigate(plantUrl p))
        | None -> model,Cmd.ofMsg(Navigate(queryUrl {model.Query with Q=opt model.Search}))
    | SearchKey _ -> model,Cmd.none
    | FilterChanged(key,value) ->
        let q=setFilter key value model.Query
        push(queryUrl q)
        {model with Query=q;Page=Browse;Limit=36;Suggest=false},Cmd.none
    | ClearFilters -> model,Cmd.ofMsg(Navigate "/plants")
    | More -> {model with Limit=model.Limit+36},Cmd.none
    | ToggleFilters -> {model with FiltersOpen=not model.FiltersOpen},Cmd.none
    | Zoom(Some photo) when not(canViewPhoto model photo) -> model,Cmd.none
    | Zoom photo -> {model with Zoom=photo;Suggest=false},Cmd.none
    | PhotoMeasured(url,width,height) -> {model with PhotoSizes=Map.add url (width,height) model.PhotoSizes},Cmd.none
    | Auth msg ->
        let auth,cmd=Client.Auth.update msg model.Auth
        let cleared =
            match msg with Client.Auth.Logout | Client.Auth.SessionCleared -> true | _ -> auth.Account<>model.Auth.Account
        let personal=if cleared then Client.Personal.empty (model.Personal.Epoch+1) else model.Personal
        let review=if cleared then Client.Review.clear model.Review else model.Review
        let access=if cleared then Client.Access.clear model.Access else model.Access
        let ready =
            match msg with
            | Client.Auth.Loaded(request,Some result) -> request=model.Auth.Request && result.Ready && not auth.LoggingOut
            | Client.Auth.LoggedOut true -> true
            | _ -> false
        let personal,personalCmd =
            if ready && Client.Auth.signedIn auth then
                match model.Page with
                | Detail id when personal.PlantId<>id -> Client.Personal.enter id (personal.Epoch+1)
                | Detail _ -> Client.Personal.update Client.Personal.Load personal
                | _ -> personal,Cmd.none
            else personal,Cmd.none
        let accessCmd =
            if (ready || cleared) && not auth.LoggingOut then Cmd.ofMsg(Access Client.Access.Refresh) else Cmd.none
        {model with Auth=auth;Access=access;Personal=personal;Review=review;Zoom=if cleared then None else model.Zoom},
        Cmd.batch [Cmd.map Auth cmd;Cmd.map Personal personalCmd;accessCmd]
    | Personal _ when not(Client.Auth.signedIn model.Auth) -> model,Cmd.none
    | Personal(Client.Personal.Preview photo) -> {model with Zoom=Some(Client.Personal.apiPhoto photo)},Cmd.none
    | Personal msg ->
        let personal,cmd=Client.Personal.update msg model.Personal
        let dataChanged=personal.Data<>model.Personal.Data
        let sizes=personal.Data |> Option.map(fun d->d.Photos |> Array.fold(fun sizes p->Map.add p.Image (p.Width,p.Height) sizes) model.PhotoSizes) |> Option.defaultValue model.PhotoSizes
        {model with Personal=personal;PhotoSizes=sizes;Zoom=if dataChanged then None else model.Zoom},Cmd.map Personal cmd
    | Review(Client.Review.ImageLoaded(_,_,Ok url)) when model.Access.Key<>Client.Access.storedKey() || not(Client.Access.canReview model.Access) ->
        Client.Review.release url
        model,Cmd.none
    | Review _ when model.Access.Key<>Client.Access.storedKey() ->
        {model with Access=Client.Access.clear model.Access;Review=Client.Review.clear model.Review},
        Cmd.ofMsg(Access Client.Access.Refresh)
    | Review _ when not(Client.Access.canReview model.Access) -> model,Cmd.none
    | Review msg ->
        let review,cmd=Client.Review.update msg model.Review
        match msg with
        | Client.Review.Loaded(epoch,Error _) when epoch=model.Review.Epoch ->
            {model with Review={Client.Review.clear review with Error=review.Error};Access=Client.Access.clear model.Access},
            Cmd.ofMsg(Access Client.Access.Refresh)
        | _ -> {model with Review=review},Cmd.map Review cmd
    | Access msg ->
        let access,cmd=Client.Access.update msg model.Access
        let review,reviewCmd =
            if not(Client.Access.canReview access) then {Client.Review.clear model.Review with Error=model.Review.Error},Cmd.none
            elif model.Page=ReviewPage && model.Access.Data<>access.Data && access.Data.IsSome then
                let cleared=Client.Review.clear model.Review
                Client.Review.enter access.Key cleared.Epoch
            elif model.Page=ReviewPage && model.Review.Data.IsNone && model.Review.Error.IsNone && not model.Review.Busy then
                Client.Review.enter access.Key (model.Review.Epoch+1)
            else model.Review,Cmd.none
        {model with Access=access;Review=review},Cmd.batch [Cmd.map Access cmd;Cmd.map Review reviewCmd]

let a (url:string) (label:string) dispatch = Html.a [prop.href url;prop.text label;prop.onClick(fun e -> if not(e.ctrlKey || e.metaKey) then e.preventDefault();dispatch(Navigate url))]
let button (label:string) action dispatch = Html.button [prop.type' "button";prop.text label;prop.onClick(fun _->dispatch action)]
let leaf = Html.span [prop.className "leaf-mark";prop.ariaHidden true;prop.text "⌁"]
let card (p:PlantCard) dispatch =
    Html.a [prop.className "plant-card";prop.href(plantUrl p);prop.onClick(fun e -> if not(e.ctrlKey || e.metaKey) then e.preventDefault();dispatch(Navigate(plantUrl p)))
            prop.children [
                Html.div [prop.className "card-image";prop.children [
                    match p.Photo with
                    | Some photo -> Html.img [prop.src photo.Thumbnail;prop.alt p.ScientificName;prop.custom("loading","lazy")]
                    | None -> Html.div [prop.className "no-photo";prop.children [leaf;Html.span "Photograph to come"]]
                    if p.EndemicNt then Html.span [prop.className "endemic-badge";prop.text "NT endemic"]
                ]]
                Html.div [prop.className "card-body";prop.children [
                    Html.span [prop.className "eyebrow";prop.text p.Family]
                    Html.h3 [prop.text p.ScientificName]
                    Html.p [prop.className "common-name";prop.text(if p.CommonNames="" then String.concat " · " p.Forms else p.CommonNames.Replace(" | ",", "))]
                    Html.div [prop.className "card-meta";prop.children [Html.span(String.concat " / " p.Forms);Html.span p.Height]]
                ]]
            ]]

let searchBox model dispatch =
    let choices=suggested model
    Html.div [prop.className "search-wrap";prop.children [
        Html.form [prop.className "search-box";prop.onSubmit(fun e->e.preventDefault();dispatch SubmitSearch);prop.children [
            Html.span [prop.className "search-symbol";prop.ariaHidden true;prop.text "⌕"]
            Html.input [prop.id "plant-search";prop.type' "search";prop.placeholder "Try a plant name, wattle or Grevillea…";prop.value model.Search
                        prop.role "combobox";prop.ariaLabel "Search plants by scientific or common name";prop.ariaExpanded(model.Suggest && not choices.IsEmpty)
                        prop.ariaControls "plant-suggestions";prop.custom("aria-autocomplete","list");prop.autoComplete "off"
                        if model.Active>=0 then prop.custom("aria-activedescendant","suggestion-"+string model.Active)
                        prop.onChange(fun s->dispatch(SearchChanged s));prop.onFocus(fun _->dispatch SearchFocus)
                        prop.onKeyDown(fun e -> if List.contains e.key ["ArrowDown";"ArrowUp";"Escape"] then e.preventDefault();dispatch(SearchKey e.key))]
            Html.button [prop.type' "submit";prop.text "Find a plant";prop.className "search-submit"]
        ]]
        if model.Suggest && not choices.IsEmpty then
            Html.div [prop.id "plant-suggestions";prop.role "listbox";prop.className "suggestions";prop.children [
                for i,p in List.indexed choices do
                    Html.div [prop.id("suggestion-"+string i);prop.role "option";prop.ariaSelected((i=model.Active));prop.className(if i=model.Active then "suggestion active" else "suggestion")
                              prop.onClick(fun _->dispatch(Navigate(plantUrl p)));prop.children [
                                Html.div [prop.children [Html.strong p.ScientificName;Html.span p.CommonNames]]
                                Html.small p.Family
                              ]]
            ]]
    ]]

let header model dispatch =
    Html.header [prop.className "site-header";prop.children [
        Html.a [prop.className "brand";prop.href "/";prop.onClick(fun e->e.preventDefault();dispatch(Navigate "/"));prop.children [leaf;Html.div [prop.children [Html.strong "Native Plants";Html.span "NORTHERN AUSTRALIA"]]]]
        Html.nav [prop.ariaLabel "Main navigation";prop.children [a "/plants" "Explore plants" dispatch;a "/taxonomy" "Taxonomy" dispatch;a "/about" "About the guide" dispatch]]
        Html.div [prop.className "header-account";prop.children [Html.span [prop.className "header-note";prop.text "A collection by John Brock"];Client.Auth.view model.Auth (Auth >> dispatch)]]
    ]]

let footer model dispatch =
    Html.footer [prop.className "site-footer";prop.children [
        Html.div [prop.children [Html.strong "Native Plants of Northern Australia";Html.p "A guide to John Brock’s collection, with a focus on the Top End."]]
        Html.div [prop.className "footer-links";prop.children [
            a "/glossary" "Glossary" dispatch
            a "/references" "References & bibliography" dispatch
            a "/about" "About the guide" dispatch
            if Client.Access.canEdit model.Access then
                Html.a [prop.href "/admin";prop.text "Edit catalogue"]
            if Client.Access.canReview model.Access then
                a "/review" "Review contributions" dispatch
        ]]
    ]]

let home model (data:GetCatalogue.Response) dispatch =
    let hero=data.Plants |> List.tryFind(fun p->p.ScientificName="Eucalyptus miniata")
    let featuredNames=["Corymbia ptychocarpa";"Nelumbo nucifera";"Grevillea pteridifolia";"Banksia dentata";"Syzygium suborbiculare";"Podocarpus grayae";"Cycas armstrongii";"Dillenia alata"]
    let featured=featuredNames |> List.choose(fun n->data.Plants |> List.tryFind(fun p->p.ScientificName=n))
    Html.main [prop.children [
        Html.section [prop.className "hero";prop.children [
            Html.div [prop.className "hero-copy";prop.children [
                Html.div [prop.className "eyebrow";prop.text "A field guide to the northern landscape"]
                Html.h1 [prop.children [Html.text "Discover the plants";Html.br [];Html.em "of northern Australia."]]
                Html.p "From sandstone escarpments to monsoon forests. Explore the plants, their distinctive features and the places they call home."
                searchBox model dispatch
                Html.div [prop.className "hero-stats";prop.children [Html.span(sprintf "%i plant accounts" data.Plants.Length);Html.span(sprintf "%i families" data.Families.Length);Html.span "One remarkable region"]]
            ]]
            match hero |> Option.bind(fun p->p.Photo |> Option.map(fun photo->p,photo)) with
            | Some(p,photo) -> Html.a [prop.href(plantUrl p);prop.className "hero-photo";prop.onClick(fun e->e.preventDefault();dispatch(Navigate(plantUrl p)));prop.children [
                Html.img [prop.src photo.Image;prop.alt "Orange flowers of the Darwin woollybutt, Eucalyptus miniata";prop.custom("fetchpriority","high")]
                Html.div [prop.className "hero-caption";prop.children [Html.span "IN THE TOP END";Html.strong "Darwin woollybutt";Html.em p.ScientificName;Html.span "Meet this plant ↗"]]
              ]]
            | None -> Html.none
        ]]
        Html.section [prop.className "browse-forms";prop.children [
            Html.span [prop.className "eyebrow";prop.text "Find your way into the collection"]
            Html.div [prop.className "form-links";prop.children [
                for name in ["Tree";"Shrub";"Climber";"Herb";"Fern";"Aquatic";"Palm";"Cycad"] do
                    let count=data.Forms |> List.tryFind(fun f->f.Name=name) |> Option.map(fun f->f.Count) |> Option.defaultValue 0
                    Html.a [prop.href(queryUrl {emptyQuery with Form=Some name});prop.onClick(fun e->e.preventDefault();dispatch(Navigate(queryUrl {emptyQuery with Form=Some name})));prop.children [Html.span name;Html.small(string count);Html.span "↗"]]
            ]]
        ]]
        Html.section [prop.className "featured section";prop.children [
            Html.div [prop.className "section-heading";prop.children [Html.div [prop.children [Html.span [prop.className "eyebrow";prop.text "A closer look"];Html.h2 "Plants to get to know"]];a "/plants" "Explore the full collection →" dispatch]]
            Html.div [prop.className "plant-grid";prop.children(featured |> List.map(fun p->card p dispatch))]
        ]]
        Html.section [prop.className "taxonomy-prompt";prop.children [
            Html.div [prop.children [Html.span [prop.className "eyebrow";prop.text "Follow the family tree"];Html.h2 "A little botanical curiosity goes a long way.";Html.p "Explore related plants, from the familiar wattles of Fabaceae to the palms, cycads and orchids of the north."]]
            a "/taxonomy" "Browse plant families →" dispatch
        ]]
    ]]

let filterSelect (label:string) key selected (options:string list) dispatch =
    Html.label [prop.className "filter-field";prop.children [
        Html.span label
        Html.select [prop.value(Option.defaultValue "" selected);prop.onChange(fun (v:string)->dispatch(FilterChanged(key,v)));prop.children [Html.option [prop.value "";prop.text "All"];for name in options do Html.option [prop.value name;prop.text name]]]
    ]]
let checkFilter (label:string) key (value:string option) dispatch =
    Html.label [prop.className "check-filter";prop.children [Html.input [prop.type' "checkbox";prop.isChecked((value=Some "true"));prop.onChange(fun (v:bool)->dispatch(FilterChanged(key,if v then "true" else "")))];Html.span label]]
let browse model (data:GetCatalogue.Response) dispatch =
    let results=filter model.Query data.Plants
    let heading=Option.orElse model.Query.Family model.Query.Genus |> Option.defaultValue "Explore the plants"
    let selectedCount=[model.Query.Family;model.Query.Genus;model.Query.Form;model.Query.Sun;model.Query.Water;model.Query.Feature;model.Query.Wildlife;model.Query.Endemic;model.Query.Photos;model.Query.Photographer] |> List.choose id |> List.length
    Html.main [prop.className "browse-page section";prop.children [
        Html.div [prop.className "page-heading";prop.children [Html.span [prop.className "eyebrow";prop.text "The plant collection"];Html.h1 heading;Html.p "Search by name, explore a family, or find plants suited to a place."]]
        searchBox model dispatch
        Html.div [prop.className "browse-layout";prop.children [
            Html.button [prop.className "mobile-filter-toggle";prop.text(sprintf "Filters%s" (if selectedCount=0 then "" else " · "+string selectedCount));prop.onClick(fun _->dispatch ToggleFilters)]
            Html.aside [prop.className("filters"+(if model.FiltersOpen then " open" else ""));prop.ariaLabel "Filter plants";prop.children [
                Html.div [prop.className "filter-heading";prop.children [Html.h2 "Narrow your search";if selectedCount>0 then button "Clear" ClearFilters dispatch]]
                filterSelect "Plant family" "family" model.Query.Family (data.Families |> List.map(fun f->f.Name)) dispatch
                filterSelect "Genus" "genus" model.Query.Genus (data.Plants |> List.filter(fun p->model.Query.Family.IsNone || model.Query.Family=Some p.Family) |> List.map(fun p->p.Genus) |> List.distinct |> List.sort) dispatch
                filterSelect "Growth form" "form" model.Query.Form (data.Forms |> List.map(fun f->f.Name)) dispatch
                filterSelect "Sun requirements" "sun" model.Query.Sun (data.Sun |> List.map(fun f->f.Name)) dispatch
                filterSelect "Water requirements" "water" model.Query.Water (data.Water |> List.map(fun f->f.Name)) dispatch
                filterSelect "Garden features" "feature" model.Query.Feature (data.GardenFeatures |> List.map(fun f->f.Name)) dispatch
                filterSelect "Attracts wildlife" "wildlife" model.Query.Wildlife (data.Wildlife |> List.map(fun f->f.Name)) dispatch
                filterSelect "Photographer" "photographer" model.Query.Photographer (data.Photographers |> List.map(fun f->f.Name)) dispatch
                checkFilter "Recorded as NT endemic" "endemic" model.Query.Endemic dispatch
                checkFilter "With a photograph" "photos" model.Query.Photos dispatch
                Html.p [prop.className "filter-note";prop.text "Filters use recorded source attributes. An unrecorded trait does not mean the plant lacks it."]
            ]]
            Html.div [prop.className "results";prop.children [
                Html.div [prop.className "results-heading";prop.children [Html.p [prop.role "status";prop.text(sprintf "%i %s%s" results.Length (if results.Length=1 then "plant" else "plants") (model.Query.Q |> Option.map(fun q->" matching “"+q+"”") |> Option.defaultValue ""))];Html.span(if model.Query.Q.IsSome then "Best name matches first" else "Scientific name A–Z")]]
                if results.IsEmpty then Html.div [prop.className "empty-state";prop.children [leaf;Html.h2 "No plants match these choices";Html.p "Try a broader name or clear a filter.";button "Clear filters" ClearFilters dispatch]]
                else Html.div [prop.className "plant-grid";prop.children(results |> List.truncate model.Limit |> List.map(fun p->card p dispatch))]
                if results.Length>model.Limit then Html.div [prop.className "more";prop.children [button (sprintf "Show more plants · %i remaining" (results.Length-model.Limit)) More dispatch]]
            ]]
        ]]
    ]]

let taxonomy model (data:GetCatalogue.Response) dispatch =
    let families=data.Families |> List.filter(fun f->normalize f.Name |> fun name -> name.Contains(normalize model.Search) || f.Genera |> List.exists(fun g->(normalize g).Contains(normalize model.Search)))
    Html.main [prop.className "taxonomy-page section";prop.children [
        Html.div [prop.className "page-heading";prop.children [Html.span [prop.className "eyebrow";prop.text "Family → genus → plant"];Html.h1 "Explore the family tree";Html.p "Follow the relationships between the plants in this collection."]]
        Html.input [prop.className "taxonomy-search";prop.placeholder "Find a family or genus…";prop.ariaLabel "Filter families and genera";prop.value model.Search;prop.onChange(fun s->dispatch(SearchChanged s))]
        Html.p [prop.className "taxonomy-count";prop.text(sprintf "%i families · %i genera in the collection" data.Families.Length (data.Plants |> List.map(fun p->p.Genus) |> List.distinct |> List.length))]
        Html.div [prop.className "family-grid";prop.children [
            for f in families do Html.article [prop.className "family-card";prop.children [
                Html.div [prop.className "family-heading";prop.children [a (queryUrl {emptyQuery with Family=Some f.Name}) f.Name dispatch;Html.span(sprintf "%i %s" f.Count (if f.Count=1 then "plant" else "plants"))]]
                Html.div [prop.className "genera";prop.children [for g in f.Genera do a (queryUrl {emptyQuery with Family=Some f.Name;Genus=Some g}) g dispatch]]
            ]]
        ]]
    ]]

let photoImage model hero (photo:Photo) dispatch =
    let image = Html.img [
        prop.src(if hero then photo.Image else photo.Thumbnail)
        prop.alt photo.Caption
        if not hero then prop.custom("loading","lazy")
    ]
    if canViewPhoto model photo then
        Html.button [prop.className "image-button";prop.ariaLabel(if hero then "Enlarge plant photograph" else "Enlarge: "+photo.Caption)
                     prop.onClick(fun _->dispatch(Zoom(Some photo)));prop.children [image;Html.span "View photograph ↗"]]
    else Html.div [prop.className "image-button photograph-static";prop.children [image]]

let detail model (plant:PlantDetail) dispatch =
    let signedIn=Client.Auth.signedIn model.Auth
    let personal=if signedIn && model.Personal.PlantId=plant.Card.Id then model.Personal else Client.Personal.empty model.Personal.Epoch
    let plant=Client.Personal.compose personal.Data plant
    let p=plant.Card
    let glossary=model.Data |> Option.map(fun d->d.Glossary) |> Option.defaultValue [] |> NativePlants.Glossary.glossaryMatcher
    let references=model.Data |> Option.map(fun d->d.References) |> Option.defaultValue []
    let referenceNames=NativePlants.Glossary.referenceMatcher references
    let rich=Client.Annotations.text glossary
    let prose label value =
        if label="Aboriginal uses · source account" then Client.Annotations.usage glossary references value
        elif label="References" then
            NativePlants.Glossary.annotate referenceNames value
            |> List.collect(fun part -> if part.Note.IsSome then [part] else NativePlants.Glossary.annotate glossary part.Text)
            |> Client.Annotations.chunks
        else rich value
    let supportingPhotos = plant.Photos |> List.filter(fun photo -> p.Photo |> Option.forall(fun hero -> hero.Id<>photo.Id))
    let compactGallery = (not supportingPhotos.IsEmpty || signedIn) && supportingPhotos.Length<=2
    let gallery (className:string) =
        Html.section [prop.className className;prop.ariaLabel "More photographs";prop.children [
            for photo in supportingPhotos do
                Html.figure [prop.children [
                    photoImage model false photo dispatch
                    Html.figcaption [prop.children [Html.span [prop.children [rich photo.Caption]];if hasPhotoCredit photo.Photographer then Html.span("Photo: "+photo.Photographer)]]
                    Client.Personal.controls personal photo (Personal >> dispatch)
                ]]
            if signedIn then Client.Personal.uploadTile personal (Personal >> dispatch)
        ]]
    let sections =
        if plant.DistributionMaps.IsEmpty || (plant.Sections |> List.exists(fun s -> s.Label="Distribution")) then plant.Sections
        else plant.Sections @ [{Label="Distribution";Text=""}]
    let attributes=["Growth form",String.concat ", " p.Forms;"Height",p.Height;"Sun",String.concat ", " p.Sun;"Water",String.concat ", " p.Water]
    Html.main [prop.className "detail-page section";prop.children [
        Html.nav [prop.className "breadcrumbs";prop.ariaLabel "Breadcrumb";prop.children [a "/plants" "Plants" dispatch;Html.span "/";a (queryUrl {emptyQuery with Family=Some p.Family}) p.Family dispatch;Html.span "/";a (queryUrl {emptyQuery with Genus=Some p.Genus}) p.Genus dispatch]]
        Html.div [prop.className(if compactGallery then "detail-intro detail-intro--compact" else "detail-intro");prop.children [
            Html.div [prop.className "detail-title";prop.children [
                yield Html.span [prop.className "eyebrow";prop.text p.Family]
                yield Html.h1 [prop.children [rich p.ScientificName]]
                if p.CommonNames<>"" then yield Html.p [prop.className "detail-common";prop.children [rich(p.CommonNames.Replace(" | ",", "))]]
                if not p.Aliases.IsEmpty then yield Html.p [prop.className "aliases";prop.children [rich("Previously recorded as "+String.concat ", " p.Aliases)]]
                if p.EndemicNt then yield Html.span [prop.className "pill";prop.children [rich "Recorded as endemic to the Northern Territory"]]
                yield Html.p [prop.className "detail-summary";prop.children [rich p.Summary]]
                yield Html.dl [prop.className "plant-facts";prop.children [for label,value in attributes do if value<>"" then Html.div [prop.children [Html.dt [prop.children [rich label]];Html.dd [prop.children [rich value]]]]]]
            ]]
            Html.figure [prop.className "detail-figure";prop.children [
                match p.Photo with
                | Some photo ->
                    photoImage model true photo dispatch
                    Html.figcaption [prop.children [Html.span [prop.children [rich photo.Caption]];if hasPhotoCredit photo.Photographer then Html.span("Photo: "+photo.Photographer)]]
                    Client.Personal.controls personal photo (Personal >> dispatch)
                | None -> Html.div [prop.className "no-photo detail-no-photo";prop.children [leaf;Html.p "A photograph is still to be selected for this account."]]
            ]]
            if compactGallery then gallery "photo-gallery photo-gallery--compact"
        ]]
        if not compactGallery && (not supportingPhotos.IsEmpty || signedIn) then gallery "photo-gallery"
        if signedIn then
            Client.Personal.galleryFeedback personal
            Client.Personal.photoEditor personal (Personal >> dispatch)
            Client.Personal.view personal (fun ()->dispatch(Auth Client.Auth.Toggle)) (Personal >> dispatch)
        Html.div [prop.className "account-layout";prop.children [
            Html.aside [prop.className "account-nav";prop.children [Html.span [prop.className "eyebrow";prop.text "In this account"];for s in sections do if s.Label<>"Recognise this plant" then Html.a [prop.href("#"+sectionId s.Label);prop.text s.Label]]]
            Html.div [prop.className "account-sections";prop.children [
                for s in sections do
                    if s.Label<>"Recognise this plant" then
                        Html.section [prop.id(sectionId s.Label);prop.children [
                            Html.h2 [prop.children [rich s.Label]]
                            if s.Text<>"" then Html.p [prop.children [prose s.Label s.Text]]
                            if s.Label="Distribution" then
                                for map in plant.DistributionMaps do
                                    Html.figure [prop.className "distribution-map";prop.children [
                                        Html.div [prop.className "map-image";prop.children [
                                            Html.img [prop.src map.Image;prop.alt map.Caption;prop.custom("loading","lazy")]
                                        ]]
                                        Html.figcaption [prop.children [Html.span [prop.children [rich map.Caption]];Html.span map.SourceLabel]]
                                    ]]
                        ]]
            ]]
        ]]
        if not p.GardenFeatures.IsEmpty || not p.Wildlife.IsEmpty then Html.section [prop.className "plant-associations";prop.children [Html.h2 "In the garden & landscape";Html.div [prop.className "pills";prop.children [for v in p.GardenFeatures @ p.Wildlife do Html.span [prop.className "pill";prop.children [rich v]]]]]]
    ]]

[<ReactComponent>]
let PlantAccount (model:Model) (plant:PlantDetail) dispatch =
    // Direct links arrive before the asynchronously loaded account exists in the DOM.
    React.useEffect((fun () -> restoreFragment()),[|box plant.Card.Id|])
    detail model plant dispatch

let glossaryPage model (data:GetCatalogue.Response) dispatch =
    let matcher=NativePlants.Glossary.glossaryMatcher data.Glossary
    let query=normalize model.Search
    let entries=data.Glossary |> List.filter(fun g -> normalize(String.concat " " (g.Term::g.Definition::g.Aliases)) |> fun text->text.Contains query)
    Html.main [prop.className "reference-page section";prop.children [
        Html.div [prop.className "page-heading";prop.children [Html.span [prop.className "eyebrow";prop.text "John Brock’s guide"];Html.h1 "Glossary";Html.p "Botanical terms from the source glossary. Definitions are also available wherever these terms occur in plant account text."]]
        Html.input [prop.type' "search";prop.className "reference-search";prop.ariaLabel "Search the glossary";prop.placeholder "Find a term or definition…";prop.value model.Search;prop.onChange(fun s->dispatch(SearchChanged s))]
        Html.p [prop.role "status";prop.text(sprintf "%i glossary %s" entries.Length (if entries.Length=1 then "term" else "terms"))]
        Html.dl [prop.className "glossary-list";prop.children [
          for entry in entries do
            Html.div [prop.id entry.Id;prop.children [
                Html.dt entry.Term
                Html.dd [prop.children [
                    Client.Annotations.text matcher entry.Definition
                    if entry.Illustration<>"" then Html.img [prop.src entry.Illustration;prop.alt(entry.Term+" illustration");prop.custom("loading","lazy")]
                    Html.small entry.SourceLabel
                ]]
            ]]
        ]]
    ]]

let referencesPage model (data:GetCatalogue.Response) dispatch =
    let query=normalize model.Search
    let numberSearch=System.Text.RegularExpressions.Regex.IsMatch(query,"^[0-9]{1,2}$")
    let entries=data.References |> List.filter(fun r ->
        if numberSearch then r.Kind="usage" && r.Number=int query
        else (normalize r.Citation).Contains query)
    Html.main [prop.className "reference-page section";prop.children [
        Html.div [prop.className "page-heading";prop.children [Html.span [prop.className "eyebrow";prop.text "John Brock’s source collection"];Html.h1 "References & bibliography";Html.p "The numbered references support Aboriginal plant-use citations. The bibliography preserves the source entries; additional references are retained on individual plant accounts."]]
        Html.input [prop.type' "search";prop.className "reference-search";prop.ariaLabel "Search references";prop.placeholder "Find an author, title or reference number…";prop.value model.Search;prop.onChange(fun s->dispatch(SearchChanged s))]
        Html.p [prop.role "status";prop.text(sprintf "%i %s" entries.Length (if entries.Length=1 then "entry" else "entries"))]
        for kind,heading in ["usage","Aboriginal plant-use references";"bibliography","Bibliography"] do
            Html.section [prop.children [
                Html.h2 heading
                if kind="usage" && not(data.References |> List.exists(fun r->r.Kind="usage" && r.Number=22)) then Html.p "The supplied list omits reference 22. Citations retain their original numbering."
                Html.ol [prop.className("reference-list"+(if kind="bibliography" then " bibliography-list" else ""));prop.children [
                  for entry in entries |> List.filter(fun r->r.Kind=kind) do
                    Html.li [
                        prop.id entry.Id
                        if entry.Number>0 then prop.value entry.Number
                        prop.children [Html.p entry.Citation]
                    ]
                ]]
            ]]
    ]]

let about dispatch =
    Html.main [prop.className "about-page section";prop.children [
        Html.span [prop.className "eyebrow";prop.text "About this collection"]
        Html.h1 "A closer connection to the plants of the north."
        Html.p [prop.className "standfirst";prop.text "This guide brings together the plant accounts and selected photographs from John Brock’s Native Plants of Northern Australia source collection."]
        Html.h2 "A collection with a sense of place"
        Html.p "The accounts have particular depth in the Top End: its sandstone country, woodlands, wetlands, monsoon forests and coast. They describe the plants covered by the source material, rather than every species occurring across northern Australia."
        Html.h2 "Names, descriptions and photographs"
        Html.p "Scientific names and descriptions follow the source collection. Former names are searchable where the account explicitly records them. Photo credits are shown where the photographer is known."
        Html.p [prop.children [a "/glossary" "Explore the glossary" dispatch;Html.text " · ";a "/references" "Read the references and bibliography" dispatch]]
        Html.h2 "Finding a plant"
        Html.p "Search a scientific or common name, follow a family to its genera, or combine the recorded growing requirements and characteristics. Filters reflect what the source records: missing information is not evidence that a plant lacks a trait."
        Html.h2 "Reading the accounts"
        Html.p "Traditional uses are retained as attributed source material, alongside the account’s notes and qualifications. Historical uses are not instructions for consuming or preparing a plant."
        a "/plants" "Explore the collection →" dispatch
    ]]

let view model dispatch =
    Html.div [prop.className "site";prop.children [
        Html.a [prop.className "skip-link";prop.href "#main";prop.text "Skip to content"]
        header model dispatch
        Html.div [prop.id "main";prop.children [
            match model.Page,model.Data with
            | ReviewPage,_ when Client.Access.canReview model.Access ->
                Client.Review.view model.Access.Data.Value (model.Data |> Option.map(fun d->d.Plants) |> Option.defaultValue []) model.Review (Review >> dispatch)
            | ReviewPage,_ ->
                Html.main [prop.className "empty-state section";prop.children [
                    if model.Access.Loading then
                        Html.h1 [prop.role "status";prop.text "Checking review access…"]
                    elif model.Access.Failed then
                        Html.h1 "Review access could not be checked"
                        Html.p "Please try again."
                        button "Retry access check" (Access Client.Access.Refresh) dispatch
                    else
                        Html.h1 "Review access required"
                        Html.p "Sign in with a curator or identifier account using Login above, or ask the site owner for access."
                    a "/" "Back to the guide" dispatch
                ]]
            | About,_ -> about dispatch
            | GlossaryPage,Some data -> glossaryPage model data dispatch
            | ReferencesPage,Some data -> referencesPage model data dispatch
            | Detail _,_ ->
                match model.Plant,model.DetailError with
                | Some p,_ -> PlantAccount model p dispatch
                | None,Some error -> Html.main [prop.className "empty-state section";prop.children [Html.h1 error;a "/plants" "Back to the collection" dispatch]]
                | _ -> Html.main [prop.className "loading section";prop.role "status";prop.text "Opening the plant account…"]
            | _,Some data ->
                match model.Page with Home->home model data dispatch | Browse->browse model data dispatch | Taxonomy->taxonomy model data dispatch | _->Html.none
            | _,None -> Html.main [prop.className "loading section";prop.children [leaf;Html.h1 "Opening the collection";Html.p "Plants, places and the details that connect them.";if model.Error.IsSome then Html.div [prop.children [Html.p model.Error.Value;button "Try again" Refresh dispatch]]]]
        ]]
        footer model dispatch
        match model.Zoom with
        | Some photo ->
            let credit=if hasPhotoCredit photo.Photographer then " · "+photo.Photographer else ""
            Html.div [prop.className "lightbox lightbox-photograph";prop.role "dialog";prop.custom("aria-modal",true);prop.ariaLabel photo.Caption;prop.onClick(fun _->dispatch(Zoom None));prop.children [
                Html.button [prop.className "lightbox-close";prop.autoFocus true;prop.ariaLabel "Close photograph";prop.text "×";prop.onClick(fun _->dispatch(Zoom None))]
                Html.figure [prop.onClick(fun e->e.stopPropagation());prop.children [Html.img [prop.src photo.Image;prop.alt photo.Caption];Html.figcaption [prop.text(photo.Caption+credit)]]]
            ]]
        | None -> Html.none
    ]]
