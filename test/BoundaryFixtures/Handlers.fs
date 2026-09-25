module Server.Handlers
open Fable.Core
open Fable.Core.JsInterop
open Hedge.Workers
open Hedge.Router
open Hedge.Codec
open Thoth.Json
open Server.Env
let inline private respond value = okJson (encode value |> Encode.toString 0)
let private context request env (ctx:ExecutionContext) =
    getHeader request "Cookie" + ":" + env.Marker + ":" + unbox<string>(ctx?marker)
let readPublic (env:Env) = promise { return respond ({Value=env.Marker}:Probe.Api.ReadPublic.Response) }
let privateRequest request env ctx = promise {
    return respond ({Value=context request env ctx;Identity={Provider="google";Roles=["curator"]}}:Probe.Api.ReadPrivate.Response)
}
let readPrivate request env ctx = privateRequest request env ctx
let by id request env ctx = promise { return respond ({Value=id+":"+context request env ctx}:Probe.Api.By.Response) }
let query (q:Probe.Api.Query.Query) request env ctx = promise { return respond ({Value=defaultArg q.Q ""+":"+context request env ctx}:Probe.Api.Query.Response) }
let both id (q:Probe.Api.Both.Query) request env ctx = promise { return respond ({Value=id+":"+defaultArg q.Q ""+":"+context request env ctx}:Probe.Api.Both.Response) }
