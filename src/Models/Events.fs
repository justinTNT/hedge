module Models.Events

/// Internal event payloads.
/// These are backend-only events triggered by admin hooks or cron jobs.
/// They flow through a queue, not to the client.
///
/// Port of: horatio/models/Events/*.elm

/// Triggered when an admin changes the `removed` field on a comment.
/// Payload contains before/after row snapshots as raw JSON.
type CommentModerated = {
    Before: string option
    After: string option
}
