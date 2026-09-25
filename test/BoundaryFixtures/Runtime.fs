module BoundaryFixtures.Runtime
open Fable.Core
open Hedge.Workers
open Hedge.Router
open Hedge.Admin
open Hedge.Schema
let standalone request ctx = Server.Routes.dispatch request {Marker="fixture"} ctx |> Option.get
let modular request ctx =
    let env:Server.Env.Env = {Marker="fixture"}
    let handlers:Probe.RouteContract.Handlers = {
        readPublic = fun () -> Server.Handlers.readPublic env
        readPrivate = fun () request ctx -> Server.Handlers.privateRequest request env ctx
        by = fun id request ctx -> Server.Handlers.by id request env ctx
        query = fun q request ctx -> Server.Handlers.query q request env ctx
        both = fun id q request ctx -> Server.Handlers.both id q request env ctx
    }
    Probe.RouteContract.dispatch handlers request ctx |> Option.get
let private table = {
    Name="Protected";Table="protected";Schema=schema "Protected" [fieldWith "Id" FString [PrimaryKey]]
    SelectAll="SELECT id FROM protected";SelectOne="SELECT id FROM protected WHERE id=?"
    Insert="";HasCreateTs=false;HasUpdateTs=false;Update="";Delete=""
    MutableFields=[];SupportedOps=[OpList;OpRead]
}
let admin request db =
    let access =
        match getHeader request "X-Test-Access" with
        | "owner" -> AdminOwner
        | "reader" -> AdminSubject ((fun _ op -> op=OpList),Some "renewed-cookie")
        | _ -> AdminAnonymous None
    let config:AdminConfig<D1Database> = {
        Tables=[table;{table with Name="Hidden";SupportedOps=[]}];GetDb=id
        Authorize=fun _ _ -> promise {return access}
    }
    handleRequest config request db (parseRoute request) |> Option.get

let subscribeCredentials changed = Client.AdminCredential.subscribe changed
