module Mobile.Domain

// The OPT-IN mobile bearer-session table, in a SEPARATE assembly (`MobileModels`, namespace
// `Mobile`) so it stays opt-in per app: a host gains `mobile_sessions` only by adding the slice
// `{ "identity": true, "assembly": "MobileModels", "namespace": "Mobile" }` to its gen manifest
// (microblog does; articles omits it). Mirrors the GrantModels opt-in pattern and is kept out of
// `Models.Domain` so the shared identity assemblies never define the same module. The generated
// table name derives from the type's short name `MobileSession` -> `mobile_sessions`.

open Hedge.Interface

/// A revocable mobile (Capacitor) bearer session for the browser-OAuth POC. `Id` is the SHA-256 hash
/// of the opaque bearer secret — the secret itself lives ONLY in the device Keychain/Keystore; the
/// server stores the hash, never the secret. `GuestId` is a plain string (no FK, like Grant) naming
/// the guest this session authenticates; anonymous-vs-verified is determined by THAT guest's active
/// identity, never by possession of the session (possession is not proof of a verified identity).
/// `ExpiresAt` is an absolute epoch-seconds expiry — resolution is fail-closed once past it. Never
/// exposed through the generic admin (the host filters it out).
type MobileSession = {
    Id: PrimaryKey<string>
    GuestId: string
    ExpiresAt: int
    CreatedAt: CreateTimestamp
}
