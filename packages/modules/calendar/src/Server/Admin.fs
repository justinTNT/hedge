module Calendar.Admin

// The module's admin resources beyond the generated per-table CRUD (Calendar.AdminGen.entry). A host
// decides WHICH of these to expose in its allowlist; the module decides WHAT each one may touch.

open Hedge.Admin

/// The approval queue: a second admin resource over the entries table listing users' entries that
/// asked for a public facet and are still personal. Approving = opening one and setting its Category
/// to the requested facet; clearing Requested declines it. The update is narrowed to those two
/// columns, so approving can never clobber the owner's content or ownership (the generic admin
/// treats any mutable field missing from the form as NULL).
let approvalQueue : AdminTable =
    { Calendar.AdminGen.entry with
        Name = "ApprovalQueue"
        SelectAll = Calendar.Sql.approvalQueue
        Update = Calendar.Sql.approve
        MutableFields = [ "Category"; "Requested" ]
        SupportedOps = [ OpList; OpRead; OpUpdate ] }
