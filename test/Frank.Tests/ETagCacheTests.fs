module Frank.Tests.ETagCacheTests

open System
open Expecto
open Frank
open Microsoft.Extensions.Logging.Abstractions

[<Tests>]
let etagCacheTests =
    testList
        "ETagCache"
        [ testAsync "GetETag returns None for unknown key" {
              use cache = new ETagCache(10, NullLogger<ETagCache>.Instance)
              let! result = cache.GetETag("nonexistent")
              Expect.isNone result "Unknown key should return None"
          }

          testAsync "SetETag then GetETag returns stored value" {
              use cache = new ETagCache(10, NullLogger<ETagCache>.Instance)
              cache.SetETag("key1", "etag1")
              do! Async.Sleep 10
              let! result = cache.GetETag("key1")
              Expect.equal result (Some "etag1") "Should return stored ETag"
          }

          testAsync "Invalidate removes entry" {
              use cache = new ETagCache(10, NullLogger<ETagCache>.Instance)
              cache.SetETag("key1", "etag1")
              do! Async.Sleep 10
              cache.Invalidate("key1")
              do! Async.Sleep 10
              let! result = cache.GetETag("key1")
              Expect.isNone result "Invalidated key should return None"
          }

          testAsync "InvalidateAll clears all entries" {
              use cache = new ETagCache(10, NullLogger<ETagCache>.Instance)
              cache.SetETag("key1", "etag1")
              cache.SetETag("key2", "etag2")
              do! Async.Sleep 10
              cache.InvalidateAll()
              do! Async.Sleep 10
              let! result1 = cache.GetETag("key1")
              let! result2 = cache.GetETag("key2")
              Expect.isNone result1 "key1 should be cleared"
              Expect.isNone result2 "key2 should be cleared"
          }

          testAsync "GetStats reflects hit and miss counts" {
              use cache = new ETagCache(10, NullLogger<ETagCache>.Instance)
              cache.SetETag("key1", "etag1")
              do! Async.Sleep 10
              let! _ = cache.GetETag("key1") // hit
              let! _ = cache.GetETag("key2") // miss
              let! stats = cache.GetStats()
              Expect.equal stats.EntryCount 1 "Should have 1 entry"
              Expect.equal stats.HitCount 1L "Should have 1 hit"
              Expect.equal stats.MissCount 1L "Should have 1 miss"
          }

          testAsync "LRU eviction removes oldest entry when maxEntries exceeded" {
              use cache = new ETagCache(3, NullLogger<ETagCache>.Instance)
              cache.SetETag("key1", "etag1")
              do! Async.Sleep 10
              cache.SetETag("key2", "etag2")
              do! Async.Sleep 10
              cache.SetETag("key3", "etag3")
              do! Async.Sleep 10
              cache.SetETag("key4", "etag4")
              do! Async.Sleep 10

              let! result1 = cache.GetETag("key1")
              let! result2 = cache.GetETag("key2")
              let! result4 = cache.GetETag("key4")

              Expect.isNone result1 "key1 (oldest) should be evicted"
              Expect.isSome result2 "key2 should remain"
              Expect.isSome result4 "key4 should remain"
          }

          testAsync "LRU: accessing entry updates LastAccessed and survives eviction" {
              use cache = new ETagCache(3, NullLogger<ETagCache>.Instance)
              cache.SetETag("key1", "etag1")
              do! Async.Sleep 10
              cache.SetETag("key2", "etag2")
              do! Async.Sleep 10
              cache.SetETag("key3", "etag3")
              do! Async.Sleep 10
              // Access key1 to update its LastAccessed
              let! _ = cache.GetETag("key1")
              do! Async.Sleep 10
              // Adding key4 should evict key2 (now oldest), not key1
              cache.SetETag("key4", "etag4")
              do! Async.Sleep 10

              let! result1 = cache.GetETag("key1")
              let! result2 = cache.GetETag("key2")
              let! result4 = cache.GetETag("key4")

              Expect.isSome result1 "key1 should survive (recently accessed)"
              Expect.isNone result2 "key2 (oldest) should be evicted"
              Expect.isSome result4 "key4 should remain"
          }

          test "Dispose without error" {
              let cache = new ETagCache(10, NullLogger<ETagCache>.Instance)
              (cache :> IDisposable).Dispose()
          }

          testAsync "Concurrent SetETag and GetETag" {
              use cache = new ETagCache(100, NullLogger<ETagCache>.Instance)

              let setTasks =
                  [| for i in 1..20 do
                         async { cache.SetETag(sprintf "key%d" i, sprintf "etag%d" i) } |]

              do! setTasks |> Async.Parallel |> Async.Ignore
              do! Async.Sleep 50

              let getTasks =
                  [| for i in 1..20 do
                         async {
                             let! result = cache.GetETag(sprintf "key%d" i)
                             return result
                         } |]

              let! results = getTasks |> Async.Parallel

              for i in 0 .. results.Length - 1 do
                  Expect.isSome results.[i] (sprintf "key%d should exist" (i + 1))
          } ]
