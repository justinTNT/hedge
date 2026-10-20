module Calendar.Composition

// Bind the calendar module's handlers over a Services into the generated RouteContract.Handlers
// record — the one authored place that closes each handler over the host's capabilities. The host
// builds the Services (its Server.ModuleServices) and hands bind's result to the site dispatch.

open Calendar.RouteContract
open Calendar.Services

let bind (services: Services) : Handlers =
    { getEntries = fun query request _ctx -> Calendar.Handlers.getEntries query request services
      saveEntry = fun req request _ctx -> Calendar.Handlers.saveEntry req request services
      deleteEntry = fun req request _ctx -> Calendar.Handlers.deleteEntry req request services }
