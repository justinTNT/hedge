module Hedge.Oidc

// A config-driven OpenID Connect provider. Instead of hand-coding a `ProviderConfig` per IdP, a host
// REGISTERS an OIDC provider by name with either a built-in preset, explicit endpoints, or just an
// `Issuer` (→ cached `.well-known/openid-configuration` discovery), plus client id/secret. This turns
// "add an OIDC IdP" (LinkedIn, Okta, Auth0, Keycloak, Entra tenants, a government IdP, …) into a config
// entry, no code. It reuses Hedge.OAuth's authorization-code → token → userinfo flow verbatim
// (`toProviderConfig` produces a plain `OAuth.ProviderConfig`); id_token/JWKS verification is a
// separate hardening (see notes) and is not required for the server-side code flow.

open Fable.Core
open Fable.Core.JsInterop
open Hedge.Workers
open Hedge.OAuth

/// A host's registration of one OIDC provider. Resolution precedence for endpoints: explicit URLs, then
/// a preset, then discovery from `Issuer`. `Scopes` defaults to the preset's, then "openid profile email".
type OidcRegistration = {
    Preset: string option
    Issuer: string option
    AuthorizeUrl: string option
    TokenUrl: string option
    UserinfoUrl: string option
    Scopes: string option
    ClientId: string
    ClientSecret: string
}

/// Null/undefined-safe string read from a userinfo JSON object (`== null` catches both).
[<Emit("($0 == null ? '' : String($0))")>]
let private strOr (v: obj) : string = jsNative

/// Standard OIDC `userinfo` claims → normalized `UserInfo`. `name` falls back to given+family; `email`
/// and `picture` are optional. The provider label is the registration name (so an arbitrary IdP is
/// attributed under the name the host registered it as).
let standardClaims (provider: string) (o: obj) : UserInfo =
    let name =
        let n = strOr o?name
        if n <> "" then n
        else (strOr o?given_name + " " + strOr o?family_name).Trim()
    let email = strOr o?email
    { Name = name
      PictureUrl = strOr o?picture
      Email = (if email = "" then None else Some email)
      ProviderUserId = strOr o?sub
      Provider = provider }

// A built-in preset: fixed endpoints + scopes + a parser. Presets exist so common IdPs are one-liners and
// so the two MIGRATED providers (google, microsoft) keep their EXACT prior endpoints/claims — google in
// particular has live users whose provider_user_id must not drift.
type private Preset =
    { Authorize: string; Token: string; Userinfo: string; Scopes: string; Parse: string -> obj -> UserInfo }

// google: preserve the pre-migration parser — provider_user_id stays the v2 userinfo `id` (which equals
// the OIDC `sub`, but reading `id` guarantees byte-identical ids for existing google users).
let private googleParse (provider: string) (o: obj) : UserInfo =
    { Name = strOr o?name; PictureUrl = strOr o?picture
      Email = (let e = strOr o?email in if e = "" then None else Some e)
      ProviderUserId = strOr o?id; Provider = provider }

// microsoft: Graph /me shape (displayName/mail/id). Personal accounts (MSA) and some Entra users have a
// null `mail`, with the address in `userPrincipalName` — fall back to it when it looks like an address.
let private microsoftParse (provider: string) (o: obj) : UserInfo =
    let email =
        let m = strOr o?mail
        if m <> "" then Some m
        else let upn = strOr o?userPrincipalName in (if upn.Contains "@" then Some upn else None)
    { Name = strOr o?displayName; PictureUrl = ""
      Email = email
      ProviderUserId = strOr o?id; Provider = provider }

let private presets : Map<string, Preset> =
    Map.ofList [
        "google",
          { Authorize = "https://accounts.google.com/o/oauth2/v2/auth"
            Token = "https://oauth2.googleapis.com/token"
            Userinfo = "https://www.googleapis.com/oauth2/v2/userinfo"
            Scopes = "openid profile email"; Parse = googleParse }
        "microsoft",
          { Authorize = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize"
            Token = "https://login.microsoftonline.com/common/oauth2/v2.0/token"
            Userinfo = "https://graph.microsoft.com/v1.0/me"
            Scopes = "openid profile email"; Parse = microsoftParse }
        "linkedin",
          { Authorize = "https://www.linkedin.com/oauth/v2/authorization"
            Token = "https://www.linkedin.com/oauth/v2/accessToken"
            Userinfo = "https://api.linkedin.com/v2/userinfo"
            Scopes = "openid profile email"; Parse = standardClaims }
    ]

type private Endpoints = {| Authorize: string; Token: string; Userinfo: string |}

// In-isolate discovery cache: a Worker isolate handles many requests, so caching the `.well-known`
// document per issuer avoids a lookup on every login. Empty on a cold isolate (safe — just re-fetches).
let mutable private discoveryCache : Map<string, Endpoints> = Map.empty

let private discover (issuer: string) : JS.Promise<Endpoints> =
    promise {
        match discoveryCache.TryFind issuer with
        | Some ep -> return ep
        | None ->
            let url = issuer.TrimEnd('/') + "/.well-known/openid-configuration"
            let! resp = fetchRaw url (createObj [ "headers" ==> createObj [ "Accept" ==> "application/json" ] ])
            if not resp.ok then
                let! text = responseText resp
                return failwith (sprintf "OIDC discovery failed for %s (%d): %s" issuer resp.status text)
            else
                let! d = responseJson resp
                // Discovered endpoints must be https (we send the client secret to the token endpoint).
                let httpsOnly label (u: string) =
                    if u.StartsWith "https://" then u
                    else failwith (sprintf "OIDC discovery for %s returned a non-https %s endpoint" issuer label)
                let ep : Endpoints =
                    {| Authorize = httpsOnly "authorization" (strOr d?authorization_endpoint)
                       Token = httpsOnly "token" (strOr d?token_endpoint)
                       Userinfo = httpsOnly "userinfo" (strOr d?userinfo_endpoint) |}
                discoveryCache <- discoveryCache.Add(issuer, ep)
                return ep
    }

/// Build an `OAuth.ProviderConfig` from a registration (discovering endpoints if needed). `name` is the
/// registration key, used as the attributed provider for the standard-claims parser.
let toProviderConfig (name: string) (reg: OidcRegistration) : JS.Promise<ProviderConfig> =
    promise {
        let preset = reg.Preset |> Option.bind presets.TryFind
        let! endpoints =
            match reg.AuthorizeUrl, reg.TokenUrl, reg.UserinfoUrl with
            | Some a, Some t, Some u -> promise { return ({| Authorize = a; Token = t; Userinfo = u |} : Endpoints) }
            | None, None, None ->
                match preset with
                | Some p -> promise { return ({| Authorize = p.Authorize; Token = p.Token; Userinfo = p.Userinfo |} : Endpoints) }
                | None ->
                    match reg.Issuer with
                    | Some iss -> discover iss
                    | None -> failwith (sprintf "OIDC provider '%s' needs a preset, explicit endpoints, or an issuer" name)
            // A partial explicit override is a config mistake — fail loudly rather than silently falling back
            // to the preset/discovery (the documented precedence is all-three-or-none).
            | _ -> failwith (sprintf "OIDC provider '%s': set all of AuthorizeUrl/TokenUrl/UserinfoUrl, or none" name)
        let scopes =
            reg.Scopes
            |> Option.orElse (preset |> Option.map (fun p -> p.Scopes))
            |> Option.defaultValue "openid profile email"
        let parse = (preset |> Option.map (fun p -> p.Parse) |> Option.defaultValue standardClaims) name
        return
            { AuthorizeUrl = endpoints.Authorize
              TokenUrl = endpoints.Token
              UserinfoUrl = endpoints.Userinfo
              Scopes = scopes
              ParseUserinfo = parse }
    }
