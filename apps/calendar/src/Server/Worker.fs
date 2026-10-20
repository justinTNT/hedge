module Server.Worker

open Fable.Core
open Fable.Core.JsInterop
open Hedge.Workers
open Hedge.Router
open Server.Env

[<ExportDefault>]
let exports = createWorker {
    Routes = fun request env ctx ->
        let e = env :?> Env
        // Identity lifecycle (/api/auth/{identities,disconnect,revert,activate}) first; None falls through
        // for every other path, including the framework-owned /api/auth/* routes.
        match Server.ModuleServices.identityHttp e request ctx with
        | Some p -> Some p
        | None -> Server.ModuleServices.dispatch e request ctx
    Admin = Some (fun request env route ->
        Hedge.Admin.handleRequest Server.AdminConfig.adminConfig request (env :?> Env) route)
    OAuth = Some (fun env ->
        let e = env :?> Env
        { Secret = e.OAUTH_SECRET
          Providers = Map.ofList [
              "github", {| ClientId = e.GITHUB_CLIENT_ID; ClientSecret = e.GITHUB_CLIENT_SECRET |}
          ]
          OidcProviders = Map.ofList [
              "google",    { Hedge.Oidc.Preset = Some "google";    Issuer = None; AuthorizeUrl = None; TokenUrl = None; UserinfoUrl = None; Scopes = None; ClientId = e.GOOGLE_CLIENT_ID;    ClientSecret = e.GOOGLE_CLIENT_SECRET }
              "microsoft", { Hedge.Oidc.Preset = Some "microsoft"; Issuer = None; AuthorizeUrl = None; TokenUrl = None; UserinfoUrl = None; Scopes = None; ClientId = e.MICROSOFT_CLIENT_ID; ClientSecret = e.MICROSOFT_CLIENT_SECRET }
              "linkedin",  { Hedge.Oidc.Preset = Some "linkedin";  Issuer = None; AuthorizeUrl = None; TokenUrl = None; UserinfoUrl = None; Scopes = None; ClientId = e.LINKEDIN_CLIENT_ID;  ClientSecret = e.LINKEDIN_CLIENT_SECRET }
          ]
          ResolveIdentity = Server.Handlers.resolveIdentity
          OnOAuthComplete = Server.Handlers.onOAuthComplete
          SendEmail =
            Hedge.Email.selectSender
                {| Provider = e.EMAIL_PROVIDER; From = e.EMAIL_FROM
                   ResendKey = e.RESEND_API_KEY
                   MailgunKey = e.MAILGUN_API_KEY; MailgunDomain = e.MAILGUN_DOMAIN
                   SesId = e.AWS_SES_ACCESS_KEY_ID; SesSecret = e.AWS_SES_SECRET_ACCESS_KEY; SesRegion = e.AWS_SES_REGION |} })
    // Signed guest cookies (independent of OAuth), bound per request so the audience is the host. The one
    // builder Server.GuestConfig.deps is shared with the identity handlers and the entry handlers.
    GuestSession = Some (fun env request -> Server.GuestConfig.deps (env :?> Env) request)
    // Personal entries may carry images: the guest-gated, raster-only, size-capped /api/blobs/guest.
    AllowGuestUploads = true
    Mounts = []
    BlobServing = { PrivatePrefixes = [] }
    Scheduled = None
}
