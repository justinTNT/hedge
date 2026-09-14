module HostContextProbe.Program

// Stage-0 acceptance probe (unified shell): a content module's routing/identity are
// driven by an immutable HostContext, so two hosted instances at different mounts have
// INDEPENDENT URL/ID contexts — articles primary at root, blog secondary at /blog —
// with no reliance on window.MOUNT_BASE. This runs the pure context helpers under the
// real Fable/JS runtime (compiled + node, like SchemaRoundtrip). It never calls
// HostContext.standalone (), so it reads no window globals.

open Content

let mutable private failures = 0
let private check (name: string) (cond: bool) =
    if not cond then eprintfn "FAIL: %s" name; failures <- failures + 1

[<EntryPoint>]
let main _ =
    // Two instances a shell would build: articles primary (mount []), blog secondary
    // (mount ["blog"]), each with its own captured navigation + instance id.
    let mutable articlesNav : string list = []
    let mutable blogNav : string list = []
    let articles =
        { BaseSegments = []; MountSegments = []; InstanceId = 1
          Navigate = (fun s -> articlesNav <- s); SetDocTitle = ignore }
    let blog =
        { BaseSegments = []; MountSegments = [ "blog" ]; InstanceId = 2
          Navigate = (fun s -> blogNav <- s); SetDocTitle = ignore }

    // routeOf strips each instance's OWN prefix, independently.
    check "articles routeOf at root" (HostContext.routeOf articles [ "some-slug" ] = [ "some-slug" ])
    check "blog routeOf strips /blog" (HostContext.routeOf blog [ "blog"; "some-slug" ] = [ "some-slug" ])
    check "blog routeOf strips /blog/tag" (HostContext.routeOf blog [ "blog"; "tag"; "news" ] = [ "tag"; "news" ])
    // A URL for the other module is NOT mis-stripped by this instance.
    check "articles leaves /blog/* intact" (HostContext.routeOf articles [ "blog"; "x" ] = [ "blog"; "x" ])
    // Trailing ?query segment is dropped before matching.
    check "routeOf drops ?query" (HostContext.routeOf articles [ "slug"; "?tab=1" ] = [ "slug" ])

    // hrefOf builds each instance's real path independently (real hrefs for anchors).
    check "articles href" (HostContext.hrefOf articles [ "some-slug" ] = "/some-slug")
    check "blog href" (HostContext.hrefOf blog [ "some-slug" ] = "/blog/some-slug")

    // A deployment sub-path composes as base + mount (base is for routing only; it is
    // never prepended to /api — the generated client owns that prefix).
    let blogUnderBase = { blog with BaseSegments = [ "st" ] }
    // hrefOf percent-encodes each segment (a slug/tag with spaces or reserved chars).
    check "hrefOf encodes spaces" (HostContext.hrefOf blog [ "hello world" ] = "/blog/hello%20world")
    check "hrefOf encodes reserved" (HostContext.hrefOf blog [ "tag"; "a/b?c" ] = "/blog/tag/a%2Fb%3Fc")
    check "blog under /st href" (HostContext.hrefOf blogUnderBase [ "x" ] = "/st/blog/x")
    check "blog under /st routeOf" (HostContext.routeOf blogUnderBase [ "st"; "blog"; "x" ] = [ "x" ])
    check "prefixSegments base+mount" (HostContext.prefixSegments blogUnderBase = [ "st"; "blog" ])

    // Navigation + identity are captured per-instance (independent contexts).
    articles.Navigate [ "a" ]
    blog.Navigate [ "b"; "c" ]
    check "independent navigation" (articlesNav = [ "a" ] && blogNav = [ "b"; "c" ])
    check "distinct instance ids" (articles.InstanceId <> blog.InstanceId)

    if failures > 0 then
        eprintfn "host-context-probe: %d failure(s)" failures
        1
    else
        printfn "host-context-probe: independent URL/ID contexts OK (articles root, blog /blog)"
        0
