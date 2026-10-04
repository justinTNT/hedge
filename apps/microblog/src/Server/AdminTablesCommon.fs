module Server.AdminTablesCommon

// Explicit admin exposure (refactor Track 1). Generating/composing a table does NOT expose it in the
// generic admin: the host registers an allowlist of descriptors here and chooses each one's operation
// ceiling. Anything not listed returns 404 and is absent from discovery (Guest, MobileSession,
// MobileAuthCode). Ceilings bind even AdminOwner; curator WRITE workflows stay in the alerts endpoints.
// Four deliberate restrictions vs the old "register everything with full CRUD":
//   - ItemComment: no manual Create (comments are authored through the content flow).
//   - ItemSnapshot: no Create/Update (snapshots are produced by the capture pipeline).
//   - Identity: read-only (no Create/Update/Delete, incl. direct display-name edits).
//   - Guest: not registered at all (no generic guest-row administration).

open Hedge.Admin

let tables : AdminTable list = [
    Blog.AdminGen.item
    Blog.AdminGen.tag
    Blog.AdminGen.itemTag
    { Blog.AdminGen.itemComment with
        SupportedOps = [ OpList; OpRead; OpUpdate; OpDelete ] }
    { Blog.AdminGen.itemSnapshot with
        SupportedOps = [ OpList; OpRead; OpDelete ] }
    { Server.AdminGen.identity with
        SupportedOps = [ OpList; OpRead ] }
    Server.AdminGen.grant
]
