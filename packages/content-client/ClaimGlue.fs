namespace Content

// The OAuth claim-return glue, shared by every identity host (previously triplicated byte-for-byte
// in blog/Host.fs, articles/Host.fs and the shell App.fs). After a login the server redirects to
// /auth/claim?identity=…&returnTo=…; the host detects that route, boots the identity subsystem with
// the claimed id as the switcher focus, and SPA-navigates back to returnTo. Only the pure, host-
// independent pieces live here — parsing + the route pattern + a generic return-nav command; each
// host keeps its own Model/Msg wiring and supplies its own base-aware navigator (returnTo may carry
// the deployment prefix, so a host's navigateToPath must be idempotent w.r.t. the base).

module ClaimGlue =
    open Fable.Core
    open Elmish

    /// Read a query param from window.location.search (null when absent).
    [<Emit("new URLSearchParams(window.location.search).get($0)")>]
    let getQueryParam (name: string) : string = jsNative

    /// OAuth return: /auth/claim?identity=…&returnTo=… — the just-claimed identity id (the focus the
    /// switcher opens on, None if absent) and where to send the browser next (defaulting to "/").
    let parseClaimFromRoute () : string option * string =
        let identity = getQueryParam "identity"
        let returnTo = getQueryParam "returnTo"
        let identity = if isNull identity || identity = "" then None else Some identity
        let returnTo = if isNull returnTo || returnTo = "" then "/" else returnTo
        identity, returnTo

    /// The OAuth claim route (with or without a trailing identity segment), matched on base-stripped
    /// segments (the host runs routeOf first).
    let (|ClaimRoute|_|) route =
        match route with
        | [ "auth"; "claim" ] | [ "auth"; "claim"; _ ] -> Some ()
        | _ -> None

    /// Effect command that SPA-navigates back to the claim's returnTo through the host's own
    /// base-aware navigator. Generic over the host Msg (it dispatches nothing).
    let returnNavCmd (navigateToPath: string -> unit) (returnTo: string) : Cmd<'msg> =
        Cmd.ofEffect (fun _ -> navigateToPath returnTo)
