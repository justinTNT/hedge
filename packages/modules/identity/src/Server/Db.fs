module Identity.Db

// The shared identity persistence's row projection, hand-written so the identity server layer is
// self-contained and never reaches into a host's generated `Server.Db` (the un-owned identity slice
// still emits its own IdentityRow into each app's combined Server.Db for the owner admin; this is the
// module's own copy for the shared server code). Matches that generated row column-for-column — the
// identity schema is owned as a model by IdentityModels, so this shape is stable.

open Hedge.Workers

type IdentityRow = {
    Id: string
    GuestId: string
    Provider: string
    ProviderUserId: string
    Name: string
    Picture: string
    Email: string option
    ActivatedAt: int option
    CreatedAt: int
}

let parseIdentityRow (row: obj) : IdentityRow =
    { Id = rowStr row "id"
      GuestId = rowStr row "guest_id"
      Provider = rowStr row "provider"
      ProviderUserId = rowStr row "provider_user_id"
      Name = rowStr row "name"
      Picture = rowStr row "picture"
      Email = rowStrOpt row "email"
      ActivatedAt = rowIntOpt row "activated_at"
      CreatedAt = rowInt row "created_at" }
