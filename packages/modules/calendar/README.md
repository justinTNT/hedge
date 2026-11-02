# calendar module

One stream of dated entries; the month grid is a projection of it; the facets are filters, not
calendars. The module reserves one facet, `personal` (private to the signed-in identity that wrote
it); the host names the public ones (`sitewide | community | art` on the reference host) and which
of them a user may ask for. Recurrence (`daily | weekly | fortnightly | monthly | yearly`, optional
until) is expanded by the client projection. A signed-in user writes personal entries only they
see, and may ask for a requestable facet; the host's admin approves by setting the entry's category
(the module's `ApprovalQueue` admin resource), and an owner edit takes it back to personal until
re-approved. `apps/calendar` is the reference host.

## Composing it into a host (e.g. basewatch)

1. `gen-modules.json`: add `{ "module": "../../packages/modules/calendar" }` (`"primary": true` if it
   answers the naked URL). `npm run gen` emits the site glue (`calendar_entries` in schema.sql,
   `Calendar.RouteContract.dispatch` in Routes, `Calendar.AdminGen.tables` in the admin registry).
2. Gen/Models/Codecs/Server/Client projects: reference `src/Models/CalendarModels.fsproj`; import
   `calendar.codecs.props` (Codecs), `calendar.server.props` (Server, after generated Db, before the
   app's AdminGen/Routes) and `calendar.client.props` (Client, after the shared client libraries).
3. Facets: define the site's vocabulary ONCE where both server and client see it (the host's Models
   assembly, e.g. `Models.Site.calendarFacets = Calendar.Vocab.Facets.define [public…] [requestable…]`;
   it fails fast at load on a name the module can't honour) and give each public facet a colour in
   the host stylesheet (`.calendar-content .cat-<name> { --cat: … }`).
4. Server: build a `Calendar.Services.Services` (`Server.ModuleServices`) — `DB`, a `Viewer` resolver
   (`Content.Server.Viewer.resolve (GuestConfig.require env) (Identity.Server.signedIn env.DB)` over the
   app's identity layer; `Content.Server.Viewer.guestOnly` when the host has no identity: everyone
   reads the public stream, personal is off), `Facets`, `Derived` (`Calendar.Services.noDerived`, or a
   per-window projection of another service's data into read-only entries, with — when that source is
   an instrument that can be off — the days it observed and its newest one: the grid hatches unwatched
   past days and the landing route opens on the newest month; see `apps/flare`), `NewId`,
   `Now` — and pass
   `Calendar.Composition.bind services` to the generated `Server.Routes.dispatch`, with the framework's
   `Hedge.Router.guestBoundedPost` in front of `POST /api/calendar/entry[/delete]`. The Server project
   references `packages/content-server/ContentServer.fsproj`.
5. Admin allowlist: `Calendar.AdminGen.entry` plus `Calendar.Admin.approvalQueue`. Identity merges: add
   `Calendar.Sql.reassignOwner` / `Calendar.Db.Tables.entry` to the host's attribution policy.
6. Client: host the component via its hosted surface (`Calendar.Client.App.emptyHosted facets /
   enterHosted / updateHosted / withSession / invalidateContent / contentView`) with a
   `Content.HostContext` and `Calendar.ClientGen.createClient` over the host transport; include
   `calendar.css` in the host's stylesheet. The host enables `AllowGuestUploads` if entry bodies may
   carry images.
