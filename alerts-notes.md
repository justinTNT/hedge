# Alert monitoring — handoff notes

Feature on branch `alerts` (5 commits off `main`, **not merged/pushed**). Built and
runtime-tested locally on 2026-07-20. Full plan/rationale: `~/.claude/plans/its-been-months-i-happy-rivest.md`.

## What it does

Google Alerts "Deliver to RSS feed" → an hourly Worker cron polls each enabled feed,
imports new entries as verbatim `PendingPost` drafts → admin curates in the existing
generic admin UI → the same cron promotes approved drafts into normal topic-tagged
`MicroblogItem`s.

Admin workflow (generic admin only, no new screens): **Edit** a pending row to fix the
title / write an owner comment / tick **Approved**; tick **Rejected** to drop it.

## Design (the load-bearing bits)

- **First framework cron.** `createWorker` now returns `{| fetch; scheduled |}`;
  `WorkerConfig.Scheduled` is optional and inert without `[triggers]`.
- **Promotion is derived, not flagged.** A draft is promoted iff its `EntryKey` appears in
  `items.origin_entry_key` (nullable-unique). No `promoted_at` for the admin form to clobber;
  the unique key rolls back a concurrent double-promote. Rejected rows persist as tombstones;
  `entry_key` + `INSERT OR IGNORE` block re-import. **No high-water mark** (removed after review
  found it dup-imported and silently dropped back-dated entries).
- **Two seams in `Server/Alerts.fs`.** `parseFeed : string -> FeedEntry list` is the *only*
  fragile code (regex-over-Atom, swappable for `fast-xml-parser` if drift/2nd producer);
  `ingestEntries` is the reusable tail a future push route (`POST /api/ingest/:sourceId`) can
  reuse with no cron/regex.

## Proven locally (via `wrangler dev --test-scheduled`)

Import, re-poll dedup, admin approve (bool stored as integer `1`), concurrent double-fire →
exactly one item, approve+reject → not promoted (rejected wins), feed + tag visibility.
`./test.sh` green (gen idempotence, check-sql on both schema worlds, builds, scaffold).

## NOT yet exercised (production first-contact)

1. **A real Google Alerts feed.** Tested against a hand-written fixture. Biggest unknown —
   but isolated behind `parseFeed`.
2. **The hourly cron actually firing** (only `/__scheduled` by hand locally).
3. **Migration 0008 on populated tenant data** (tested on a fresh DB; additive, safe).
4. **The admin UI in a real browser** (checkbox / delete-confirm / clickable-link verified by
   build + code, not by clicking).

## Deployment (in order)

1. **De-risk the parser first, no deploy:** create the real Google Alert, point a *local*
   source at its URL, fire the local cron, inspect `pending_posts`:
   ```
   ./infra.sh add-alert <topic> '<real-feed-url>'
   curl "http://localhost:8787/__scheduled?cron=0+*+*+*+*"
   ```
2. **Migration before code** (additive → old code unaffected): `./infra.sh migrate-remote`,
   then `./infra.sh deploy`.
3. **Register the source in prod:** `./infra.sh add-alert <topic> '<url>' --remote`.
4. **Trigger the first run** from the Cloudflare dashboard (deployed crons have no CLI trigger).

## User-testing checklist

- Real feed parses (watch the `alerts: … parsed=N imported=M` log — `parsed=0` on a non-empty
  body = feed drift → swap in `fast-xml-parser` behind `parseFeed`).
- Curating in the generic admin is tolerable at an hourly rhythm.
- Approve → item appears in feed under its topic; reject → stays gone across polls.

## Known limitations / follow-ups

- Sources are registered via CLI (`infra.sh add-alert`); no admin "Create" screen yet.
- `pending_posts` grows unbounded (promoted + rejected rows kept as dedup tombstones);
  a promoted-row purge is a later chore.
- `Approved`+`Rejected` are two bools; `rejected` wins if both set.
- LLM draft-transform seam (transform-on-poll, additive columns) — schema kept minimal for it.
