module Models.Kv

/// KV store value models.
/// These define the shape of data stored in Cloudflare KV.
///
/// Port of: horatio/models/Kv/*.elm

/// User profile cached in KV for fast lookups.
type UserProfile = {
    Id: string
    Name: string
    Email: string
}

/// User session stored in KV. References a UserProfile.
type UserSession = {
    Profile: UserProfile
    LoginTime: int
    Permissions: string list
    Ttl: int
}

/// Generic test cache entry.
type TestCache = {
    Key: string
    Data: string
    Ttl: int
}
