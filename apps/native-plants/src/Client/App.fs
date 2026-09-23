module Client.App

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Elmish
open Models.Api
open NativePlants.Catalogue

type Page = Home | Browse | Taxonomy | Detail of string | About
type Model = {
    Page: Page; Query: SearchPlants.Query; Data: GetCatalogue.Response option; Plant: PlantDetail option
    Error: string option; DetailError: string option; Search: string; Suggest: bool; Active: int
    Limit: int; FiltersOpen: bool; Zoom: Photo option
    Request: int; DetailRequest: int; Polling: bool; Loading: bool
}
type Msg =
    | Loaded of int * Result<GetCatalogue.Response,Hedge.Http.ApiError>
    | PlantLoaded of int * string * Result<GetPlant.Response,Hedge.Http.ApiError>
    | Revised of Result<GetRevision.Response,Hedge.Http.ApiError>
    | Navigate of string | LocationChanged | Refresh
    | SearchChanged of string | SearchFocus | SearchKey of string | SubmitSearch
    | FilterChanged of string * string | ClearFilters | More | ToggleFilters | Zoom of Photo option

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
[<Emit("window.addEventListener('popstate',()=> $0());setInterval(()=>{if(!document.hidden)$1()},15000);document.addEventListener('visibilitychange',()=>{if(!document.hidden)$1()});document.addEventListener('keydown',e=>{if(e.key==='Escape')$2()})")>]
let listen (location:unit->unit) (refresh:unit->unit) (escape:unit->unit) : unit = jsNative

let opt (s:string) = if isNull s || s.Trim()="" then None else Some s
let readQuery () : SearchPlants.Query =
    { Q=opt(param "q");Family=opt(param "family");Genus=opt(param "genus");Form=opt(param "form")
      Sun=opt(param "sun");Water=opt(param "water");Feature=opt(param "feature");Wildlife=opt(param "wildlife")
      Endemic=opt(param "endemic");Photos=opt(param "photos") }
let route () =
    let bits=(path()).Trim('/').Split('/')
    if bits.[0]="plants" && bits.Length>1 then Detail bits.[1]
    elif bits.[0]="plants" then Browse
    elif bits.[0]="taxonomy" then Taxonomy
    elif bits.[0]="about" then About else Home
let queryUrl (q: SearchPlants.Query) =
    let ps=["q",q.Q;"family",q.Family;"genus",q.Genus;"form",q.Form;"sun",q.Sun;"water",q.Water;"feature",q.Feature;"wildlife",q.Wildlife;"endemic",q.Endemic;"photos",q.Photos]
    let qs=ps |> List.choose (fun (k,v) -> v |> Option.map (fun x -> k+"="+enc x)) |> String.concat "&"
    "/plants"+(if qs="" then "" else "?"+qs)
let plantUrl (p:PlantCard) = "/plants/"+p.Id+"/"+p.Slug
let load request = Cmd.OfPromise.either api.getCatalogue () (fun r -> Loaded(request,r)) (fun e -> Loaded(request,Error(Hedge.Http.TransportFailure e.Message)))
let loadPlant request id = Cmd.OfPromise.either api.getPlant id (fun r -> PlantLoaded(request,id,r)) (fun e -> PlantLoaded(request,id,Error(Hedge.Http.TransportFailure e.Message)))
let currentPlant request = function Detail id -> loadPlant request id | _ -> Cmd.none
let revision = Cmd.OfPromise.either api.getRevision () Revised (fun e -> Revised(Error(Hedge.Http.TransportFailure e.Message)))
let init () =
    let q=readQuery()
    let p=route()
    {Page=p;Query=q;Data=None;Plant=None;Error=None;DetailError=None;Search=Option.defaultValue "" q.Q;Suggest=false;Active= -1;Limit=36;FiltersOpen=false;Zoom=None;Request=1;DetailRequest=1;Polling=false;Loading=true},
    Cmd.batch [load 1;currentPlant 1 p;Cmd.ofEffect(fun dispatch -> listen (fun ()->dispatch LocationChanged) (fun ()->dispatch Refresh) (fun ()->dispatch(Zoom None)))]
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
    | _ -> q
let changeRoute model =
    let p=route()
    let q=readQuery()
    title "Native Plants of Northern Australia"
    let request=model.DetailRequest+1
    {model with Page=p;Query=q;Search=Option.defaultValue "" q.Q;Plant=None;DetailError=None;Suggest=false;Active= -1;Limit=36;Zoom=None;DetailRequest=request},currentPlant request p
let update msg model =
    match msg with
    | Loaded(request,Ok data) when request=model.Request -> {model with Data=Some data;Error=None;Loading=false},Cmd.none
    | Loaded(request,Error _) when request=model.Request -> {model with Data=None;Loading=false;Error=Some "We couldn’t load the plant collection. Please try again."},Cmd.none
    | Loaded _ -> model,Cmd.none
    | PlantLoaded(request,id,Ok data) when model.Page=Detail id && request=model.DetailRequest ->
        title (data.Plant.Card.ScientificName+" · Native Plants")
        {model with Plant=Some data.Plant;DetailError=None},Cmd.none
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
    | Zoom photo -> {model with Zoom=photo;Suggest=false},Cmd.none

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
        Html.span [prop.className "header-note";prop.text "A collection by John Brock"]
    ]]

let footer dispatch =
    Html.footer [prop.className "site-footer";prop.children [
        Html.div [prop.children [Html.strong "Native Plants of Northern Australia";Html.p "A guide to John Brock’s collection, with a focus on the Top End."]]
        Html.div [prop.className "footer-links";prop.children [a "/about" "Sources & acknowledgements" dispatch;Html.a [prop.href "/admin";prop.text "Edit catalogue"]]]
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
    let selectedCount=[model.Query.Family;model.Query.Genus;model.Query.Form;model.Query.Sun;model.Query.Water;model.Query.Feature;model.Query.Wildlife;model.Query.Endemic;model.Query.Photos] |> List.choose id |> List.length
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

let detail (plant:PlantDetail) dispatch =
    let p=plant.Card
    let attributes=["Growth form",String.concat ", " p.Forms;"Height",p.Height;"Sun",String.concat ", " p.Sun;"Water",String.concat ", " p.Water]
    Html.main [prop.className "detail-page section";prop.children [
        Html.nav [prop.className "breadcrumbs";prop.ariaLabel "Breadcrumb";prop.children [a "/plants" "Plants" dispatch;Html.span "/";a (queryUrl {emptyQuery with Family=Some p.Family}) p.Family dispatch;Html.span "/";a (queryUrl {emptyQuery with Genus=Some p.Genus}) p.Genus dispatch]]
        Html.div [prop.className "detail-intro";prop.children [
            Html.div [prop.className "detail-title";prop.children [
                yield Html.span [prop.className "eyebrow";prop.text p.Family]
                yield Html.h1 p.ScientificName
                if p.CommonNames<>"" then yield Html.p [prop.className "detail-common";prop.text(p.CommonNames.Replace(" | ",", "))]
                if not p.Aliases.IsEmpty then yield Html.p [prop.className "aliases";prop.text("Previously recorded as "+String.concat ", " p.Aliases)]
                if p.EndemicNt then yield Html.span [prop.className "pill";prop.text "Recorded as endemic to the Northern Territory"]
                yield Html.p [prop.className "detail-summary";prop.text p.Summary]
                yield Html.dl [prop.className "plant-facts";prop.children [for label,value in attributes do if value<>"" then Html.div [prop.children [Html.dt label;Html.dd value]]]]
            ]]
            Html.figure [prop.className "detail-figure";prop.children [
                match p.Photo with
                | Some photo ->
                    Html.button [prop.className "image-button";prop.ariaLabel "Enlarge plant photograph";prop.onClick(fun _->dispatch(Zoom(Some photo)));prop.children [Html.img [prop.src photo.Image;prop.alt photo.Caption];Html.span "View photograph ↗"]]
                    Html.figcaption [prop.children [Html.span photo.Caption;Html.span("Photo: "+photo.Photographer)]]
                | None -> Html.div [prop.className "no-photo detail-no-photo";prop.children [leaf;Html.p "A photograph is still to be selected for this account."]]
            ]]
        ]]
        if plant.Photos.Length>1 then Html.section [prop.className "photo-gallery";prop.ariaLabel "More photographs";prop.children [
            for photo in plant.Photos |> List.skip 1 do
                Html.figure [prop.children [
                    Html.button [prop.className "image-button";prop.ariaLabel("Enlarge: "+photo.Caption);prop.onClick(fun _->dispatch(Zoom(Some photo)));prop.children [Html.img [prop.src photo.Thumbnail;prop.alt photo.Caption;prop.custom("loading","lazy")];Html.span "View photograph ↗"]]
                    Html.figcaption [prop.children [Html.span photo.Caption;Html.span("Photo: "+photo.Photographer)]]
                ]]
        ]]
        Html.div [prop.className "account-layout";prop.children [
            Html.aside [prop.className "account-nav";prop.children [Html.span [prop.className "eyebrow";prop.text "In this account"];for s in plant.Sections do if s.Label<>"Recognise this plant" then Html.a [prop.href("#section-"+enc s.Label);prop.text s.Label]]]
            Html.div [prop.className "account-sections";prop.children [for s in plant.Sections do if s.Label<>"Recognise this plant" then Html.section [prop.id("section-"+enc s.Label);prop.children [Html.h2 s.Label;Html.p s.Text]]]]
        ]]
        if not p.GardenFeatures.IsEmpty || not p.Wildlife.IsEmpty then Html.section [prop.className "plant-associations";prop.children [Html.h2 "In the garden & landscape";Html.div [prop.className "pills";prop.children [for v in p.GardenFeatures @ p.Wildlife do Html.span [prop.className "pill";prop.text v]]]]]
        Html.div [prop.className "source-line";prop.text "Plant account: John Brock, Native Plants of Northern Australia · 2026 source collection."]
    ]]

let about dispatch =
    Html.main [prop.className "about-page section";prop.children [
        Html.span [prop.className "eyebrow";prop.text "About this collection"]
        Html.h1 "A closer connection to the plants of the north."
        Html.p [prop.className "standfirst";prop.text "This guide brings together the plant accounts and selected photographs from John Brock’s Native Plants of Northern Australia source collection."]
        Html.h2 "A collection with a sense of place"
        Html.p "The accounts have particular depth in the Top End: its sandstone country, woodlands, wetlands, monsoon forests and coast. They describe the plants covered by the source material, rather than every species occurring across northern Australia."
        Html.h2 "Names, descriptions and photographs"
        Html.p "Scientific names and descriptions follow the source collection. Former names are searchable where the account explicitly records them. Photo credits are shown with each image; where a photographer has not yet been established, that is stated."
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
            | About,_ -> about dispatch
            | Detail _,_ ->
                match model.Plant,model.DetailError with
                | Some p,_ -> detail p dispatch
                | None,Some error -> Html.main [prop.className "empty-state section";prop.children [Html.h1 error;a "/plants" "Back to the collection" dispatch]]
                | _ -> Html.main [prop.className "loading section";prop.role "status";prop.text "Opening the plant account…"]
            | _,Some data ->
                match model.Page with Home->home model data dispatch | Browse->browse model data dispatch | Taxonomy->taxonomy model data dispatch | _->Html.none
            | _,None -> Html.main [prop.className "loading section";prop.children [leaf;Html.h1 "Opening the collection";Html.p "Plants, places and the details that connect them.";if model.Error.IsSome then Html.div [prop.children [Html.p model.Error.Value;button "Try again" Refresh dispatch]]]]
        ]]
        footer dispatch
        match model.Zoom with
        | Some photo -> Html.div [prop.className "lightbox";prop.role "dialog";prop.custom("aria-modal",true);prop.ariaLabel photo.Caption;prop.onClick(fun _->dispatch(Zoom None));prop.children [
            Html.button [prop.className "lightbox-close";prop.autoFocus true;prop.ariaLabel "Close photograph";prop.text "×";prop.onClick(fun _->dispatch(Zoom None))]
            Html.figure [prop.onClick(fun e->e.stopPropagation());prop.children [Html.img [prop.src photo.Image;prop.alt photo.Caption];Html.figcaption [prop.text(photo.Caption+" · "+photo.Photographer)]]]
          ]]
        | None -> Html.none
    ]]
