module Content.Server.Viewer

// The shared, app-independent "who is asking" contract for READS and OWNER-KEYED writes — the
// sibling of Content.Server.Author (which resolves an author for a comment write, with an anonymous
// fallback). A Viewer has no fallback: either the request carries a verified guest whose active
// identity is a claimed provider account, or it is a guest. A content module keeps anything private
// to a person (personal calendar entries, drafts) under the viewer's IdentityId and never sees the
// app's cookie, secret, identity rows or environment.

open Fable.Core
open Hedge.Workers
open Hedge.GuestSession

type Viewer =
    { /// The signed-in identity id — the owner key for anything private to a person — if any.
      IdentityId: string option
      /// A renewal/rotation cookie the host's session policy wants echoed on the response.
      Replacement: string option }

let anonymous : Viewer = { IdentityId = None; Replacement = None }

type ViewerResolver = WorkerRequest -> JS.Promise<Viewer>

/// The resolver for a host that composes no identity layer: everyone is a guest.
let guestOnly : ViewerResolver = fun _ -> promise { return anonymous }

/// A resolver over the host's guest-write authorizer and the identity module's signed-in lookup
/// (Identity.Server.signedIn). A request with no cookie short-circuits BEFORE the signing policy is
/// bound, so a public read works on a deployment that has not configured GUEST_SECRET yet; a rejected
/// cookie is a guest; otherwise the lookup decides. Reading never mints a guest.
let resolve (require: WorkerRequest -> JS.Promise<RequireResult>) (signedIn: string -> JS.Promise<string option>) : ViewerResolver =
    fun request ->
        promise {
            match readCookie request with
            | None -> return anonymous
            | Some _ ->
                let! r = require request
                match r with
                | Rejected -> return anonymous
                | Accepted guest ->
                    let! id = signedIn guest.GuestId
                    return { IdentityId = id; Replacement = guest.Replacement }
        }
