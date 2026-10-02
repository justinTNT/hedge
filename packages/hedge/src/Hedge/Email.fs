module Hedge.Email

// Transactional email for passwordless magic-link sign-in. hedge sends no email natively, so this owns a
// single `EmailSender` seam with three real senders (Resend, Mailgun, AWS SES) + a log-only stub, chosen by
// config. Adding a future sender = one `sendVia*` + one `selectSender` arm. Also holds the stateless
// HMAC-signed magic-link token (same shape as OAuth's signed state — no DB).

open Fable.Core
open Fable.Core.JsInterop
open Hedge.Workers

/// A message to send. Text is the fallback body; Html the rich one.
type EmailMessage = {| To: string; Subject: string; Html: string; Text: string |}

/// The seam every sender satisfies: deliver a message, Ok on success, Error with a reason otherwise.
type EmailSender = EmailMessage -> JS.Promise<Result<unit, string>>

[<Emit("btoa($0)")>]
let private btoa (s: string) : string = jsNative

let private ok (resp: WorkerResponse) (name: string) : JS.Promise<Result<unit, string>> =
    promise {
        if resp.ok then return Ok ()
        else
            let! t = responseText resp
            return Error (sprintf "%s send failed (%d): %s" name resp.status t)
    }

/// Resend — POST /emails, Bearer key, JSON body.
let sendViaResend (apiKey: string) (fromAddr: string) : EmailSender =
    fun (msg: EmailMessage) ->
        promise {
            let body =
                JS.JSON.stringify (createObj [
                    "from" ==> fromAddr; "to" ==> [| msg.To |]; "subject" ==> msg.Subject
                    "html" ==> msg.Html; "text" ==> msg.Text ])
            let! resp =
                fetchRaw "https://api.resend.com/emails" (createObj [
                    "method" ==> "POST"
                    "headers" ==> createObj [
                        "Authorization" ==> ("Bearer " + apiKey)
                        "Content-Type" ==> "application/json" ]
                    "body" ==> body ])
            return! ok resp "Resend"
        }

/// Mailgun — POST /v3/<domain>/messages, HTTP Basic api:<key>, form-encoded body.
let sendViaMailgun (apiKey: string) (domain: string) (fromAddr: string) : EmailSender =
    fun (msg: EmailMessage) ->
        promise {
            let form =
                [ "from", fromAddr; "to", msg.To; "subject", msg.Subject; "html", msg.Html; "text", msg.Text ]
                |> List.map (fun (k, v) -> sprintf "%s=%s" k (JS.encodeURIComponent v))
                |> String.concat "&"
            let! resp =
                fetchRaw (sprintf "https://api.mailgun.net/v3/%s/messages" domain) (createObj [
                    "method" ==> "POST"
                    "headers" ==> createObj [
                        "Authorization" ==> ("Basic " + btoa ("api:" + apiKey))
                        "Content-Type" ==> "application/x-www-form-urlencoded" ]
                    "body" ==> form ])
            return! ok resp "Mailgun"
        }

/// AWS SES v2 — POST /v2/email/outbound-emails, signed with SigV4 (service "ses").
let sendViaSes (accessKeyId: string) (secretKey: string) (region: string) (fromAddr: string) : EmailSender =
    fun (msg: EmailMessage) ->
        promise {
            let host = sprintf "email.%s.amazonaws.com" region
            let path = "/v2/email/outbound-emails"
            let body =
                JS.JSON.stringify (createObj [
                    "FromEmailAddress" ==> fromAddr
                    "Destination" ==> createObj [ "ToAddresses" ==> [| msg.To |] ]
                    "Content" ==> createObj [
                        "Simple" ==> createObj [
                            "Subject" ==> createObj [ "Data" ==> msg.Subject ]
                            "Body" ==> createObj [
                                "Html" ==> createObj [ "Data" ==> msg.Html ]
                                "Text" ==> createObj [ "Data" ==> msg.Text ] ] ] ] ])
            let! payloadHash = Hedge.MobileSession.sha256Hex body
            let (amzDate, dateStamp) = Hedge.Sigv4.now ()
            // Signed headers must be sorted by name and match the headers actually sent (host is set by fetch).
            let signedHeaders = [ "content-type", "application/json"; "host", host; "x-amz-date", amzDate ]
            let! authz =
                Hedge.Sigv4.authorizationHeader
                    accessKeyId secretKey region "ses" amzDate dateStamp "POST" path "" signedHeaders payloadHash
            let! resp =
                fetchRaw (sprintf "https://%s%s" host path) (createObj [
                    "method" ==> "POST"
                    "headers" ==> createObj [
                        "Content-Type" ==> "application/json"
                        "X-Amz-Date" ==> amzDate
                        "Authorization" ==> authz ]
                    "body" ==> body ])
            return! ok resp "SES"
        }

/// Stub — logs the message (incl. the magic link in Text) and sends nothing. Lets the whole flow run and be
/// tested before a provider is provisioned.
let sendViaStub : EmailSender =
    fun (msg: EmailMessage) ->
        promise {
            JS.console.log (sprintf "[email:stub] to=%s subject=%s\n%s" msg.To msg.Subject msg.Text)
            return Ok ()
        }

/// All email config, read from the app env. Provider names: resend | mailgun | ses | stub.
type EmailConfig =
    {| Provider: string; From: string
       ResendKey: string
       MailgunKey: string; MailgunDomain: string
       SesId: string; SesSecret: string; SesRegion: string |}

let private has (s: string) = not (isNull s) && s <> ""

/// Build the active sender from config — None when the provider is unknown or its creds are missing (so an
/// unconfigured site simply omits email rather than erroring). Adding a sender = one arm here.
let selectSender (c: EmailConfig) : EmailSender option =
    match c.Provider with
    | "resend" when has c.ResendKey && has c.From -> Some (sendViaResend c.ResendKey c.From)
    | "mailgun" when has c.MailgunKey && has c.MailgunDomain && has c.From -> Some (sendViaMailgun c.MailgunKey c.MailgunDomain c.From)
    | "ses" when has c.SesId && has c.SesSecret && has c.SesRegion && has c.From -> Some (sendViaSes c.SesId c.SesSecret c.SesRegion c.From)
    | "stub" -> Some sendViaStub
    | _ -> None

// ---- Magic-link token: stateless, HMAC-signed (mirrors OAuth.generateState/verifyState) ----

/// Mint a signed magic-link token binding the REQUESTING guest + email + return path with a short TTL.
/// Binding the guest is the CSRF/adoption protection (the OAuth-state analogue): verify requires the same
/// guest, so a link opened in a different browser can't complete and mutate that browser's identities.
/// No storage. `guestId` and `email` never contain '|' (newId; email is validated at the route).
let generateEmailToken (secret: string) (guestId: string) (email: string) (returnTo: string) (ttlSeconds: int) : JS.Promise<string> =
    promise {
        let expiry = epochNow () + ttlSeconds
        let payload = sprintf "%s|%s|%s|%d" guestId email returnTo expiry
        let! mac = hmacSha256 secret payload
        return base64urlEncode (payload + "|" + mac)
    }

/// Verify a magic-link token: signature + expiry. Returns the bound guest + email + return path. Exactly
/// five '|' fields (guestId|email|returnTo|expiry|mac) — none of the first four contain '|' (guestId is a
/// newId, email is route-validated, returnTo is safeReturnPath'd, expiry is digits), so any other count is
/// tampering.
let verifyEmailToken (secret: string) (token: string) : JS.Promise<Result<{| GuestId: string; Email: string; ReturnTo: string |}, string>> =
    promise {
        try
            let parts = (base64urlDecode token).Split('|')
            if parts.Length <> 5 then return Error "Invalid token"
            else
                let guestId, email, returnTo, expiry, mac = parts.[0], parts.[1], parts.[2], parts.[3], parts.[4]
                let payload = sprintf "%s|%s|%s|%s" guestId email returnTo expiry
                let! expected = hmacSha256 secret payload
                if mac <> expected then return Error "Invalid token signature"
                elif int expiry < epochNow () then return Error "Token expired"
                else return Ok {| GuestId = guestId; Email = email; ReturnTo = returnTo |}
        with _ -> return Error "Invalid token"
    }
