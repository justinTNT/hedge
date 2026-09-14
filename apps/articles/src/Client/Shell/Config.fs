module Articles.Client.Shell.Config

// Static route/navigation configuration for the Justat shell (unified shell, Stage 1).
//
// Justat composes {identity, articles, blog} (gen-modules.json). The shell HOSTS
// articles at the root and links to blog as a separately-bundled sibling document at
// /blog (a real navigation, not an in-SPA route); Stage 2 folds blog into the shell.
//
// This is a compile-time fact, NOT derived from SITE_FEATURES / a runtime flag — the
// navigation entries the shell renders are fixed for this build. test.sh asserts this
// set agrees with gen-modules.json + the Client.fsproj conditional imports (the shell is
// compiled for every site EXCEPT ndct, which stays articles-standalone).

/// Content modules this shell hosts in-SPA. Stage 1: articles only.
type HostedModule = Articles

let hostedModules : HostedModule list = [ Articles ]

/// Blog is reachable as a separately-bundled sibling document at this path until Stage 2.
let hasBlogSibling = true
let blogSiblingPath = "/blog"
