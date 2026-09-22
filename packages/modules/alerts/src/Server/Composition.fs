module Alerts.Composition

// C3 — bind the alerts module into the generated RouteContract.Handlers record. Alerts contributes
// no HTTP endpoints, so the record is the no-op placeholder and dispatch always returns None; the
// host composes it into its site dispatch harmlessly. The real entry point — the cron
// (Alerts.Cron.run) — is wired directly by the host as createWorker's Scheduled handler, not here.

open Alerts.RouteContract
open Alerts.Services

let bind (_services: Services) : Handlers =
    { NoEndpoints = () }
