# Hedge request and admin boundaries

## Generated private GET handlers

Existing GET handlers retain their signatures. A models endpoint can opt in to
the actual request and execution context:

```fsharp
module GetAccess =
    type Response = { CanReview:bool }
    let requestContext = true
    let endpoint : Get<Response> = Get "/api/access"
```

Standalone handlers receive `request env ctx` after any ID/query arguments.
Module `RouteContract` delegates receive `request ctx` after their ordinary
arguments, including the unit argument of a parameterless GET. The host still
binds environment/services. `Get`, `GetBy`, `GetQuery` and `GetByQuery` support
this opt-in; POST already receives request/context.

Define actual nested `Request`/`Response` records where the generator discovers
them. Shared nested DTOs can be referenced by those records. Use lists for
collection DTOs supported by the codec. No authentication policy is inferred
from `requestContext`; the handler/host must enforce it.

Generated POST routing decodes `request.text()`. For private or size-sensitive
APIs, the host must authorize and bound the untrusted stream before handing it
to generated routing. Native Plants demonstrates this with its app-specific
24,000-byte preflight and bounded reconstructed request. Do not clone an
unbounded stream, or treat DTO validation as a streaming request limit.

## Browser transports and session coordination

`Client.Api.browserTransport` is the ordinary transport. Private clients can
compose `Client.GuestSession.transport Client.Api.uncachedBrowserTransport`.
The former joins the existing `HedgeGuest.withSessionRequest` lock; the latter
uses `cache: no-store`, applies `BASE_PATH` once, carries supplied headers and
consumes response text before resolving. The generated client interprets HTTP
statuses and decodes success bodies into `Result<_,Hedge.Http.ApiError>`.

Capture credential/viewer headers when creating a request. Do not nest calls
to the session lock. Multipart and binary APIs may retain their own browser
adapters; they must consume response bodies within the lock as well. Apps still
own request-generation checks that discard responses after navigation, logout
or account changes. Public catalogue reads need not join the session lock.

## Owner credential notifications

Include `src/Client/AdminCredential.fs` in a client that uses the owner key.
`read`, `write`, `clear` and `subscribe` preserve the `adminKey` storage entry.
Writes and clears notify the current document with
`hedge:admin-credential-changed`; other documents receive the browser storage
event. Notifications contain no key. `subscribe` returns a disposer.

`write`/`clear` return an explicit error when storage fails. A stored key or a
notification is never confirmed permission. Clients clear sensitive state and
fetch server-confirmed permissions when it changes, and reject completions
captured under an older credential/generation. The common admin implements
this for discovery, lists, record editing and mutations.

OAuth logout and the owner key are independent. Apps consume identity/session
signals separately and retain focus/visibility/periodic server revalidation for
role revocation; credential events do not replace authorization checks.

## Admin operation ceilings

Each `Hedge.Admin.AdminTable` has `SupportedOps: AdminOp list`. Generated
descriptors default to the current full CRUD set. Hosts can explicitly register
and narrow descriptors:

```fsharp
let tables = [
    AdminGen.article
    { AdminGen.identity with SupportedOps = [OpList; OpRead] }
]
```

Discovery and direct handlers use the same intersection of supported operations
and caller permissions. `AdminOwner` permits all supported operations, but does
not bypass a resource ceiling. `AdminSubject` contributes its permission
predicate. An empty supported list exposes no operations. Resources omitted
from the host's list are unavailable even to an owner.

Schemas, role definitions and identity lifecycle are unchanged. The host owns
its resource list; content modules do not gain admin rights automatically.

`test/BoundaryFixtures/run.sh` compiles real generated routes/clients and the
shared admin, and checks these contracts under Node. It runs in `./test.sh`.
