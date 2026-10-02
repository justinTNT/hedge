module IdentityHttp.Composition

// Bind the identity module's HTTP handlers over the host's WriteDeps into the generated
// IdentityHttp.RouteContract.Handlers record — the one authored place that closes each typed, decoded
// endpoint over the shared Identity.Handlers cores. The host (Server.ModuleServices) builds the WriteDeps
// (app DB + guest-write authorizer + attribution policy) and hands bind's result to the generated dispatch,
// after its bounded-body preflight. Mirrors Blog.Composition.bind at the module boundary.
//
// The decoded cores (switchDecoded / disconnectDecoded / getIdentitiesDecoded) each re-run the guest-write
// authorizer themselves (fail-closed, no cookie fallback on a present-but-invalid bearer) and perform the
// ownership checks before mutation — so this binding carries no authorization of its own; it only adapts the
// generated decoded-argument shape to the cores. Request context (WorkerRequest) is threaded for the
// authorizer + renewal cookie; ExecutionContext is unused by these handlers.

open IdentityHttp.RouteContract

let bind (deps: Identity.Handlers.WriteDeps) : Handlers =
    { getIdentities = fun () request _ctx -> Identity.Handlers.getIdentitiesDecoded deps request
      disconnect =
        fun req request _ctx ->
            // Preserve the historical fallback: an absent or blank display name becomes "Anonymous".
            let fallbackName = match req.name with Some n when n <> "" -> n | _ -> "Anonymous"
            Identity.Handlers.disconnectDecoded deps request req.identityId fallbackName
      revert = fun req request _ctx -> Identity.Handlers.switchDecoded deps request req.identityId req.merge
      activate = fun req request _ctx -> Identity.Handlers.switchDecoded deps request req.identityId req.merge }
