module Client.Access

open Fable.Core
open Fable.Core.JsInterop
open Elmish
open Models.Contributions

let storedKey () = Client.AdminCredential.read()
let readCapabilities key = Client.ContributionsApi.access key |> Client.ContributionsApi.forView
let listen refresh = Client.AdminCredential.subscribe refresh

type Model = {Epoch:int;Key:string;Data:Capabilities option;Loading:bool;Failed:bool}
type Msg = Refresh | Loaded of int * Result<Capabilities,string>
let empty epoch = {Epoch=epoch;Key="";Data=None;Loading=false;Failed=false}
let clear model = empty (model.Epoch+1)
let canEdit model = model.Data |> Option.exists(fun a->a.CanEditCatalogue)
let canReview model = model.Data |> Option.exists(fun a->a.CanReview || a.CanIdentify)
let update msg model =
    match msg with
    | Refresh ->
        let key=storedKey()
        if model.Loading && key=model.Key then model,Cmd.none
        else
            let next={model with Epoch=model.Epoch+1;Key=key;Loading=true;Failed=false;Data=if key=model.Key then model.Data else None}
            next,Cmd.OfPromise.either readCapabilities key (fun result->Loaded(next.Epoch,Ok result)) (fun ex->Loaded(next.Epoch,Error ex.Message))
    | Loaded(epoch,_) when epoch=model.Epoch && model.Key<>storedKey() ->
        {model with Data=None;Loading=false},Cmd.ofMsg Refresh
    | Loaded(epoch,Ok result) when epoch=model.Epoch -> {model with Data=Some result;Loading=false;Failed=false},Cmd.none
    | Loaded(epoch,Error _) when epoch=model.Epoch -> {model with Data=None;Loading=false;Failed=true},Cmd.none
    | Loaded _ -> model,Cmd.none
