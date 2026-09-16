module Articles.Composition

// C3 — bind the articles module's handlers over a Services into the generated
// RouteContract.Handlers record (mirrors Blog.Composition).

open Articles.RouteContract
open Articles.Services

let bind (services: Services) : Handlers =
    { getPost = fun id -> Articles.Handlers.getPost id services
      submitComment = fun req request ctx -> Articles.Handlers.submitComment req request services ctx
      getFeed = fun query -> Articles.Handlers.getFeed query services }
