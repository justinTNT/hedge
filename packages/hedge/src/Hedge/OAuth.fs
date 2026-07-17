module Hedge.OAuth

open Fable.Core
open Fable.Core.JsInterop
open Hedge.Workers

/// Normalized user info from any OAuth provider.
type UserInfo = {
    Name: string
    PictureUrl: string
    Email: string option
    ProviderUserId: string
    Provider: string
}

/// Provider-specific OAuth configuration.
type ProviderConfig = {
    AuthorizeUrl: string
    TokenUrl: string
    UserinfoUrl: string
    Scopes: string
    ParseUserinfo: obj -> UserInfo
}

/// Build provider configs.
let private googleConfig : ProviderConfig = {
    AuthorizeUrl = "https://accounts.google.com/o/oauth2/v2/auth"
    TokenUrl = "https://oauth2.googleapis.com/token"
    UserinfoUrl = "https://www.googleapis.com/oauth2/v2/userinfo"
    Scopes = "openid profile email"
    ParseUserinfo = fun o ->
        { Name = o?name |> unbox<string>
          PictureUrl = o?picture |> unbox<string>
          Email = let e : string = o?email |> unbox in if isNull (box e) then None else Some e
          ProviderUserId = o?id |> unbox<string>
          Provider = "google" }
}

let private githubConfig : ProviderConfig = {
    AuthorizeUrl = "https://github.com/login/oauth/authorize"
    TokenUrl = "https://github.com/login/oauth/access_token"
    UserinfoUrl = "https://api.github.com/user"
    Scopes = "read:user user:email"
    ParseUserinfo = fun o ->
        { Name = o?name |> unbox<string> |> fun n -> if isNull (box n) then o?login |> unbox<string> else n
          PictureUrl = o?avatar_url |> unbox<string>
          Email = let e : string = o?email |> unbox in if isNull (box e) then None else Some e
          ProviderUserId = string (o?id |> unbox<int>)
          Provider = "github" }
}

let private microsoftConfig : ProviderConfig = {
    AuthorizeUrl = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize"
    TokenUrl = "https://login.microsoftonline.com/common/oauth2/v2.0/token"
    UserinfoUrl = "https://graph.microsoft.com/v1.0/me"
    Scopes = "openid profile email"
    ParseUserinfo = fun o ->
        { Name = o?displayName |> unbox<string>
          PictureUrl = ""
          Email = let e : string = o?mail |> unbox in if isNull (box e) then None else Some e
          ProviderUserId = o?id |> unbox<string>
          Provider = "microsoft" }
}

let private facebookConfig : ProviderConfig = {
    AuthorizeUrl = "https://www.facebook.com/v18.0/dialog/oauth"
    TokenUrl = "https://graph.facebook.com/v18.0/oauth/access_token"
    UserinfoUrl = "https://graph.facebook.com/me?fields=id,name,picture.type(large),email"
    Scopes = "public_profile email"
    ParseUserinfo = fun o ->
        { Name = o?name |> unbox<string>
          PictureUrl =
            let pic = o?picture
            if isNull pic then "" else
            let data = pic?data
            if isNull data then "" else data?url |> unbox<string>
          Email = let e : string = o?email |> unbox in if isNull (box e) then None else Some e
          ProviderUserId = o?id |> unbox<string>
          Provider = "facebook" }
}

/// All supported providers.
let providers : Map<string, ProviderConfig> =
    Map.ofList [
        "google", googleConfig
        "github", githubConfig
        "microsoft", microsoftConfig
        "facebook", facebookConfig
    ]

// ============================================================
// State token — HMAC-signed, zero-storage
// ============================================================

/// Generate a signed state token: base64url(timestamp|guestId|returnTo|hmac)
let generateState (secret: string) (guestId: string) (returnTo: string) : JS.Promise<string> =
    promise {
        let timestamp = string (epochNow ())
        let payload = sprintf "%s|%s|%s" timestamp guestId returnTo
        let! hmac = hmacSha256 secret payload
        let token = sprintf "%s|%s" payload hmac
        return base64urlEncode token
    }

/// Verify a signed state token. Returns Ok (guestId, returnTo) or Error message.
let verifyState (secret: string) (state: string) : JS.Promise<Result<string * string, string>> =
    promise {
        try
            let decoded = base64urlDecode state
            let parts = decoded.Split('|')
            if parts.Length < 4 then return Error "Invalid state"
            else
                let timestamp = parts.[0]
                let guestId = parts.[1]
                let returnTo = parts.[2]
                let receivedHmac = parts.[3]
                let payload = sprintf "%s|%s|%s" timestamp guestId returnTo
                let! expectedHmac = hmacSha256 secret payload
                if receivedHmac <> expectedHmac then
                    return Error "Invalid state signature"
                else
                    let ts = int timestamp
                    let now = epochNow ()
                    if now - ts > 900 then
                        return Error "State token expired"
                    else
                        return Ok (guestId, returnTo)
        with _ ->
            return Error "Invalid state"
    }

// ============================================================
// OAuth protocol — auth URL, code exchange, userinfo fetch
// ============================================================

/// Build the authorization redirect URL for a provider.
let generateAuthUrl
    (provider: ProviderConfig)
    (clientId: string)
    (redirectUri: string)
    (state: string)
    : string =
    sprintf "%s?client_id=%s&redirect_uri=%s&scope=%s&state=%s&response_type=code"
        provider.AuthorizeUrl
        (JS.encodeURIComponent clientId)
        (JS.encodeURIComponent redirectUri)
        (JS.encodeURIComponent provider.Scopes)
        (JS.encodeURIComponent state)

/// Exchange an authorization code for an access token.
let exchangeCode
    (provider: ProviderConfig)
    (code: string)
    (redirectUri: string)
    (clientId: string)
    (clientSecret: string)
    : JS.Promise<string> =
    promise {
        let body =
            sprintf "grant_type=authorization_code&code=%s&redirect_uri=%s&client_id=%s&client_secret=%s"
                (JS.encodeURIComponent code)
                (JS.encodeURIComponent redirectUri)
                (JS.encodeURIComponent clientId)
                (JS.encodeURIComponent clientSecret)
        let options = createObj [
            "method" ==> "POST"
            "headers" ==> createObj [
                "Content-Type" ==> "application/x-www-form-urlencoded"
                "Accept" ==> "application/json"
            ]
            "body" ==> body
        ]
        let! response = fetchRaw provider.TokenUrl options
        if not response.ok then
            let! text = responseText response
            return failwith (sprintf "Token exchange failed (%d): %s" response.status text)
        else
        let! data = responseJson response
        let token : string = data?access_token |> unbox
        if isNull (box token) || token = "" then
            return failwith (sprintf "No access_token in response")
        else
        return token
    }

/// Fetch and normalize user info from the provider.
let fetchUserinfo (provider: ProviderConfig) (accessToken: string) : JS.Promise<UserInfo> =
    promise {
        let options = createObj [
            "headers" ==> createObj [
                "Authorization" ==> sprintf "Bearer %s" accessToken
                "Accept" ==> "application/json"
                "User-Agent" ==> "Hedge"
            ]
        ]
        let! response = fetchRaw provider.UserinfoUrl options
        if not response.ok then
            let! text = responseText response
            return failwith (sprintf "Userinfo fetch failed (%d): %s" response.status text)
        else
        let! data = responseJson response
        return provider.ParseUserinfo data
    }
