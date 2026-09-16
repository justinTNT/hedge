module Blog.Composition

// C3 — bind the blog module's handlers over a Services into the generated
// RouteContract.Handlers record. The one authored place that closes each handler over the
// host's capabilities; the app builds the Services (Server.ModuleServices) and hands bind's
// result to the site dispatch.

open Blog.RouteContract
open Blog.Services

let bind (services: Services) : Handlers =
    { getItemsByTag = fun id query -> Blog.Handlers.getItemsByTag id query services
      getTags = fun () -> Blog.Handlers.getTags services
      getItem = fun id -> Blog.Handlers.getItem id services
      submitItem = fun req request ctx -> Blog.Handlers.submitItem req request services ctx
      submitComment = fun req request ctx -> Blog.Handlers.submitComment req request services ctx
      submitSnapshot = fun req request _ctx -> Blog.Snapshots.capture req request services
      getFeed = fun query -> Blog.Handlers.getFeed query services }
