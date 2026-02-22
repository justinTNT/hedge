module Models.Config

/// Global configuration embedded per-host.
/// Passed to the client as init data.
type GlobalConfig = {
    SiteName: string
    Features: FeatureFlags
}

and FeatureFlags = {
    Comments: bool
    Submissions: bool
    Tags: bool
}
