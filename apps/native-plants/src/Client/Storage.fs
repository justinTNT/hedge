module Client.Storage

/// The device's offline store for this app: cached catalogue reads, kept photographs and, later, the
/// outbox. One IndexedDB database per origin; memory where storage is unavailable, so nothing depends on it.
let store = Client.Offline.openStore "native-plants"
/// The signed-in identity ("provider:id") whose private reads, queued writes and own photographs are
/// bound to; "" while anonymous. Set by the app as the session changes.
let mutable owner = ""
