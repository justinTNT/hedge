# calendar module

One stream of dated entries; the month grid is a projection of it; `sitewide | community | art |
personal` are filter facets, not calendars. Recurrence (`daily | weekly | fortnightly | monthly |
yearly`, optional until) is expanded by the client projection. A signed-in user writes personal
entries only they see, and may ask for `community` or `art`; the host's admin approves by setting
the entry's category (the `ApprovalQueue` admin resource), and an owner edit takes it back to
personal until re-approved. `apps/calendar` is the reference host.

## Composing it into a host (e.g. basewatch)

1. `gen-modules.json`: add `{ "module": "../../packages/modules/calendar" }` (`"primary": true` if it
   answers the naked URL). `npm run gen` emits the site glue (`calendar_entries` in schema.sql,
   `Calendar.RouteContract.dispatch` in Routes, `Calendar.AdminGen.tables` in the admin registry).
2. Gen/Models/Codecs/Server/Client projects: reference `src/Models/CalendarModels.fsproj`; import
   `calendar.codecs.props` (Codecs), `calendar.server.props` (Server, after generated Db, before the
   app's AdminGen/Routes) and `calendar.client.props` (Client, after the shared client libraries).
3. Server: build a `Calendar.Services.Services` (`Server.ModuleServices`) — `DB`, a `Viewer` resolver
   (the app's identity layer; `Calendar.Services.guestOnly` when the host has no identity: everyone
   reads the public stream, personal is off), `NewId`, `Now` — and pass
   `Calendar.Composition.bind services` to the generated `Server.Routes.dispatch`. Put the host's
   authorize-then-bound-body preflight in front of `POST /api/calendar/entry[/delete]`.
4. Admin allowlist: `Calendar.AdminGen.entry` plus the queue descriptor (see `apps/calendar`
   `Server/AdminConfig.fs`: `SelectAll = Calendar.Sql.approvalQueue`, `Update = Calendar.Sql.approve`,
   `MutableFields = ["Category"; "Requested"]`). Identity merges: add `Calendar.Sql.reassignOwner` /
   `Calendar.Db.Tables.entry` to the host's attribution policy.
5. Client: host the component via its hosted surface (`Calendar.Client.App.emptyHosted / enterHosted /
   updateHosted / withSession / invalidateContent / contentView`) with a `Content.HostContext` and
   `Calendar.ClientGen.createClient` over the host transport; include `calendar.css` in the host's
   stylesheet. The host enables `AllowGuestUploads` if entry bodies may carry images.
