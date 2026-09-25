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
