module Frank.Tests.BoundedCacheTests

open Expecto
open Frank

/// #405: BoundedCache is the shared primitive both DiscoveryMiddleware's resolvedAlpsCache/
/// resolvedHomeResourcesCache and LinkedDataMiddleware's cachedStaticBody now use, closing
/// the unbounded-cache-growth-via-Host-header vector uniformly (Constitution #8: no
/// duplicated logic — one bounded-cache implementation, not two divergent ones).

[<Tests>]
let tests =
    testList
        "BoundedCache (#405)"
        [ testCase "GetOrAdd builds a key at most once, even across repeated calls"
          <| fun _ ->
              let cache = BoundedCache<string, int>(10)
              let mutable buildCount = 0

              let build () =
                  buildCount <- buildCount + 1
                  42

              for _ in 1..5 do
                  cache.GetOrAdd("a", build) |> ignore

              Expect.equal buildCount 1 "same key repeated 5x builds exactly once"

          testCase "GetOrAdd returns the built value"
          <| fun _ ->
              let cache = BoundedCache<string, int>(10)
              let result = cache.GetOrAdd("a", fun () -> 42)
              Expect.equal result 42 "returns the value build() produced"

          testCase "distinct keys under capacity are all retained"
          <| fun _ ->
              let cache = BoundedCache<string, int>(10)

              for i in 1..5 do
                  cache.GetOrAdd(string i, fun () -> i) |> ignore

              Expect.equal cache.Count 5 "5 distinct keys, capacity 10 — none evicted"

          testCase "inserting beyond capacity evicts the oldest key, plateauing at capacity"
          <| fun _ ->
              let cache = BoundedCache<string, int>(3)

              for i in 1..100 do
                  cache.GetOrAdd(string i, fun () -> i) |> ignore

              Expect.equal
                  cache.Count
                  3
                  "100 distinct keys inserted, capacity 3 — count plateaus at capacity, never grows unbounded"

          testCase "a flood of 10,000+ distinct keys plateaus at capacity, not unbounded growth (#405 AC1)"
          <| fun _ ->
              let cache = BoundedCache<string, int>(1000)

              for i in 1..10_000 do
                  cache.GetOrAdd(string i, fun () -> i) |> ignore

              Expect.equal
                  cache.Count
                  1000
                  "10,000 distinct keys ⇒ cache plateaus at its configured capacity (1000), not 10,000"

          testCase "a small set of legitimate keys repeated many times is unaffected by the capacity bound (#405 AC2)"
          <| fun _ ->
              let cache = BoundedCache<string, int>(1000)
              let mutable buildCount = 0

              let build (i: int) () =
                  System.Threading.Interlocked.Increment(&buildCount) |> ignore
                  i

              // 3 legitimate origins, 1000 requests total — build-once-per-origin must hold.
              for _ in 1..1000 do
                  for i in 1..3 do
                      cache.GetOrAdd(string i, build i) |> ignore

              Expect.equal
                  buildCount
                  3
                  "3 distinct legitimate keys, 1000 requests each ⇒ built exactly 3 times total, unaffected by the capacity bound"

              Expect.equal cache.Count 3 "3 keys retained, nowhere near the 1000 capacity"

          testCase "capacity must be positive"
          <| fun _ ->
              Expect.throwsT<System.ArgumentException>
                  (fun () -> BoundedCache<string, int>(0) |> ignore)
                  "capacity 0 is rejected"

              Expect.throwsT<System.ArgumentException>
                  (fun () -> BoundedCache<string, int>(-1) |> ignore)
                  "negative capacity is rejected"

          // #422 Finding B: FIFO (oldest-INSERTED-first) inverts the security guarantee under
          // a SUSTAINED flood combined with real traffic — it evicts the earliest-registered
          // LEGITIMATE origins first, not the attacker's flood entries. True LRU (touch-on-read)
          // fixes this: a key re-accessed after insertion, before the eviction that would
          // otherwise claim it, survives; an untouched key is evicted like any other stale one.
          testCase
              "a key re-accessed after insertion survives eviction over keys inserted after it but never touched again (true LRU, not FIFO)"
          <| fun _ ->
              let cache = BoundedCache<string, int>(3)
              let mutable legitBuildCount = 0

              let buildLegit () =
                  legitBuildCount <- legitBuildCount + 1
                  0

              cache.GetOrAdd("legit", buildLegit) |> ignore // insert 1st; count=1
              cache.GetOrAdd("k1", fun () -> 1) |> ignore // count=2
              cache.GetOrAdd("k2", fun () -> 2) |> ignore // count=3, at capacity

              // Re-access "legit" BEFORE the over-capacity insertion below — under FIFO this
              // has zero effect (no touch-on-read) and "legit", being oldest-INSERTED, would
              // be evicted next; under LRU this refreshes its recency so k1 (now the least
              // recently used) is evicted instead.
              cache.GetOrAdd("legit", buildLegit) |> ignore

              cache.GetOrAdd("k3", fun () -> 3) |> ignore // over capacity -> evicts the LRU key

              cache.GetOrAdd("legit", buildLegit) |> ignore // if "legit" survived, build stays 1

              Expect.equal
                  legitBuildCount
                  1
                  "a key re-accessed just before an eviction must survive it — FIFO would have evicted it as the oldest-inserted key regardless of the re-access"

          testCase
              "a legitimate key NEVER re-accessed during a flood is still eventually evicted, like any other stale entry (LRU still bounds memory, doesn't pin forever)"
          <| fun _ ->
              let cache = BoundedCache<string, int>(3)
              let mutable legitBuildCount = 0

              let buildLegit () =
                  legitBuildCount <- legitBuildCount + 1
                  0

              cache.GetOrAdd("legit", buildLegit) |> ignore // insert once, never touched again

              for i in 1..100 do
                  cache.GetOrAdd(string i, fun () -> i) |> ignore // flood; never re-accesses "legit"

              cache.GetOrAdd("legit", buildLegit) |> ignore // evicted long ago -> rebuilds -> count=2

              Expect.equal
                  legitBuildCount
                  2
                  "a key never re-accessed during a sustained flood must eventually be evicted like any other stale entry — LRU bounds memory the same as FIFO did" ]
