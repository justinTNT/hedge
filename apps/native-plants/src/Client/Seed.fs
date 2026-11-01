module Client.Seed

// First launch with no network: the bundle carries the catalogue and the bulk accounts as the worker
// served them at build time (scripts/seed-mobile.mjs). They prime the read cache once, before the first
// load, so every account opens offline from the start. A stale seed is harmless: the first connection's
// revision poll replaces it, and the warm-up records the seed's revision so nothing is refetched twice.

open Fable.Core
open Thoth.Json

[<Emit("fetch($0).then(r => { if (!r.ok) throw new Error('seed missing'); return r.text(); })")>]
let private readText (url: string) : JS.Promise<string> = jsNative
[<Emit("Date.now()")>]
let private now () : float = jsNative

/// Prime the cache from the bundled seed when the device has no saved catalogue yet. True when it did.
let ensure () : JS.Promise<bool> = promise {
    if not (Client.Packs.enabled ()) then return false
    else
        try
            let! cached = Client.Offline.cachedResponses Client.Storage.store "" "/api/plants/catalogue"
            if not cached.IsEmpty then return false
            else
                let! catalogue = readText "seed/catalogue.json"
                let! accounts = readText "seed/accounts.json"
                match Decode.fromString Codecs.Decode.getCatalogueResponse catalogue, Decode.fromString Codecs.Decode.getAccountsResponse accounts with
                | Ok data, Ok bulk ->
                    do! Client.Offline.primeRead Client.Storage.store "" (Client.ClientGen.requests.getCatalogue ()) { Status = 200; Headers = []; Body = catalogue }
                    do! Client.Search.prime bulk
                    if bulk.Revision = data.Revision then
                        do! Client.Storage.store.Put "warm/catalogue" (box data.Revision)
                        do! Client.Storage.store.Put "warm/updated" (box (now ()))
                    return true
                | _ -> return false
        with _ -> return false
}
