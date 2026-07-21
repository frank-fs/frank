namespace Frank

open System.Collections.Concurrent
open System.Threading

/// Thread-safe, size-bounded cache: build-at-most-once-per-key (matching the unbounded
/// `ConcurrentDictionary<'K, Lazy<'V>>` pattern DiscoveryMiddleware's
/// resolvedAlpsCache/resolvedHomeResourcesCache (#398), LinkedDataMiddleware's
/// cachedStaticBody (#382), and ValidationMiddleware's hostRelativeShapesCache (#422) all
/// used before this fix), PLUS a hard ceiling on distinct keys retained (#405: an
/// unauthenticated client varying its `Host` header can otherwise mint unbounded permanent
/// cache entries, one per distinct origin string — a resource-exhaustion vector requiring
/// no authentication).
///
/// Eviction is true LRU (least-recently-used, touch-on-READ as well as touch-on-insert) —
/// NOT FIFO (#422 Finding B: FIFO evicts the oldest-INSERTED key regardless of recent use,
/// which under a SUSTAINED Host-header flood combined with real traffic evicts exactly the
/// legitimate, early-registered deployment origins first — inverting the guarantee this
/// cache exists to provide). Under LRU, a small, stable set of legitimate deployment
/// origins (1-3) that keep getting re-accessed by real traffic stays "recently used"
/// indefinitely and is never evicted by a flood of one-off attacker keys, however long the
/// flood runs — the existing build-once-per-origin memoization benefit genuinely holds
/// under the combined scenario, not just the simple monotonic-insert case. A key that is
/// itself never re-accessed (legitimate or not) is still evicted once the flood pushes past
/// `capacity`, exactly like FIFO — this bounds memory, it does not pin entries forever.
///
/// Eviction is a bounded scan over at most `capacity` entries (Holzmann 10: never an
/// unbounded sweep) — acceptable because `capacity` is fixed and small (default 1000) and
/// the scan only runs on the already-rare over-capacity path, never per-request in the
/// common case. Per-key builds still run independently and in parallel across different
/// keys (each key gets its own `Lazy<'V>`, exactly as before bounding) — only the
/// bookkeeping needed to cap total retained keys and track recency is new; legitimate
/// multi-origin throughput is not serialized behind a single global lock.
type internal BoundedCache<'K, 'V when 'K: equality>(capacity: int) =
    do
        if capacity <= 0 then
            invalidArg (nameof capacity) "capacity must be positive"

    let map = ConcurrentDictionary<'K, Lazy<'V>>()
    let lastAccess = ConcurrentDictionary<'K, int64>()
    let mutable clock = 0L

    /// Stamp `key` as just-used with a fresh, monotonically increasing tick — called on
    /// every GetOrAdd (both a fresh insert and a cache hit), which is what makes eviction
    /// LRU rather than FIFO.
    let touch (key: 'K) =
        lastAccess.[key] <- Interlocked.Increment(&clock)

    /// Evict the single least-recently-used key, if any is present — a scan bounded by
    /// `capacity` (Holzmann 10), run only on the already-rare over-capacity path.
    let evictLeastRecentlyUsed () =
        let mutable oldestKey = Unchecked.defaultof<'K>
        let mutable oldestStamp = System.Int64.MaxValue
        let mutable found = false

        for kvp in lastAccess do
            if kvp.Value < oldestStamp then
                oldestStamp <- kvp.Value
                oldestKey <- kvp.Key
                found <- true

        if found then
            map.TryRemove(oldestKey) |> ignore
            lastAccess.TryRemove(oldestKey) |> ignore

    /// Get the existing value for `key`, or build it (via `build`, run at most once per
    /// key) and insert it. Every call — hit or miss — refreshes `key`'s recency. Once more
    /// than `capacity` distinct keys have been inserted, each further insertion evicts the
    /// single least-recently-used key first — a bounded, O(1)-common-case cap.
    member _.GetOrAdd(key: 'K, build: unit -> 'V) : 'V =
        let mutable wasNew = false

        let lazyValue =
            map.GetOrAdd(
                key,
                (fun _ ->
                    wasNew <- true
                    Lazy<'V>(build))
            )

        touch key

        if wasNew && map.Count > capacity then
            evictLeastRecentlyUsed ()

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
