module Program

// Fixtures for the OIDC provider subsystem + email senders + magic-link token. See the .fsproj header.

open Fable.Core
open Fable.Core.JsInterop
open Hedge

let mutable failures = 0
let mutable checks = 0
let check name cond =
    checks <- checks + 1
    if not cond then
        failures <- failures + 1
        eprintfn "  FAIL: %s" name

// Install a mock global fetch that records the last request and returns a canned response.
[<Emit("(globalThis.fetch = (url, opts) => { globalThis.__req = { url, opts }; return Promise.resolve({ ok: $0, status: $1, text: () => Promise.resolve($2), json: () => Promise.resolve($3) }); })")>]
let mockFetch (ok: bool) (status: int) (text: string) (json: obj) : unit = jsNative
[<Emit("globalThis.__req.url")>]
let reqUrl () : string = jsNative
[<Emit("(globalThis.__req.opts.headers[$0] || '')")>]
let reqHeader (name: string) : string = jsNative
[<Emit("(globalThis.__req.opts.body || '')")>]
let reqBody () : string = jsNative

let private reg preset issuer clientId : Oidc.OidcRegistration =
    { Preset = preset; Issuer = issuer; AuthorizeUrl = None; TokenUrl = None; UserinfoUrl = None
      Scopes = None; ClientId = clientId; ClientSecret = "secret" }

let run () =
    promise {
        // ---- OIDC: preset claim parsing ----
        let! liCfg = Oidc.toProviderConfig "linkedin" (reg (Some "linkedin") None "x")
        let li = liCfg.ParseUserinfo (createObj [ "sub" ==> "LI1"; "name" ==> "Ada L"; "email" ==> "ada@x.com"; "picture" ==> "http://p" ])
        check "linkedin: standard claims (sub/name/email/provider)"
            (li.ProviderUserId = "LI1" && li.Name = "Ada L" && li.Email = Some "ada@x.com" && li.Provider = "linkedin")
        check "linkedin: preset authorize endpoint" (liCfg.AuthorizeUrl = "https://www.linkedin.com/oauth/v2/authorization")

        let! gCfg = Oidc.toProviderConfig "google" (reg (Some "google") None "x")
        let g = gCfg.ParseUserinfo (createObj [ "id" ==> "G1"; "name" ==> "Bob"; "email" ==> "b@x.com"; "picture" ==> "p" ])
        check "google: provider_user_id reads the v2 `id` (continuity)" (g.ProviderUserId = "G1" && g.Provider = "google")

        let! mCfg = Oidc.toProviderConfig "microsoft" (reg (Some "microsoft") None "x")
        let m = mCfg.ParseUserinfo (createObj [ "id" ==> "M1"; "displayName" ==> "Carol"; "mail" ==> "c@x.com" ])
        check "microsoft: Graph override (displayName/mail/id)"
            (m.ProviderUserId = "M1" && m.Name = "Carol" && m.Email = Some "c@x.com")
        check "microsoft: userinfo is Graph /me" (mCfg.UserinfoUrl = "https://graph.microsoft.com/v1.0/me")

        // ---- OIDC: discovery from an arbitrary issuer ----
        mockFetch true 200 "" (createObj [ "authorization_endpoint" ==> "https://idp.test/a"; "token_endpoint" ==> "https://idp.test/t"; "userinfo_endpoint" ==> "https://idp.test/u" ])
        let! dCfg = Oidc.toProviderConfig "okta" (reg None (Some "https://idp.test") "x")
        check "discovery: resolves endpoints from .well-known"
            (dCfg.AuthorizeUrl = "https://idp.test/a" && dCfg.TokenUrl = "https://idp.test/t" && dCfg.UserinfoUrl = "https://idp.test/u")
        let d = dCfg.ParseUserinfo (createObj [ "sub" ==> "OK1"; "name" ==> "Dan"; "email" ==> "d@x.com" ])
        check "discovery: standardClaims attributes under the registration name" (d.ProviderUserId = "OK1" && d.Provider = "okta")
        let d2 = dCfg.ParseUserinfo (createObj [ "sub" ==> "OK2"; "given_name" ==> "Eve"; "family_name" ==> "Stone" ])
        check "standardClaims: name falls back to given+family" (d2.Name = "Eve Stone")

        // ---- SES SigV4: signing key vs AWS's published derivation vector ----
        let! kHex = Sigv4.signingKeyHex "wJalrXUtnFEMI/K7MDENG+bPxRfiCYEXAMPLEKEY" "20120215" "us-east-1" "iam"
        check "sigv4: signing key matches AWS vector"
            (kHex = "f4780e2d9f65fa895f9c67b32ce1baf0b0d8a43505a000a1a9e090d414db404d")

        // ---- Email senders: request shapes + Ok/Error mapping ----
        let msg : Email.EmailMessage = {| To = "a@b.com"; Subject = "S"; Html = "<p>H</p>"; Text = "T" |}

        mockFetch true 200 "" (createObj [])
        let! rr = (Email.sendViaResend "rk_test" "from@x.com") msg
        check "resend: Ok on 200" (match rr with Ok _ -> true | _ -> false)
        check "resend: POST /emails" (reqUrl () = "https://api.resend.com/emails")
        check "resend: Bearer auth" (reqHeader "Authorization" = "Bearer rk_test")
        check "resend: JSON body carries recipient" ((reqBody ()).Contains "a@b.com")

        mockFetch true 200 "" (createObj [])
        let! _ = (Email.sendViaMailgun "mgkey" "mg.x.com" "from@x.com") msg
        check "mailgun: POST /v3/<domain>/messages" ((reqUrl ()).Contains "api.mailgun.net/v3/mg.x.com/messages")
        check "mailgun: HTTP Basic auth" ((reqHeader "Authorization").StartsWith "Basic ")
        check "mailgun: form-encoded body" ((reqBody ()).Contains "to=a%40b.com")

        mockFetch true 200 "" (createObj [])
        let! _ = (Email.sendViaSes "AKID" "secret" "us-east-1" "from@x.com") msg
        check "ses: POST to regional SES v2 endpoint" ((reqUrl ()).Contains "email.us-east-1.amazonaws.com/v2/email/outbound-emails")
        check "ses: SigV4 Authorization header" ((reqHeader "Authorization").StartsWith "AWS4-HMAC-SHA256 ")
        check "ses: X-Amz-Date present" ((reqHeader "X-Amz-Date").Length > 0)

        mockFetch false 500 "boom" (createObj [])
        let! re = (Email.sendViaResend "k" "f@x.com") msg
        check "sender: non-2xx maps to Error" (match re with Error _ -> true | _ -> false)

        // ---- selectSender gating ----
        let baseCfg : Email.EmailConfig =
            {| Provider = "resend"; From = "f@x.com"; ResendKey = "k"
               MailgunKey = ""; MailgunDomain = ""; SesId = ""; SesSecret = ""; SesRegion = "" |}
        check "select: resend with creds -> Some" ((Email.selectSender baseCfg).IsSome)
        check "select: resend missing key -> None" ((Email.selectSender {| baseCfg with ResendKey = "" |}).IsNone)
        check "select: ses needs id+secret+region" ((Email.selectSender {| baseCfg with Provider = "ses"; SesId = "a"; SesSecret = "b"; SesRegion = "us-east-1" |}).IsSome)
        check "select: unknown provider -> None" ((Email.selectSender {| baseCfg with Provider = "nope" |}).IsNone)
        check "select: stub -> Some" ((Email.selectSender {| baseCfg with Provider = "stub" |}).IsSome)

        // ---- Magic-link token: round-trip, tamper, expiry ----
        let secret = "test-secret-at-least-32-bytes-long-string!!"
        let! tok = Email.generateEmailToken secret "user@x.com" "/back" 900
        let! v1 = Email.verifyEmailToken secret tok
        check "token: round-trip returns email + returnTo" (match v1 with Ok r -> r.Email = "user@x.com" && r.ReturnTo = "/back" | _ -> false)
        let! v2 = Email.verifyEmailToken "different-secret-at-least-32-bytes-long!!" tok
        check "token: wrong secret rejected" (match v2 with Error _ -> true | _ -> false)
        let! tokExp = Email.generateEmailToken secret "u@x.com" "/" -100
        let! v3 = Email.verifyEmailToken secret tokExp
        check "token: expired rejected" (match v3 with Error _ -> true | _ -> false)

        if failures = 0 then printfn "auth-fixtures: all %d checks OK" checks
        else eprintfn "auth-fixtures: %d of %d checks FAILED" failures checks
    }

run () |> Promise.start
