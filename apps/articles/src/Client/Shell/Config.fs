module Articles.Client.Shell.Config

// Static route/navigation configuration for the Justat shell (unified shell, Stage 2).
//
// Justat composes {identity, articles, blog} (gen-modules.json). The shell HOSTS both
// content modules in one document — articles at the root, blog at /blog — with in-SPA
// navigation between them (Stage 1 kept blog as a separate bundle; Stage 2 folds it in).
//
// This is a compile-time fact, NOT derived from SITE_FEATURES / a runtime flag — the
// navigation entries the shell renders are fixed for this build. test.sh asserts this
// set agrees with gen-modules.json + the Client.fsproj conditional imports (the shell is
// compiled for every site EXCEPT ndct, which stays articles-standalone).

/// Content modules this shell hosts in-SPA.
type HostedModule =
    | Articles
    | Blog

let hostedModules : HostedModule list = [ Articles; Blog ]

/// The blog module is hosted at this mount within the shell.
let hostsBlog = true
let blogPath = "/blog"
