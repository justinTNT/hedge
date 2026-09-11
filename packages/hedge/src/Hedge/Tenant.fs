module Hedge.Tenant

// Per-tenant config, delivered to the client at build time: the app's
// vite siteConfig plugin injects window.SITE_* (and stamps body.tenant-<slug>),
// and this reads them into one typed accessor. Per-tenant *behaviour* gates on a
// feature flag (Tenant.hasFeature "x") rather than hardcoded tenant-slug checks.

open Fable.Core

type TenantConfig = {
    Slug: string
    Title: string
    Logo: string
    Features: Set<string>
    /// Optional external info/companion page (e.g. a campaign page on Pages). When
    /// set, the client shows a prominent link to it. Empty = no link.
    InfoUrl: string
    InfoLabel: string
}

[<Emit("(window.SITE_SLUG || '')")>]
let private slug : string = jsNative
[<Emit("(window.SITE_TITLE || '')")>]
let private title : string = jsNative
[<Emit("(window.SITE_LOGO || '')")>]
let private logo : string = jsNative
[<Emit("(window.SITE_FEATURES || '')")>]
let private featuresRaw : string = jsNative
[<Emit("(window.SITE_INFO_URL || '')")>]
let private infoUrl : string = jsNative
[<Emit("(window.SITE_INFO_LABEL || '')")>]
let private infoLabel : string = jsNative

let config : TenantConfig =
    { Slug = slug
      Title = title
      Logo = logo
      InfoUrl = infoUrl
      InfoLabel = infoLabel
      Features =
        featuresRaw.Split(',')
        |> Array.map (fun s -> s.Trim())
        |> Array.filter (fun s -> s <> "")
        |> Set.ofArray }

/// Is a per-tenant feature enabled? (from window.SITE_FEATURES, a comma list)
let hasFeature (feature: string) : bool = config.Features.Contains feature
