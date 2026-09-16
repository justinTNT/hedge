module Server.ModuleServices

// C3 — the single place that adapts this app's environment into each content module's Services.
// It is the only server file that knows BOTH the app's Env / Server.Identity AND a module's
// Services type, so the module itself stays ignorant of the app.

open Hedge.Workers
open Content.Server.Author
open Server.Env

/// Build the author resolver from the app's Server.Identity: ensure the guest + anonymous
/// identity exist (one batch), then return the active (claimed) identity or the anon fallback —
/// exactly the flow the module used to run inline against Server.Identity.
let private authorResolver (db: D1Database) : AuthorResolver =
    { ResolveAuthor = fun (req: AuthorRequest) ->
        promise {
            let! _ =
                db.batch([|
                    Server.Identity.ensureGuestStmt db req.GuestId req.Now
                    Server.Identity.ensureAnonymousStmt db req.FallbackIdentityId req.GuestId req.AuthorName req.Now
                |])
            let! active = Server.Identity.activeFor db req.GuestId
            return
                { IdentityId = active |> Option.map (fun i -> i.Id) |> Option.defaultValue req.FallbackIdentityId
                  Picture = active |> Option.map (fun i -> i.Picture) |> Option.defaultValue "" }
        } }

/// The blog module's Services, adapted from this app's Env.
let blog (env: Env) : Blog.Services.Services =
    { DB = env.DB
      Blobs = env.BLOBS
      Events = env.EVENTS
      AdminKey = env.ADMIN_KEY
      Author = authorResolver env.DB
      NewId = newId
      Now = epochNow }
