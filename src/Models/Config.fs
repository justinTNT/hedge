module Models.Config

/// App configuration models.
/// Port of: horatio/models/Config/*.elm

/// Global configuration embedded per-host.
/// Passed to the client as init data.
type GlobalConfig = {
    SiteName: string
    Features: FeatureFlags
}

and FeatureFlags = {
    Comments: bool
    Submissions: bool
    Tags: bool
}

/// Admin hook — triggers an event when an admin operation matches.
type AdminHook = {
    Table: string
    Trigger: Trigger
    Condition: Condition option
    Event: string
}

and Trigger =
    | OnInsert
    | OnUpdate
    | OnDelete

and Condition =
    | Eq of Value * Value
    | Neq of Value * Value
    | IsNull of RowRef * string
    | IsNotNull of RowRef * string
    | And of Condition * Condition
    | Or of Condition * Condition

and Value =
    | Const of string
    | Field of RowRef * string

and RowRef =
    | Before
    | After

/// Condition helpers — mirror Hamlet's DSL.
module Condition =
    let changed field =
        Neq (Field (Before, field), Field (After, field))

    let changedTo field value =
        And (changed field, Eq (Field (After, field), Const value))

    let changedFrom field value =
        And (changed field, Eq (Field (Before, field), Const value))

/// Cron schedule entry.
type CronEvent = {
    Event: string
    Schedule: string
}

/// Horatio's hook configuration.
let adminHooks : AdminHook list = [
    { Table = "item_comment"
      Trigger = OnUpdate
      Condition = Some (Condition.changed "removed")
      Event = "CommentModerated" }
]

/// Horatio's cron configuration.
let cronEvents : CronEvent list = [
    { Event = "HardDeletes"; Schedule = "23 * * * *" }
]
