module BoundaryFixtures.Generate
open System.IO
open Gen.Program
[<EntryPoint>]
let main _ =
    let eps = discoverApiModules "Probe" "" typeof<Probe.Api.ReadPublic.Response>.Assembly "" "Server.Handlers"
    if eps.Length <> 5 || (eps |> List.filter (fun ep -> ep.RequestContext)).Length <> 4 then failwith "Request-context discovery"
    Directory.CreateDirectory "test/BoundaryFixtures/generated" |> ignore
    let write name (content:string) = File.WriteAllText("test/BoundaryFixtures/generated/"+name,content)
    write "Codecs.fs" (generateCodecsFs [] eps [] true "Probe" "Codecs" false)
    write "Routes.fs" (generateRoutesFs [] [] eps)
    write "RouteContract.fs" ((generateRouteContractFs "Probe" eps).Replace("open Probe.Codecs", "open Codecs"))
    write "ClientGen.fs" (generateClientGenFs eps [] true "Probe" "Probe.ClientGen" "Codecs")
    let stubs = generateHandlersFs eps
    if not(stubs.Contains("let readPrivate (request: WorkerRequest) (env: Env) (ctx: ExecutionContext)")) then failwith "Context handler stub"
    printfn "Generated request-aware standalone/module fixtures."
    0
