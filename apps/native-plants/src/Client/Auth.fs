module Client.Auth

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Elmish
open Client.GuestSession

type Model = {
    Account: IdentityData option
    Loading: bool
    LoggingOut: bool
    Request: int
    Open: bool
    Providers: string list option
    ProviderRequest: int
    Error: string option
}
type Msg =
    | Refresh
    | Loaded of int * SessionReadiness option
    | Toggle
    | Close
    | ProvidersLoaded of int * Result<string list,string>
    | Logout
    | LoggedOut of bool
    | SessionCleared

let load request = Cmd.OfPromise.either refreshSession () (fun r -> Loaded(request,Some r)) (fun _ -> Loaded(request,None))
let providers request =
    Cmd.OfPromise.either
        (fun () -> Client.Api.fetchJson "/api/auth/providers" (Thoth.Json.Decode.field "providers" (Thoth.Json.Decode.list Thoth.Json.Decode.string)))
        () (fun r -> ProvidersLoaded(request,r)) (fun _ -> ProvidersLoaded(request,Error "Login is temporarily unavailable. Please try again."))

let init () =
    { Account=None;Loading=true;LoggingOut=false;Request=1;Open=false;Providers=None;ProviderRequest=0;Error=None },load 1

/// No cached guest/display identity is an authenticated account. A fresh successful read is required.
let account (result: SessionReadiness option) =
    result |> Option.bind(fun r ->
        if r.Ready then r.Session.Identity |> Option.filter(fun i -> i.Provider<>"anonymous" && i.Provider<>"" && i.Id<>"")
        else None)

let signedIn model = model.Account.IsSome && not model.LoggingOut

let update msg model =
    match msg with
    | Refresh when model.Loading || model.LoggingOut || (model.Error |> Option.exists(fun e->e.StartsWith("Logout"))) -> model,Cmd.none
    | Refresh ->
        let request=model.Request+1
        {model with Request=request;Loading=true},load request
    | Loaded(request,result) when request=model.Request && not model.LoggingOut ->
        let current=account result
        let needsProviders=model.Open && current.IsNone && model.Account.IsSome
        let providerRequest=if needsProviders then model.ProviderRequest+1 else model.ProviderRequest
        {model with Account=current;Loading=false;ProviderRequest=providerRequest},
        if needsProviders then providers providerRequest else Cmd.none
    | Loaded _ -> model,Cmd.none
    | Toggle ->
        let opening=not model.Open
        let request=model.ProviderRequest+1
        {model with Open=opening;Providers=None;ProviderRequest=request;Error=None},
        if opening && model.Account.IsNone then providers request else Cmd.none
    | Close -> {model with Open=false},Cmd.none
    | ProvidersLoaded(request,result) when request=model.ProviderRequest ->
        match result with
        | Ok providers -> {model with Providers=Some providers},Cmd.none
        | Error error -> {model with Error=Some error;Providers=Some []},Cmd.none
    | ProvidersLoaded _ -> model,Cmd.none
    | Logout when model.LoggingOut -> model,Cmd.none
    | Logout ->
        {model with Account=None;Request=model.Request+1;Loading=false;LoggingOut=true;Error=None},
        Cmd.OfPromise.either signOut () LoggedOut (fun _ -> LoggedOut false)
    | LoggedOut ok ->
        {model with Account=None;Loading=false;LoggingOut=false;Open=not ok;
                    Error=if ok then None else Some "Logout did not complete. Please retry before leaving this browser."},Cmd.none
    | SessionCleared ->
        {model with Account=None;Request=model.Request+1;Loading=false;Open=false},Cmd.none

[<Emit("window.location.pathname + window.location.search + window.location.hash")>]
let private returnTo () : string = jsNative
[<Emit("encodeURIComponent($0)")>]
let private encode (value:string) : string = jsNative

let loginUrl provider = Client.Api.basePath+"/api/auth/"+encode provider+"/login?returnTo="+encode(returnTo())

[<Emit("window.addEventListener('hedge:session-cleared', $0); window.addEventListener('focus', $1)")>]
let listen (cleared:unit->unit) (refresh:unit->unit) : unit = jsNative

let view model dispatch =
    Html.div [prop.className "account-control";prop.children [
        Html.button [prop.type' "button";prop.className "account-toggle";prop.ariaExpanded model.Open;prop.ariaControls "account-panel"
                     prop.disabled model.LoggingOut
                     prop.text (if model.LoggingOut then "Logging out…" else model.Account |> Option.map(fun i->i.Name) |> Option.defaultValue "Login")
                     prop.onClick(fun _->dispatch Toggle)]
        if model.Open then
            Html.div [prop.id "account-panel";prop.className "account-panel";prop.children [
                Html.button [prop.type' "button";prop.className "account-close";prop.ariaLabel "Close account panel";prop.text "×";prop.onClick(fun _->dispatch Close)]
                match model.Account with
                | Some identity ->
                    Html.strong [prop.text identity.Name]
                    Html.p [prop.text ("Signed in with "+(if identity.Provider="google" then "Google" else "GitHub"))]
                    Html.button [prop.type' "button";prop.className "account-action";prop.text "Logout";prop.onClick(fun _->dispatch Logout)]
                | None ->
                    Html.strong "Login"
                    match model.Error with
                    | Some error ->
                        Html.p [prop.role "alert";prop.text error]
                        if error.StartsWith("Logout") then Html.button [prop.type' "button";prop.text "Retry logout";prop.onClick(fun _->dispatch Logout)]
                    | None ->
                        match model.Providers with
                        | None -> Html.p [prop.role "status";prop.text "Checking sign-in options…"]
                        | Some [] -> Html.p "Login is not available yet. You can continue exploring the guide."
                        | Some providers ->
                            for provider in providers do
                                Html.a [prop.className "account-action";prop.href(loginUrl provider);prop.text("Continue with "+(if provider="google" then "Google" else "GitHub"))]
            ]]
    ]]
