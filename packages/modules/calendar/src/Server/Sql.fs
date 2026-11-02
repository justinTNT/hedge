module Calendar.Sql

// The calendar module's hand-written SQL. Table names come from the generated `Calendar.Db.Tables`
// constants, so the same statements work wherever the module is mounted (`calendar_entries` in any
// host). Visibility rule: `category <> 'personal'` is public; a personal row is visible only to its
// identity_id. (`identities` stays unprefixed — it's the shared, app-level table.)

open Calendar.Db

/// The public stream (a guest's view): one-offs dated inside the window plus recurrences that
/// started on or before its end and have not ended before its start. Bind: [from; to; to; from].
let publicEntries =
    sprintf "SELECT id, date, category, requested, summary, body, repeat, repeat_until, identity_id, created_at, updated_at, deleted_at FROM %s WHERE deleted_at IS NULL AND category <> 'personal' AND ((repeat = 'none' AND date >= ? AND date <= ?) OR (repeat <> 'none' AND date <= ? AND (repeat_until IS NULL OR repeat_until >= ?))) ORDER BY date, created_at" Tables.entry

/// The public stream plus everything one identity owns (pending or published). Bind:
/// [identityId; from; to; to; from].
let visibleEntries =
    sprintf "SELECT id, date, category, requested, summary, body, repeat, repeat_until, identity_id, created_at, updated_at, deleted_at FROM %s WHERE deleted_at IS NULL AND (category <> 'personal' OR identity_id = ?) AND ((repeat = 'none' AND date >= ? AND date <= ?) OR (repeat <> 'none' AND date <= ? AND (repeat_until IS NULL OR repeat_until >= ?))) ORDER BY date, created_at" Tables.entry

/// One live entry, ownership-checked. Bind: [id; identityId].
let ownEntry =
    sprintf "SELECT id, date, category, requested, summary, body, repeat, repeat_until, identity_id, created_at, updated_at, deleted_at FROM %s WHERE id = ? AND identity_id = ? AND deleted_at IS NULL" Tables.entry

/// Bind: [id; date; summary; body; repeat; repeatUntil|NULL; requested|NULL; identityId; now].
let insertPersonal =
    sprintf "INSERT INTO %s (id, date, category, summary, body, repeat, repeat_until, requested, identity_id, created_at) VALUES (?, ?, 'personal', ?, ?, ?, ?, ?, ?, ?)" Tables.entry

/// An owner edit always lands as personal (a published entry is un-published until re-approved).
/// Bind: [date; summary; body; repeat; repeatUntil|NULL; requested|NULL; now; id; identityId].
let updateOwn =
    sprintf "UPDATE %s SET date = ?, summary = ?, body = ?, repeat = ?, repeat_until = ?, requested = ?, category = 'personal', updated_at = ? WHERE id = ? AND identity_id = ? AND deleted_at IS NULL" Tables.entry

/// Bind: [now; id; identityId].
let deleteOwn =
    sprintf "UPDATE %s SET deleted_at = ? WHERE id = ? AND identity_id = ? AND deleted_at IS NULL" Tables.entry

/// The admin's approval queue: owner entries that asked for a public facet and are still personal,
/// oldest first (actioning the visible rows uncovers the rest). For a host's AdminTable descriptor.
let approvalQueue =
    sprintf "SELECT id, date, category, requested, summary, body, repeat, repeat_until, identity_id, created_at, updated_at, deleted_at FROM %s WHERE deleted_at IS NULL AND requested IS NOT NULL AND category = 'personal' ORDER BY created_at ASC LIMIT 100" Tables.entry

/// The queue's narrowed update — only the decision columns, so approving can never clobber the
/// owner's content or ownership. Bind: [category; requested; now; id] (MutableFields = Category, Requested).
let approve =
    sprintf "UPDATE %s SET category = ?, requested = ?, updated_at = ? WHERE id = ?" Tables.entry

/// Identity merge re-attribution (Identity.Attribution.reassign binds [toId; toId; fromId]; the second
/// bind is consumed by a tautology because there is no denormalised author name to refresh).
let reassignOwner =
    sprintf "UPDATE %s SET identity_id = ? WHERE ? IS NOT NULL AND identity_id = ?" Tables.entry
