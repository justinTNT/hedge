module Client.Offline

// The offline runtime shared by Hedge clients, written against Hedge.Http alone: a durable Store, a
// read-through cache as a Transport decorator (reads stay transparent to the generated client), an
// Outbox the app enqueues into explicitly (a queued write is neither failed nor done, and the UI has to
// know that at submit time), and media keeping/prefetch. The app owns policy: which requests it routes
// through the cache and under what scope, what each replay outcome means, which media it keeps and
// when to flush. A service worker would not do: CapacitorHttp routes fetch natively past any worker
// and the private preview marks every response no-store. See notes/HEDGE-offline.md.

open System.Collections.Generic
open Fable.Core
open Fable.Core.JsInterop
open Hedge.Http

// ============================================================
// Store
// ============================================================

/// A durable key-value store: string or Blob values under opaque, prefix-listable keys. Any operation
/// may reject (storage blocked, private mode, quota); callers that must keep working treat that as a
/// miss, which `tryGet`/`tryPut` below do.
type Store =
    { Get: string -> JS.Promise<obj option>
      Put: string -> obj -> JS.Promise<unit>
      Delete: string -> JS.Promise<unit>
      /// Keys with a prefix, sorted.
      Keys: string -> JS.Promise<string list> }

/// An in-memory store: tests, and the fallback where IndexedDB is unavailable.
let memoryStore () : Store =
    let data = Dictionary<string, obj>()
    { Get = fun key -> promise { return (match data.TryGetValue key with | true, v -> Some v | _ -> None) }
      Put = fun key value -> promise { data.[key] <- value; return () }
      Delete = fun key -> promise { data.Remove key |> ignore; return () }
      Keys = fun prefix -> promise { return data.Keys |> Seq.filter (fun k -> k.StartsWith prefix) |> List.ofSeq |> List.sort } }

[<Emit("typeof indexedDB !== 'undefined'")>]
let private hasIndexedDb : bool = jsNative

[<Emit("new Promise((resolve, reject) => { const opening = indexedDB.open($0, 1); opening.onupgradeneeded = () => { opening.result.createObjectStore('kv'); }; opening.onsuccess = () => resolve(opening.result); opening.onerror = () => reject(opening.error); opening.onblocked = () => reject(new Error('IndexedDB blocked')); })")>]
let private openDatabase (name: string) : JS.Promise<obj> = jsNative

/// One operation in its own transaction. The promise resolves only when the transaction commits, so a
/// write reported as kept is durable; a request error or a transaction abort (quota, storage reclaim)
/// rejects, and never hangs.
[<Emit("new Promise((resolve, reject) => { const transaction = $0.transaction('kv', $1); let result; const request = $2(transaction.objectStore('kv')); request.onsuccess = () => { result = request.result; }; request.onerror = () => reject(request.error); transaction.oncomplete = () => resolve(result); transaction.onerror = () => reject(transaction.error || new Error('IndexedDB transaction failed')); transaction.onabort = () => reject(transaction.error || new Error('IndexedDB transaction aborted')); })")>]
let private databaseRequest (database: obj) (mode: string) (operation: obj -> obj) : JS.Promise<obj> = jsNative

[<Emit("IDBKeyRange.bound($0, $0 + '\\uffff')")>]
let private prefixRange (prefix: string) : obj = jsNative

/// An IndexedDB-backed store (one object store, opened on first use). Blobs are stored as Blobs.
let indexedDbStore (name: string) : Store =
    let database = lazy (openDatabase name)
    let run mode operation = promise { let! db = database.Force() in return! databaseRequest db mode operation }
    { Get = fun key -> promise { let! value = run "readonly" (fun kv -> kv?get(key)) in return (if isNull value then None else Some value) }
      Put = fun key value -> promise { let! _ = run "readwrite" (fun kv -> kv?put(value, key)) in return () }
      Delete = fun key -> promise { let! _ = run "readwrite" (fun kv -> kv?delete(key)) in return () }
      Keys = fun prefix -> promise { let! keys = run "readonly" (fun kv -> kv?getAllKeys(prefixRange prefix)) in return (unbox<string[]> keys |> List.ofArray |> List.sort) } }

/// The durable store for this origin when IndexedDB exists, else memory (node, storage switched off).
let openStore (name: string) : Store = if hasIndexedDb then indexedDbStore name else memoryStore ()

/// Ask the browser not to evict this origin's storage under pressure. Best effort; false when refused.
[<Emit("(typeof navigator !== 'undefined' && navigator.storage && navigator.storage.persist ? navigator.storage.persist() : Promise.resolve(false))")>]
let persist () : JS.Promise<bool> = jsNative

/// Store operations that must never take the app down: a failure reads as a miss or a no-op.
let private tryGet (store: Store) key = promise { try return! store.Get key with _ -> return None }
let private tryPut (store: Store) key value = promise { try do! store.Put key value with _ -> () }

[<Emit("Date.now()")>]
let private now () : float = jsNative

[<Emit("Promise.all($0)")>]
let private all (promises: JS.Promise<'T>[]) : JS.Promise<'T[]> = jsNative

let private pairsToJs (pairs: (string * string) list) = pairs |> List.map (fun (k, v) -> [| k; v |]) |> List.toArray
let private pairsOfJs (value: obj) = (unbox<string[][]> value) |> Array.map (fun p -> p.[0], p.[1]) |> List.ofArray
let private successful (response: Response) = response.Status >= 200 && response.Status < 300

// ============================================================
// Read cache
// ============================================================

/// Where a read was answered from. FromCache carries when that copy was stored (ms since the epoch).
type Served =
    | FromNetwork
    | FromCache of storedAt: float

/// The cache key of a read: scope (an identity for private reads, "" for public), method, path, query.
let cacheKey (scope: string) (request: Request) =
    let query = match request.Query with [] -> "" | q -> "?" + (q |> List.map (fun (k, v) -> k + "=" + v) |> String.concat "&")
    "read/" + scope + "/" + request.Method + " " + request.Path + query

let private toStored (response: Response) (storedAt: float) : obj =
    createObj [ "status" ==> response.Status; "headers" ==> pairsToJs response.Headers; "body" ==> response.Body; "storedAt" ==> storedAt ]
let private fromStored (stored: obj) : Response * float =
    ({ Status = unbox<int> stored?status; Headers = pairsOfJs stored?headers; Body = unbox<string> stored?body }: Response), unbox<float> stored?storedAt
let private header (name: string) (response: Response) =
    response.Headers |> List.tryPick (fun (k, v) -> if k.ToLowerInvariant() = name then Some v else None)

/// A read-through cache over `inner` for GET requests; everything else passes straight through.
/// Network first: a 2xx is stored and returned; a request that never completed, or a 5xx, is answered
/// from the stored copy when there is one (`onServed` then reports FromCache and the copy's age),
/// otherwise the failure stands. A stored ETag makes the request conditional and a 304 refreshes the
/// copy's age. 4xx responses are never masked: an expired session or a missing record reaches the caller.
let cachedTransport (store: Store) (scope: unit -> string) (onServed: Request -> Served -> unit) (inner: Transport) : Transport =
    // A named function, not a returned lambda: Fable then keeps this at four arguments and the result is
    // a plain one-argument transport, which JS callers (tests, runtime glue) can hold and call directly.
    let read (request: Request) = promise {
        if request.Method <> "GET" then return! inner request
        else
            let key = cacheKey (scope ()) request
            let! stored = tryGet store key
            let cached = stored |> Option.map fromStored
            let conditional =
                match cached |> Option.bind (fun (copy, _) -> header "etag" copy) with
                | Some etag -> { request with Headers = ("If-None-Match", etag) :: request.Headers }
                | None -> request
            let! outcome = inner conditional
            match outcome, cached with
            | Ok response, _ when successful response ->
                do! tryPut store key (toStored response (now ()))
                onServed request FromNetwork
                return Ok response
            | Ok response, Some (copy, _) when response.Status = 304 ->
                do! tryPut store key (toStored copy (now ()))
                onServed request FromNetwork
                return Ok copy
            | Error (TransportFailure _), Some (copy, storedAt) ->
                onServed request (FromCache storedAt)
                return Ok copy
            | Ok response, Some (copy, storedAt) when response.Status >= 500 ->
                onServed request (FromCache storedAt)
                return Ok copy
            | result, _ -> return result
    }
    read

/// Every stored read under a scope whose path starts with `pathPrefix`: its path (with query), the
/// stored response and when it was stored. Lets an app derive work from what the cache already holds
/// (a media pack from the saved accounts) without knowing the storage format.
let cachedResponses (store: Store) (scope: string) (pathPrefix: string) : JS.Promise<(string * Response * float) list> =
    let lead = "read/" + scope + "/GET "
    promise {
        let! keys = promise { try return! store.Keys (lead + pathPrefix) with _ -> return [] }
        let! loaded = all [| for key in keys -> promise { let! value = tryGet store key in return key, value } |]
        return [ for (key, value) in loaded do
                   match value with
                   | Some stored ->
                       let response, storedAt = fromStored stored
                       yield key.Substring lead.Length, response, storedAt
                   | None -> () ]
    }

/// Store a response for a read as if the network had just answered it: one bulk fetch can prime the
/// cache for the many single reads the pages make, and a cache-serving transport then finds them.
let primeRead (store: Store) (scope: string) (request: Request) (response: Response) : JS.Promise<unit> =
    tryPut store (cacheKey scope request) (toStored response (now ()))

/// Cache first: a stored copy is answered at once (FromCache); otherwise the network, stored on 2xx.
let cacheFirstTransport (store: Store) (scope: unit -> string) (onServed: Request -> Served -> unit) (inner: Transport) : Transport =
    let networkFirst = cachedTransport store scope onServed inner
    fun request -> promise {
        if request.Method <> "GET" then return! inner request
        else
            let! stored = tryGet store (cacheKey (scope ()) request)
            match stored |> Option.map fromStored with
            | Some (copy, storedAt) ->
                onServed request (FromCache storedAt)
                return Ok copy
            | None -> return! networkFirst request
    }

/// Read each request through a plain transport, `concurrency` at a time, and store every answer the
/// network gives under `scope`. The count of those comes back, so a count equal to the request count
/// means every read was confirmed for the current content: a copy the cache would have served instead
/// is never mistaken for confirmation. Failures are counted out, never raised.
let warm (store: Store) (scope: string) (transport: Transport) (requests: Request list) (concurrency: int) : JS.Promise<int> =
    let queue = ResizeArray<Request>(requests)
    let completed = ref 0
    let rec worker () = promise {
        if queue.Count > 0 then
            let request = queue.[0]
            queue.RemoveAt 0
            let! outcome = promise { try return! transport request with ex -> return Error (TransportFailure ex.Message) }
            match outcome with
            | Ok response when successful response ->
                do! tryPut store (cacheKey scope request) (toStored response (now ()))
                completed.Value <- completed.Value + 1
            | _ -> ()
            return! worker ()
    }
    promise {
        let! _ = all [| for _ in 1 .. max 1 concurrency -> worker () |]
        return completed.Value
    }

// ============================================================
// Outbox
// ============================================================

/// A write made without connectivity: the wire request, its attachments (store keys holding Blobs the
/// app's sender uploads), and its delivery history. Bound to the scope (identity) that made it.
type OutboxItem =
    { Id: string
      Scope: string
      Request: Request
      Attachments: string list
      Attempts: int
      CreatedAt: float
      /// Set when the app parked the item: it waits for the user, not for connectivity.
      Parked: string option }

/// What the app makes of a replay outcome. Delivered removes the item; Retry keeps it and ends the pass,
/// so later items wait behind it in order; Park keeps it aside with a reason until the app acts; Discard
/// removes it unsent.
type Decision =
    | Delivered
    | Retry
    | Park of reason: string
    | Discard

/// The reading of an outcome by HTTP alone: 2xx delivered, 5xx or no response retried, any other status
/// parked. An app wraps this to give its own statuses meaning (a 409 quota, a 401 awaiting login).
let defaultDecision (_: OutboxItem) (outcome: Result<Response, ApiError>) : Decision =
    match outcome with
    | Ok response when successful response -> Delivered
    | Ok response when response.Status >= 500 -> Retry
    | Ok response -> Park (sprintf "HTTP %d" response.Status)
    | Error (TransportFailure _) -> Retry
    | Error error -> Park (renderError error)

type Outbox =
    { /// Persist a write for later delivery, with the store keys of its attachments. The item comes
      /// back for the app's pending markers.
      Enqueue: Request -> string list -> JS.Promise<OutboxItem>
      /// This scope's items, oldest first, parked ones included.
      Items: unit -> JS.Promise<OutboxItem list>
      /// Remove an item, and its attachments, without sending it.
      Remove: string -> JS.Promise<unit>
      /// Give an item a new request (the person's resolution of a parked one) and send it afresh.
      Replace: string -> Request -> JS.Promise<unit>
      /// Put every parked item of this scope back in line for the next flush.
      Release: unit -> JS.Promise<unit>
      /// Replay this scope's pending items in order through the sender; what remains comes back.
      /// A flush while one is running joins it.
      Flush: unit -> JS.Promise<OutboxItem list> }

[<Emit("crypto.randomUUID()")>]
let private uuid () : string = jsNative
[<Emit("String(Math.floor($0)).padStart($1, '0')")>]
let private padded (value: float) (width: int) : string = jsNative

let private itemKey (scope: string) (sequence: string) = "outbox/" + scope + "/" + sequence
let private toStoredItem (item: OutboxItem) : obj =
    createObj [ "id" ==> item.Id; "scope" ==> item.Scope; "verb" ==> item.Request.Method; "path" ==> item.Request.Path
                "query" ==> pairsToJs item.Request.Query; "headers" ==> pairsToJs item.Request.Headers
                "body" ==> (match item.Request.Body with Some b -> box b | None -> null)
                "attachments" ==> List.toArray item.Attachments
                "attempts" ==> item.Attempts; "createdAt" ==> item.CreatedAt
                "parked" ==> (match item.Parked with Some p -> box p | None -> null) ]
let private optional (value: obj) = if isNull value then None else Some (unbox<string> value)
let private fromStoredItem (stored: obj) : OutboxItem =
    { Id = unbox stored?id
      Scope = unbox stored?scope
      Request = { Method = unbox stored?verb; Path = unbox stored?path; Query = pairsOfJs stored?query; Headers = pairsOfJs stored?headers; Body = optional stored?body }
      Attachments = (unbox<string[]> stored?attachments) |> List.ofArray
      Attempts = unbox stored?attempts
      CreatedAt = unbox stored?createdAt
      Parked = optional stored?parked }

/// An outbox over a store. `send` performs one item (the plain case is `fun item -> transport item.Request`;
/// an item with attachments is the app's multipart upload); `decide` reads the outcome; `onDelivered`
/// hears each success before the item and its attachments are removed, so an app can keep what it
/// needs (a sent photograph staying on the phone as the person's own copy).
let createOutbox (store: Store) (scope: unit -> string) (send: OutboxItem -> JS.Promise<Result<Response, ApiError>>)
                 (decide: OutboxItem -> Result<Response, ApiError> -> Decision) (onDelivered: OutboxItem -> Response -> JS.Promise<unit>) : Outbox =
    let sequence = ref 0
    let running : JS.Promise<OutboxItem list> option ref = ref None
    let keyed () = promise {
        let! keys = store.Keys (itemKey (scope ()) "")
        let! loaded = all [| for key in keys -> promise { let! value = store.Get key in return key, value } |]
        return [ for (key, value) in loaded do match value with Some v -> yield key, fromStoredItem v | None -> () ]
    }
    let items () = promise { let! pairs = keyed () in return pairs |> List.map snd }
    let drop key (item: OutboxItem) = promise {
        do! store.Delete key
        for attachment in item.Attachments do do! store.Delete attachment
    }
    let replace id request = promise {
        let! pairs = keyed ()
        match pairs |> List.tryFind (fun (_, item) -> item.Id = id) with
        | Some (key, item) -> do! store.Put key (toStoredItem { item with Request = request; Parked = None; Attempts = 0 })
        | None -> ()
    }
    let release () = promise {
        let! pairs = keyed ()
        for (key, item) in pairs do
            if item.Parked.IsSome then do! store.Put key (toStoredItem { item with Parked = None })
    }
    let remove id = promise {
        let! pairs = keyed ()
        match pairs |> List.tryFind (fun (_, item) -> item.Id = id) with
        | Some (key, item) -> do! drop key item
        | None -> ()
    }
    let again = ref false
    let pass () = promise {
        let owner = scope ()
        let! pending = keyed ()
        let blocked = ref false
        for (key, item) in pending do
            // The pass belongs to the identity it started under: an account switch stops it before another
            // send, so nothing of one person's is ever sent, parked or kept under another's credential.
            if scope () <> owner then blocked.Value <- true
            if not blocked.Value && item.Parked.IsNone then
                let! outcome = promise { try return! send item with ex -> return Error (TransportFailure ex.Message) }
                match decide item outcome with
                | Delivered ->
                    match outcome with
                    | Ok response -> do! (promise { try do! onDelivered item response with _ -> () })
                    | Error _ -> ()
                    do! drop key item
                | Retry ->
                    do! store.Put key (toStoredItem { item with Attempts = item.Attempts + 1 })
                    blocked.Value <- true
                | Park reason ->
                    do! store.Put key (toStoredItem { item with Attempts = item.Attempts + 1; Parked = Some reason })
                | Discard -> do! drop key item
        return! items ()
    }
    let rec flush () =
        match running.Value with
        | Some current ->
            again.Value <- true
            current
        | None ->
            let current = promise {
                try return! pass ()
                finally
                    running.Value <- None
                    // Something queued, or asked for, during the pass gets one more pass now rather than
                    // waiting for the next trigger.
                    if again.Value then
                        again.Value <- false
                        flush () |> ignore }
            running.Value <- Some current
            current
    let enqueue request attachments = promise {
        sequence.Value <- sequence.Value + 1
        let item = { Id = uuid (); Scope = scope (); Request = request; Attachments = attachments; Attempts = 0; CreatedAt = now (); Parked = None }
        do! store.Put (itemKey item.Scope (padded item.CreatedAt 15 + "-" + padded (float sequence.Value) 6)) (toStoredItem item)
        return item
    }
    { Enqueue = enqueue; Items = items; Remove = remove; Replace = replace; Release = release; Flush = flush }

// ============================================================
// Media
// ============================================================

let private mediaKey (url: string) = "media/" + url
let private privateKey (scope: string) (url: string) = "private/" + scope + "/" + url

/// The kept Blob for a media URL, if any.
let mediaBlob (store: Store) (url: string) : JS.Promise<obj option> = tryGet store (mediaKey url)
/// Keep a media Blob under its URL. Content-hashed URLs never go stale; others are the app's to refresh.
let keepMedia (store: Store) (url: string) (blob: obj) : JS.Promise<unit> = tryPut store (mediaKey url) blob
let forgetMedia (store: Store) (url: string) : JS.Promise<unit> = promise { try do! store.Delete (mediaKey url) with _ -> () }
/// Every kept media URL.
let keptMedia (store: Store) : JS.Promise<string list> =
    promise { let! keys = store.Keys "media/" in return keys |> List.map (fun key -> key.Substring 6) }

/// Media bound to an identity (a person's own photographs): kept and found under their scope alone, so
/// another account on the same device never sees them. Untouched by the public media helpers above.
let privateMediaBlob (store: Store) (scope: string) (url: string) : JS.Promise<obj option> = tryGet store (privateKey scope url)
let keepPrivateMedia (store: Store) (scope: string) (url: string) (blob: obj) : JS.Promise<unit> = tryPut store (privateKey scope url) blob
let keptPrivateMedia (store: Store) (scope: string) : JS.Promise<string list> =
    let lead = privateKey scope ""
    promise { let! keys = store.Keys lead in return keys |> List.map (fun key -> key.Substring lead.Length) }
let forgetPrivateMedia (store: Store) (scope: string) : JS.Promise<unit> = promise {
    let! keys = store.Keys (privateKey scope "")
    for key in keys do do! (promise { try do! store.Delete key with _ -> () })
}

type MediaProgress = { Total: int; Kept: int; Failed: int }

let private fetchMediaWith (keyOf: string -> string) (store: Store) (fetchBlob: string -> JS.Promise<obj>) (urls: string list) (concurrency: int) (onProgress: MediaProgress -> unit) (proceed: unit -> bool) : JS.Promise<MediaProgress> =
    let queue = ResizeArray<string>(urls)
    let progress = ref { Total = urls.Length; Kept = 0; Failed = 0 }
    let kept () = progress.Value <- { progress.Value with Kept = progress.Value.Kept + 1 }
    let rec worker () = promise {
        if queue.Count > 0 && proceed () then
            let url = queue.[0]
            queue.RemoveAt 0
            let! present = tryGet store (keyOf url)
            match present with
            | Some _ -> kept ()
            | None ->
                try
                    let! blob = fetchBlob url
                    do! store.Put (keyOf url) blob
                    kept ()
                with _ -> progress.Value <- { progress.Value with Failed = progress.Value.Failed + 1 }
            onProgress progress.Value
            return! worker ()
    }
    promise {
        let! _ = all [| for _ in 1 .. max 1 concurrency -> worker () |]
        return progress.Value
    }

/// Fetch and keep the media not already present, `concurrency` at a time, reporting after each URL,
/// until done or `proceed` says stop (URLs in flight finish). A failed URL is counted and skipped, so an
/// interrupted pack resumes where it stopped on the next call.
let fetchMedia (store: Store) (fetchBlob: string -> JS.Promise<obj>) (urls: string list) (concurrency: int) (onProgress: MediaProgress -> unit) (proceed: unit -> bool) : JS.Promise<MediaProgress> =
    fetchMediaWith mediaKey store fetchBlob urls concurrency onProgress proceed
/// As `fetchMedia`, into an identity's private media.
let fetchPrivateMedia (store: Store) (scope: string) (fetchBlob: string -> JS.Promise<obj>) (urls: string list) (concurrency: int) (onProgress: MediaProgress -> unit) (proceed: unit -> bool) : JS.Promise<MediaProgress> =
    fetchMediaWith (privateKey scope) store fetchBlob urls concurrency onProgress proceed

[<Emit("URL.createObjectURL($0)")>]
let objectUrl (blob: obj) : string = jsNative

/// Bytes this origin is using, by the browser's estimate; 0 where unsupported.
[<Emit("(typeof navigator !== 'undefined' && navigator.storage && navigator.storage.estimate ? navigator.storage.estimate().then(e => e.usage || 0) : Promise.resolve(0))")>]
let storageUsage () : JS.Promise<float> = jsNative
/// The network type when the platform tells it ("wifi", "cellular", "ethernet", "none", …), else "".
[<Emit("(typeof navigator !== 'undefined' && navigator.connection && navigator.connection.type) || ''")>]
let connectionType () : string = jsNative
/// False only when the platform says the device is offline.
[<Emit("typeof navigator === 'undefined' || navigator.onLine !== false")>]
let isOnline () : bool = jsNative
[<Emit("URL.revokeObjectURL($0)")>]
let releaseObjectUrl (url: string) : unit = jsNative

// ============================================================
// Connectivity
// ============================================================

/// Call back when connectivity may be back: the window comes online or the page becomes visible again
/// (a Capacitor resume raises the latter in the WebView). Returns the remover.
[<Emit("(() => { const handler = () => { if (!document.hidden) $0(); }; window.addEventListener('online', handler); document.addEventListener('visibilitychange', handler); return () => { window.removeEventListener('online', handler); document.removeEventListener('visibilitychange', handler); }; })()")>]
let onReconnect (callback: unit -> unit) : unit -> unit = jsNative
