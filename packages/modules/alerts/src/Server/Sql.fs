module Alerts.Sql

// The alerts module's hand-written SQL, table names via Alerts.Db.Tables so the same statements
// work whatever the tablePrefix. Ported from the pre-module alerts branch, with one change: the
// dedup subquery reads the module-owned alerts_promotions table instead of items.origin_entry_key,
// so promotion never depends on (or touches) the host feed's schema.

open Alerts.Db

let selectEnabledAlertSources =
    sprintf "SELECT id, topic, feed_url, enabled, created_at FROM %s WHERE enabled = 1" Tables.alertSource

/// OR IGNORE on entry_key: re-polls and same-run dupes land once. New drafts import
/// un-approved/un-rejected with an empty owner_comment.
let insertPendingPost =
    sprintf
        "INSERT OR IGNORE INTO %s (id, source_id, entry_key, title, link, snippet, published_at, approved, rejected, owner_comment, created_at) VALUES (?, ?, ?, ?, ?, ?, ?, 0, 0, '', ?)"
        Tables.pendingPost

/// Promotable = approved, not rejected, and not already promoted — its entry_key is absent from the
/// module-owned promotions table (was items.origin_entry_key on the monolith). Derived; no flag.
let selectPromotable =
    sprintf
        "SELECT p.id, p.source_id, p.entry_key, p.title, p.link, p.snippet, p.published_at, p.approved, p.rejected, p.owner_comment, p.created_at, s.topic AS topic FROM %s p JOIN %s s ON s.id = p.source_id WHERE p.approved = 1 AND p.rejected = 0 AND p.entry_key NOT IN (SELECT entry_key FROM %s) ORDER BY p.published_at ASC LIMIT 20"
        Tables.pendingPost Tables.alertSource Tables.promotion

/// One promotion row per promoted entry; the unique entry_key index makes a concurrent
/// double-promote roll back at COMMIT (the guarantee the old unique origin_entry_key gave).
let insertPromotion =
    sprintf "INSERT INTO %s (id, entry_key, item_id, created_at) VALUES (?, ?, ?, ?)" Tables.promotion

// ---- Curator surface (dedicated, guest-cookie-authorized) ----

/// The undecided curation queue: not approved, not rejected, oldest-first, with the source topic.
/// Same population the admin's [<AdminList>] shows — a drain-the-backlog view.
let selectPendingQueue =
    sprintf
        "SELECT p.id, p.title, p.link, p.snippet, p.owner_comment, p.published_at, s.topic AS topic FROM %s p JOIN %s s ON s.id = p.source_id WHERE p.approved = 0 AND p.rejected = 0 ORDER BY p.published_at ASC LIMIT 500"
        Tables.pendingPost Tables.alertSource

// State-guarded mutations: each only acts on an UNDECIDED row. A 0-row result means another curator
// already approved/dismissed it (or it's gone) -> the handler returns 409, so a stale page can't edit
// or dismiss something already approved (which the cron may have snapshotted for publication).
let approvePendingPost =
    sprintf "UPDATE %s SET approved = 1 WHERE id = ? AND approved = 0 AND rejected = 0" Tables.pendingPost

let rejectPendingPost =
    sprintf "UPDATE %s SET rejected = 1 WHERE id = ? AND approved = 0 AND rejected = 0" Tables.pendingPost

let updateFraming =
    sprintf "UPDATE %s SET title = ?, snippet = ?, owner_comment = ? WHERE id = ? AND approved = 0 AND rejected = 0" Tables.pendingPost
