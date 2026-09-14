module Articles.Client.Shell.Types

// The Justat shell's Elmish types (unified shell, Stage 2). The shell owns one router +
// identity + chrome and hosts BOTH content modules — articles at the root, blog at
// /blog — via their Stage-0 hosted surfaces, with seamless SPA navigation between them.
//
// Child messages carry the ACTIVATION they were issued under. A navigation bumps the
// activation, so a late read from a route we've left is recognised as stale and dropped;
// the outgoing module's live resources are disposed before the incoming module enters
// (the LeaveCompleted transition, keyed by a monotonic id so a newer navigation wins).

module A = Articles.Client.Types
module B = Blog.Client.Types

type ModuleId =
    | Articles
    | Blog

type Msg =
    /// The shell's single router changed the path (raw segments, incl. the deployment base).
    | UrlChanged of segments: string list
    /// A message from the hosted articles child, tagged with its activation.
    | ArticlesMsg of activation: int * message: A.Msg
    /// A message from the hosted blog child, tagged with its activation.
    | BlogMsg of activation: int * message: B.Msg
    /// A message from the shell-owned identity subsystem.
    | IdentityMsg of Identity.Msg
    /// Fired after the outgoing activation's resources have been disposed; enters the
    /// pending target route. Ignored if a newer navigation has superseded it.
    | LeaveCompleted of transition: int

type Model =
    { /// Which module is currently shown.
      Active: ModuleId
      /// The active module's own (mount-local) content route.
      Route: string list
      /// Monotonic activation id; bumped on every navigation. Tags child messages so a
      /// stale read (from a route we've left) can be dropped.
      Activation: int
      /// An in-flight transition (its activation id, the target module, and the target
      /// module-local route) awaiting LeaveCompleted after the outgoing dispose.
      Pending: (int * ModuleId * string list) option
      /// The hosted articles child (always present; articles is the root module).
      Articles: A.Model
      /// The hosted blog child, created lazily on its first visit.
      Blog: B.Model option
      /// The single, shell-owned identity authority.
      Identity: Identity.Model }
