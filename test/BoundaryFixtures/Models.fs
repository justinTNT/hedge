namespace Probe
open Hedge.Interface
module Api =
    type Identity = { Provider: string; Roles: string list }
    module ReadPublic =
        type Response = { Value: string }
        let endpoint: Get<Response> = Get "/public"
    module ReadPrivate =
        type Response = { Value: string; Identity: Identity }
        let endpoint: Get<Response> = Get "/private"
        let requestContext = true
    module By =
        type Response = { Value: string }
        let endpoint: GetBy<Response> = GetBy (sprintf "/by/%s")
        let requestContext = true
    module Query =
        type Query = { Q: string option }
        type Response = { Value: string }
        let endpoint: GetQuery<Query,Response> = GetQuery "/query"
        let requestContext = true
    module Both =
        type Query = { Q: string option }
        type Response = { Value: string }
        let endpoint: GetByQuery<Query,Response> = GetByQuery (sprintf "/both/%s")
        let requestContext = true
    module Submit =
        type Request = { Note: string }
        type Response = { Value: string }
        let endpoint: Post<Request,Response> = Post "/submit"
        let requestContext = true
    module Ping =
        // Parameterless POST: a nested Response and NO Request record.
        type Response = { Value: string }
        let endpoint: PostEmpty<Response> = PostEmpty "/ping"
        let requestContext = true

// A deliberately-malformed Api (endpoint value not named `endpoint`), under its OWN namespace so it does
// not pollute Probe.Api's counts. Generate.fs asserts discovery over "ProbeBad" THROWS the diagnostic.
namespace ProbeBad
open Hedge.Interface
module Api =
    module Oops =
        type Response = { Value: string }
        let notEndpoint: Get<Response> = Get "/oops"
