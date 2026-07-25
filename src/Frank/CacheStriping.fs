namespace Frank

open Microsoft.Extensions.Caching.Memory

/// #468: a small, FIXED-size (Holzmann 10 — never grows with distinct-key count, unlike a
/// per-key lock dictionary) pool of lock objects used by CacheStriping.getOrBuild below to
/// guarantee build-at-most-once-per-key against an IMemoryCache. Two DIFFERENT keys that
/// happen to hash into the SAME stripe briefly serialize their builds against each other (an
/// accepted, rare false-sharing cost of fixed-size striping, matching the prior hand-rolled
/// cache's own "not a single global lock" design goal) — it never blocks a cache HIT or a
/// build for a different stripe.
type internal StripedLocks(stripeCount: int) =
    do
        if stripeCount <= 0 then
            invalidArg (nameof stripeCount) "stripeCount must be positive"

    let locks: obj[] = Array.init stripeCount (fun _ -> obj ())

    member _.LockFor(hashCode: int) : obj =
        locks.[(hashCode &&& 0x7FFFFFFF) % stripeCount]

/// #468: replaces the hand-rolled generic bounded-cache primitive this module supersedes
/// (Constitution #3/#4 — compose with ASP.NET Core's own IMemoryCache instead of a custom
/// bounded-cache type).
/// IMemoryCache's own `CacheExtensions.GetOrCreate` has NO stampede protection under
/// concurrent racers on the SAME missing key: each racer's factory runs independently and
/// the last committer's entry wins, so two concurrent callers can each build (and each
/// observe a DIFFERENT built value) — verified empirically against the real net10.0
/// Microsoft.Extensions.Caching.Memory assembly (50 concurrent racers on one missing key
/// produced ~10 separate factory invocations and ~10 distinct returned values with plain
/// GetOrCreate). getOrBuild below closes that gap via double-checked locking against a
/// StripedLocks pool; IMemoryCache's own SizeLimit / per-entry Size / Compact still own ALL
/// size-bounding and eviction (approximate LRU via its own recency-ordered compaction) —
/// this module adds ONLY the missing single-flight guarantee, no capacity bookkeeping of
/// its own.
module internal CacheStriping =
    [<Literal>]
    let DefaultStripeCount = 64

    /// Get `key`'s cached value from `cache`, or build it via `build` (run at most once per
    /// key, even under concurrent racing callers) and insert it with `.SetSize(1)` so
    /// `cache`'s own SizeLimit governs total retained entries. `key` is boxed as the
    /// IMemoryCache key — its own Equals/GetHashCode decide identity (e.g. a caller folding
    /// reference identity into part of a compound key must override those itself).
    let getOrBuild<'K, 'T when 'K: equality>
        (locks: StripedLocks)
        (cache: IMemoryCache)
        (key: 'K)
        (build: unit -> 'T)
        : 'T =
        match cache.TryGetValue(box key) with
        | true, (v: obj) -> v :?> 'T
        | false, _ ->
            lock (locks.LockFor(key.GetHashCode())) (fun () ->
                match cache.TryGetValue(box key) with
                | true, (v: obj) -> v :?> 'T
                | false, _ ->
                    let result = build ()
                    use entry = cache.CreateEntry(box key)
                    entry.SetSize(1L) |> ignore
                    entry.Value <- box result
                    result)
