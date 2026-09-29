module BoundaryFixtures.Runtime
open Fable.Core
open Hedge.Workers
open Hedge.Router
open Hedge.Admin
open Hedge.Schema
let standalone request ctx = Server.Routes.dispatch request {Marker="fixture"} ctx |> Option.get
let modular request ctx =
    let env:Server.Env.Env = {Marker="fixture"}
    let handlers:Probe.RouteContract.Handlers = {
        readPublic = fun () -> Server.Handlers.readPublic env
        readPrivate = fun () request ctx -> Server.Handlers.privateRequest request env ctx
        by = fun id request ctx -> Server.Handlers.by id request env ctx
        query = fun q request ctx -> Server.Handlers.query q request env ctx
        both = fun id q request ctx -> Server.Handlers.both id q request env ctx
    }
    Probe.RouteContract.dispatch handlers request ctx |> Option.get
let private table = {
    Name="Protected";Table="protected";Schema=schema "Protected" [fieldWith "Id" FString [PrimaryKey]]
    SelectAll="SELECT id FROM protected";SelectOne="SELECT id FROM protected WHERE id=?"
    Insert="";HasCreateTs=false;HasUpdateTs=false;Update="";Delete=""
    MutableFields=[];SupportedOps=[OpList;OpRead]
}
let admin request db =
    let access =
        match getHeader request "X-Test-Access" with
        | "owner" -> AdminOwner
        | "reader" -> AdminSubject ((fun _ op -> op=OpList),Some "renewed-cookie")
        | _ -> AdminAnonymous None
    let config:AdminConfig<D1Database> = {
        Tables=[table;{table with Name="Hidden";SupportedOps=[]}];GetDb=id
        Authorize=fun _ _ -> promise {return access}
    }
    handleRequest config request db (parseRoute request) |> Option.get

let subscribeCredentials changed = Client.AdminCredential.subscribe changed

// --- Mobile bearer-session resolution (Capacitor POC) ---
// A fake hash lookup that resolves ONLY the hash of `validToken` to `guestId` (unknown/expired -> None).
let private mobileDeps (validHash: string) (guestId: string) : Hedge.MobileSession.Deps =
    { LookupByHash = fun h -> promise { return (if h = validHash then Some guestId else None) } }

/// resolve: valid bearer -> "valid:<gid>", present-but-bad -> "invalid", no header -> "no-bearer".
let mobileResolve request (validToken: string) (guestId: string) : JS.Promise<string> =
    promise {
        let! validHash = Hedge.MobileSession.sha256Hex validToken
        match! Hedge.MobileSession.resolve (mobileDeps validHash guestId) request with
        | Hedge.MobileSession.NoBearer -> return "no-bearer"
        | Hedge.MobileSession.Valid g -> return "valid:" + g
        | Hedge.MobileSession.Invalid -> return "invalid"
    }

/// requireGuestOrBearer with a cookie fallback that ALWAYS accepts "COOKIE-GID": a valid bearer wins,
/// a bad bearer must REJECT without consulting the cookie (fail closed), a missing bearer falls back.
let mobileRequire request (validToken: string) (guestId: string) : JS.Promise<string> =
    promise {
        let! validHash = Hedge.MobileSession.sha256Hex validToken
        let cookieResolve _ = promise { return Hedge.GuestSession.Accepted { GuestId = "COOKIE-GID"; Replacement = None } }
        match! Hedge.MobileSession.requireGuestOrBearer (mobileDeps validHash guestId) cookieResolve request with
        | Hedge.GuestSession.Accepted a -> return "accepted:" + a.GuestId
        | Hedge.GuestSession.Rejected -> return "rejected"
    }

/// A bearer-resolved session run through the SHARED role tail (roleFromRequired) — the SAME policy the
/// cookie path uses. Fake grant is "curator" for a non-anonymous subject; `provider=""` means no active
/// identity, `provider="anonymous"` is the shared anon subject (never a role).
let mobileRole request (validToken: string) (guestId: string) (provider: string) (role: string) : JS.Promise<string> =
    promise {
        let! validHash = Hedge.MobileSession.sha256Hex validToken
        let! outcome = Hedge.MobileSession.resolve (mobileDeps validHash guestId) request
        let required =
            match outcome with
            | Hedge.MobileSession.Valid g -> Hedge.GuestSession.Accepted { GuestId = g; Replacement = None }
            | _ -> Hedge.GuestSession.Rejected
        let activeSubject gid =
            promise { return (if gid = guestId && provider <> "" then Some ({ Provider = provider; ProviderUserId = "u1" } : Hedge.AccessControl.Subject) else None) }
        let hasGrant (p: string) (_: string) (r: string) = promise { return (p = provider && r = "curator" && provider <> "anonymous") }
        match! Hedge.AccessControl.roleFromRequired activeSubject hasGrant role required with
        | Hedge.AccessControl.Authorized _ -> return "authorized"
        | Hedge.AccessControl.Forbidden _ -> return "forbidden"
        | Hedge.AccessControl.AuthRequired _ -> return "auth-required"
    }
