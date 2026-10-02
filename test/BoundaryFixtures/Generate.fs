module BoundaryFixtures.Generate
open System.IO
open Gen.Program
[<EntryPoint>]
let main _ =
    let asm = typeof<Probe.Api.ReadPublic.Response>.Assembly
    let eps = discoverApiModules "Probe" "" asm "" "Server.Handlers"
    // 7 endpoints (5 GET-family + Submit POST + Ping PostEmpty); 6 carry requestContext (all but ReadPublic).
    if eps.Length <> 7 || (eps |> List.filter (fun ep -> ep.RequestContext)).Length <> 6 then failwith "Request-context discovery"
    // Negative proof: an endpoint-typed value not named `endpoint` must make discovery throw the diagnostic.
    let badThrew = try discoverApiModules "ProbeBad" "" asm "" "Server.Handlers" |> ignore; false with _ -> true
    if not badThrew then failwith "endpoint-name diagnostic did not fire for a misnamed endpoint property"
    Directory.CreateDirectory "test/BoundaryFixtures/generated" |> ignore
    let write name (content:string) = File.WriteAllText("test/BoundaryFixtures/generated/"+name,content)
    write "Codecs.fs" (generateCodecsFs [] eps [] true "Probe" "Codecs" false)
    write "Routes.fs" (generateRoutesFs [] [] eps)
    write "RouteContract.fs" ((generateRouteContractFs "Probe" eps).Replace("open Probe.Codecs", "open Codecs"))
    write "ClientGen.fs" (generateClientGenFs eps [] true "Probe" "Probe.ClientGen" "Codecs")
    let stubs = generateHandlersFs eps
    if not(stubs.Contains("let readPrivate (request: WorkerRequest) (env: Env) (ctx: ExecutionContext)")) then failwith "Context handler stub"
    // PostEmpty stub is env-shaped (no request DTO arg), like a context-carrying GET.
    if not(stubs.Contains("let ping (request: WorkerRequest) (env: Env) (ctx: ExecutionContext)")) then failwith "PostEmpty handler stub shape"
    printfn "Generated request-aware standalone/module fixtures."
    0
