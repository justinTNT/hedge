module Alerts.Composition

// C3 — bind the alerts module into the generated RouteContract.Handlers record. The curator surface
// (dedicated, guest-cookie-authorized) is composed into the host's site dispatch; the cron
// (Alerts.Cron.run) remains wired directly by the host as createWorker's Scheduled handler.

open Alerts.RouteContract
open Alerts.Services

let bind (services: Services) : Handlers =
    { queue = fun _req request _ctx -> Alerts.Curator.queue services request
      approve = fun req request _ctx -> Alerts.Curator.approve req services request
      dismiss = fun req request _ctx -> Alerts.Curator.dismiss req services request
      editFraming = fun req request _ctx -> Alerts.Curator.editFraming req services request }
