namespace MobileHttp

// Typed HTTP contracts for the native (Capacitor) bearer-session endpoints (/api/mobile/*), generated into
// a module surface (Codecs/ClientGen/RouteContract) like a content module — but with NO Domain records, so
// it contributes zero tables (the mobile_sessions / mobile_auth_codes tables come from the separate
// Mobile.Domain storage slice). Lives in the existing MobileModels assembly under its own `MobileHttp`
// namespace. Lowercase field names are deliberate: they are the existing wire keys
// (token/guest/guestId/identity/code/verifier/ok), so the generated codecs reproduce the current JSON
// exactly. Route prefix /api/mobile comes from the manifest. Only bootstrap/me/exchange/signout are typed
// here; the browser-OAuth return (a redirect) and the multipart blob upload stay hand-wired adapters.

open Hedge.Interface

module Api =

    module Bootstrap =
        // Parameterless POST: the client sends {} or no body; dispatch reads no body. Mints an anonymous
        // bearer session and returns it once.
        type Response = { token: string }
        let endpoint : PostEmpty<Response> = PostEmpty "/api/bootstrap"
        let requestContext = true

    module Me =
        /// Public identity projection (no guest ownership, provider account id, or internal lifecycle fields).
        type PublicIdentity =
            { id: string
              provider: string
              name: string
              picture: string
              email: string option }
        type GuestAccount = { guestId: string; identity: PublicIdentity }
        // guest = None renders {"guest":null} (a valid anonymous session). A missing/invalid/expired/revoked
        // bearer is 401 (the handler controls status), kept distinct from the anonymous 200.
        type Response = { guest: GuestAccount option }
        let endpoint : Get<Response> = Get "/api/me"
        let requestContext = true

    module Exchange =
        type Request = { code: string; verifier: string }
        type Response = { token: string }
        let endpoint : Post<Request, Response> = Post "/api/exchange"
        let requestContext = true

    module Signout =
        // Parameterless POST: the client sends no body; dispatch reads none. Revokes the presented bearer,
        // idempotent for repeated/missing/already-revoked tokens.
        type Response = { ok: bool }
        let endpoint : PostEmpty<Response> = PostEmpty "/api/signout"
        let requestContext = true
