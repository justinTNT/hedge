namespace Content

open Fable.Core

/// Immutable per-instance context a host supplies to a content module's hosted
/// functions (Stage 0 of the unified shell; see notes/UNIFIED-SHELL.md).
///
/// The standalone entry supplies `HostContext.standalone`, derived from
/// `window.BASE_PATH` / `window.MOUNT_BASE`. A shell supplies one context per hosted
/// module instance and NEVER falls back to those globals — routing on the hosted path
/// is driven entirely by this record. Routing (strip incoming / build outgoing) uses
/// `BaseSegments @ MountSegments`. API and asset URLs are deliberately NOT built from
/// this: the generated client keeps its own `/api/<module>/*` prefix, and a client
/// mount is never prepended to it.
///
/// This is ordinary shared client code, file-linked into each consuming Client
/// project before the module `.props` — not a project, and not part of the core Hedge
/// library.
type HostContext =
    { /// Deployment sub-path segments (`window.BASE_PATH`), e.g. `["st"]`; `[]` at root.
      BaseSegments: string list
      /// Segments this module instance is mounted at, e.g. `["blog"]` for a secondary
      /// mount, `[]` for the primary module.
      MountSegments: string list
      /// Stable id for this hosted instance — the key for instance-scoped resources
      /// (sockets, editors, listeners). `0` is the singleton standalone instance.
      InstanceId: int
      /// Navigate to a content route (segments relative to this instance's mount),
      /// in-app / SPA. The standalone default drives Feliz.Router.
      Navigate: string list -> unit
      /// Set the browser document title. The standalone default sets it directly; a
      /// shell may scope or debounce it.
      SetDocTitle: string -> unit }

module HostContext =

    [<Emit("window.BASE_PATH || ''")>]
    let private basePathGlobal : string = jsNative

    [<Emit("window.MOUNT_BASE || ''")>]
    let private mountBaseGlobal : string = jsNative

    // Matches the standalone Shared.setDocTitle: "<title> · <site>", or just the site
    // title when passed "". SITE_TITLE is the base (the server pre-sets <title> for SEO).
    [<Emit("(function(t){var b=window.SITE_TITLE||'';document.title=t?(t+' · '+b):b;})($0)")>]
    let private setDocTitleGlobal (title: string) : unit = jsNative

    let private toSegments (s: string) =
        s.Split('/') |> Array.filter (fun x -> x <> "") |> Array.toList

    /// The deployment+mount prefix stripped from / prepended to routes.
    let prefixSegments (ctx: HostContext) = ctx.BaseSegments @ ctx.MountSegments

    /// Drop the deployment/mount prefix from raw router segments, so route matching is
    /// written as though the module were mounted at the root. Also drops a trailing
    /// `?query` segment. If the prefix does not match, the segments pass through
    /// unchanged (same behaviour as the original `stripBase`).
    let routeOf (ctx: HostContext) (segments: string list) =
        let content = segments |> List.filter (fun s -> not (s.StartsWith "?"))
        let rec strip prefix rest =
            match prefix, rest with
            | [], remaining -> remaining
            | p :: ps, r :: rs when p = r -> strip ps rs
            | _ -> content
        strip (prefixSegments ctx) content

    /// A real absolute path for a content route — for anchor `href` values (the shell
    /// intercepts normal clicks; modified clicks / new tabs use the real href). Each
    /// segment is percent-encoded, so a slug or tag containing spaces or reserved
    /// characters (e.g. `/`, `?`, `#`) yields a valid, unambiguous URL.
    let hrefOf (ctx: HostContext) (segments: string list) =
        "/" + (prefixSegments ctx @ segments |> List.map JS.encodeURIComponent |> String.concat "/")

    /// The standalone default: base/mount segments read from `window.BASE_PATH` /
    /// `window.MOUNT_BASE`, title set directly. The caller supplies `navigate` (each
    /// module already has a Feliz.Router-backed `Shared.navigateTo` that prepends the
    /// same base/mount), which keeps this contract free of any React/Feliz dependency —
    /// so it stays pure enough to exercise from a non-browser probe. A function (not a
    /// value) so merely loading this module reads no `window` globals; a shell never
    /// calls it.
    let standalone (navigate: string list -> unit) : HostContext =
        { BaseSegments = toSegments basePathGlobal
          MountSegments = toSegments mountBaseGlobal
          InstanceId = 0
          Navigate = navigate
          SetDocTitle = setDocTitleGlobal }
