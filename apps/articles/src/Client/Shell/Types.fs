module Articles.Client.Shell.Types

// The Justat shell's own Elmish types (unified shell, Stage 1). The shell owns one
// router + identity + chrome, and hosts the articles content module via its Stage-0
// hosted surface. Stage 1 hosts articles only; blog keeps its separate bundle. Blog is
// added to this state at Stage 2 (a second child + BlogMsg).

module A = Articles.Client.Types

type Msg =
    /// The shell's single router changed the path (already stripped to content segments).
    | UrlChanged of segments: string list
    /// A message from the hosted articles child.
    | ArticlesMsg of A.Msg
    /// A message from the shell-owned identity subsystem.
    | IdentityMsg of Identity.Msg

type Model =
    { /// Content route the shell is showing (module-local segments).
      Route: string list
      /// The hosted articles child model.
      Articles: A.Model
      /// The single, shell-owned identity authority.
      Identity: Identity.Model }
