namespace Frank

open System.Collections.Concurrent
open System.Threading

/// Thread-safe, size-bounded cache: build-at-most-once-per-key (matching the unbounded
/// `ConcurrentDictionary<'K, Lazy<'V>>` pattern both DiscoveryMiddleware's
/// resolvedAlpsCache/resolvedHomeResourcesCache (#398) and LinkedDataMiddleware's
/// cachedStaticBody (#382) used before this fix), PLUS a hard ceiling on distinct keys
/// retained (#405: an unauthenticated client varying its `Host` header can otherwise mint
/// unbounded permanent cache entries, one per distinct origin string — a resource-exhaustion
/// vector requiring no authentication).
///
/// Eviction is FIFO (oldest-inserted-first), not LRU (no recency touch on read) — the
/// simplest bounded strategy that satisfies the actual requirement (a hard ceiling on
/// retained memory, verified by a flood test) without misrepresenting stronger recency
/// semantics this cache doesn't implement. For the realistic case this defends — a small,
/// stable set of legitimate deployment origins (1-3) vastly under `capacity` — no eviction
/// ever happens among them, so the existing build-once-per-origin memoization benefit is
/// completely unaffected; eviction only engages once a flood of DISTINCT keys exceeds
/// `capacity`, which is exactly the attack this closes.
///
/// Per-key builds still run independently and in parallel across different keys (each key
/// gets its own `Lazy<'V>`, exactly as before bounding) — only the bookkeeping needed to
/// cap total retained keys is new; legitimate multi-origin throughput is not serialized
/// behind a single global lock.
type internal BoundedCache<'K, 'V when 'K: equality>(capacity: int) =
    do
        if capacity <= 0 then
            invalidArg (nameof capacity) "capacity must be positive"

    let map = ConcurrentDictionary<'K, Lazy<'V>>()
    let order = ConcurrentQueue<'K>()
    let mutable count = 0

    /// Get the existing value for `key`, or build it (via `build`, run at most once per
    /// key) and insert it. Once more than `capacity` distinct keys have been inserted,
    /// each further insertion evicts the single oldest-inserted key first — a bounded,
    /// O(1)-per-insertion cap (Holzmann 10: eviction is capped to "one dequeue per
    /// over-capacity insertion", never an unbounded sweep).
    member _.GetOrAdd(key: 'K, build: unit -> 'V) : 'V =
        let mutable wasNew = false

        let lazyValue =
            map.GetOrAdd(
                key,
                (fun _ ->
                    wasNew <- true
                    Lazy<'V>(build))
            )

        if wasNew then
            order.Enqueue key
            let newCount = Interlocked.Increment(&count)

            if newCount > capacity then
                match order.TryDequeue() with
                | true, oldest ->
                    if map.TryRemove(oldest) |> fst then
                        Interlocked.Decrement(&count) |> ignore
                | false, _ -> ()

        lazyValue.Value

    /// Test-only visibility: current (approximate under concurrent races — see GetOrAdd)
    /// number of distinct keys retained. Never exceeds `capacity` under sequential access.
    member _.Count = map.Count

/// Default capacity shared by both middlewares' origin-keyed caches (#405) — large enough
/// that no realistic deployment's legitimate host set is ever evicted (a real deployment
/// serves a handful of distinct origins, not thousands), small enough to bound worst-case
/// retained memory under a Host-header flood to a fixed, known ceiling.
module internal BoundedCache =
    [<Literal>]
    let DefaultCapacity = 1000
