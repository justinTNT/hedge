module Server.AuthConfig

open Fable.Core
open Fable.Core.JsInterop
open Hedge.Workers
open Hedge.GuestSession
open Server.Env

[<Emit("new URL($0.url).hostname")>]
let private hostOf (request: WorkerRequest) : string = jsNative

let deps (env: Env) (request: WorkerRequest) : Deps =
    let keyId = if System.String.IsNullOrWhiteSpace env.GUEST_KEY_ID then "k1" else env.GUEST_KEY_ID
    { Config = configFor keyId env.GUEST_SECRET (hostOf request) (keyringFrom env.GUEST_KEYRING)
      Bridge = HardCutover
      Secure = env.ENVIRONMENT <> "development"
      Now = epochNow
      NewGuestId = newId
      LegacyEligible = fun _ -> promise { return false }
      LegacyHasLinkedIdentity = fun _ -> promise { return false } }

let isOwner (env:Env) request =
    let key=getHeader request "X-Admin-Key"
    key<>"" && key=env.ADMIN_KEY

/// Shared identity owns account adoption; the app moves its private contributions on verified login.
let completion : Identity.Handlers.OAuthDeps =
    { ReassignStatements = []; CommentTables = []; ActivateOnReturn = fun _ -> true }

let resolveIdentity db guestId = promise {
    let! subject = Identity.Server.activeSubject db guestId
    match subject with
    | None -> return None
    | Some _ -> return! Identity.Handlers.resolveIdentity db guestId
}

/// Verified subjects only. The app’s temporary anonymous contribution policy is separate.
let subject (env: Env) (request: WorkerRequest) = promise {
    let! guest = requireGuest (deps env request) (readCookie request)
    match guest with
    | Rejected -> return None, None
    | Accepted accepted ->
        let! identity = Identity.Server.activeSubject env.DB accepted.GuestId
        return identity, accepted.Replacement
}

let oauth (env: Env) : Hedge.Router.OAuthConfig =
    // Provider discovery stays usable without credentials; only complete configurations are offered.
    let configured clientId secret =
        not (System.String.IsNullOrWhiteSpace clientId || System.String.IsNullOrWhiteSpace secret)
    let ready = not (isNull env.OAUTH_SECRET) && env.OAUTH_SECRET.Length >= 32
                && not (isNull env.GUEST_SECRET) && env.GUEST_SECRET.Length >= 32
    { Secret = env.OAUTH_SECRET
      Providers =
        [ if ready && configured env.GOOGLE_CLIENT_ID env.GOOGLE_CLIENT_SECRET then
              "google", {| ClientId = env.GOOGLE_CLIENT_ID; ClientSecret = env.GOOGLE_CLIENT_SECRET |}
          if ready && configured env.GITHUB_CLIENT_ID env.GITHUB_CLIENT_SECRET then
              "github", {| ClientId = env.GITHUB_CLIENT_ID; ClientSecret = env.GITHUB_CLIENT_SECRET |} ]
        |> Map.ofList
      ResolveIdentity = resolveIdentity
      OnOAuthComplete = fun db blobs guestId userInfo returnTo -> promise {
          let! result = Identity.Handlers.onOAuthComplete completion db blobs guestId userInfo returnTo
          // userInfo comes only from the verified OAuth callback, never from a browser payload.
          do! Server.ContributionOwnership.claim db guestId (userInfo?Provider) (userInfo?ProviderUserId)
          return result
      } }
