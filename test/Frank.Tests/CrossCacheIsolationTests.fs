/// #468 AT1: all four keyed IMemoryCache regions, wired together via the SAME
/// Frank.Builder.registerBoundedMemoryCaches production registration path WebHostBuilder.Run
/// itself calls (mirroring real app composition, not four separately hand-rolled
/// MemoryCache instances) — flood ONE cache with 10,000+ distinct keys while the other
/// three are pre-seeded with a small set of legitimate keys and otherwise idle, then prove:
/// (a) the flooded cache's size — read as (cache :?> MemoryCache).Count, never a substitute
/// counter — is > 0 and <= CacheCapacity (a stronger bound than AT1's own 110% allowance);
/// (b) the other three caches' pre-seeded legitimate keys are all still retrievable without
/// rebuild, proving the flood didn't starve them (each keyed registration is an
/// independently-budgeted MemoryCache instance, not a shared pool). Repeated with each of
/// the four caches as the flooded one, in turn — the ONE place this cross-cache guarantee is
/// tested; the per-middleware CacheBoundTests.fs suites already cover single-cache
/// flood-plateau behavior through real HTTP-context-driven middleware invocation.
module Frank.Tests.CrossCacheIsolationTests

open System
open Microsoft.Extensions.Caching.Memory
open Microsoft.Extensions.DependencyInjection
open Expecto
open Frank
open Frank.Builder

let private cacheKeys =
    [ "discovery:alps"
      "discovery:home"
      "validation:shapes"
      "linkeddata:staticbody" ]

/// A fresh service provider wired via the SAME production registration function
/// WebHostBuilder.Run calls on both TFM branches — never a hand-rolled `new MemoryCache(...)`
/// per test.
let private newServiceProvider () : IServiceProvider =
    let services = ServiceCollection()
    registerBoundedMemoryCaches services |> ignore
    services.BuildServiceProvider()

let private cacheFor (sp: IServiceProvider) (key: string) : IMemoryCache =
    sp.GetRequiredKeyedService<IMemoryCache>(key)

/// Pre-seed `cache` with 3 legitimate keys via the SAME CacheStriping.getOrBuild path the
/// real middlewares use (`.SetSize(1)` per entry) — never a bypass insert.
let private preSeedLegitKeys (cache: IMemoryCache) : Set<string> =
    let locks = StripedLocks(CacheStriping.DefaultStripeCount)
    let legit = [ "legit-a"; "legit-b"; "legit-c" ]

    for key in legit do
        CacheStriping.getOrBuild locks cache key (fun () -> key) |> ignore

    Set.ofList legit

let private floodDistinctKeys (cache: IMemoryCache) (count: int) : unit =
    let locks = StripedLocks(CacheStriping.DefaultStripeCount)

    for i in 1..count do
        CacheStriping.getOrBuild locks cache (sprintf "flood-%d" i) (fun () -> string i)
        |> ignore

[<Tests>]
let tests =
    testList
        "Cross-cache isolation under a single-cache flood (#468 AT1)"
        [ for floodedKey in cacheKeys ->
              testCase $"flooding '{floodedKey}' plateaus it while the other three caches' legit keys survive untouched"
              <| fun _ ->
                  let sp = newServiceProvider ()
                  let allCaches = cacheKeys |> List.map (fun k -> k, cacheFor sp k)

                  let seededLegitKeys =
                      allCaches
                      |> List.filter (fun (k, _) -> k <> floodedKey)
                      |> List.map (fun (k, cache) -> k, cache, preSeedLegitKeys cache)

                  let floodedCache = allCaches |> List.find (fun (k, _) -> k = floodedKey) |> snd
                  floodDistinctKeys floodedCache 10_000

                  let floodedSize = (floodedCache :?> MemoryCache).Count

                  Expect.isGreaterThan
                      floodedSize
                      0
                      $"'{floodedKey}': flooded cache must retain SOME entries, not be accidentally empty"

                  Expect.isLessThanOrEqual
                      floodedSize
                      CacheCapacity
                      $"'{floodedKey}': 10,000 distinct keys must not grow the flooded cache past its configured hard ceiling"

                  for otherKey, otherCache, legitKeys in seededLegitKeys do
                      let locks = StripedLocks(CacheStriping.DefaultStripeCount)
                      let mutable rebuiltAny = false

                      for legitKey in legitKeys do
                          CacheStriping.getOrBuild locks otherCache legitKey (fun () ->
                              rebuiltAny <- true
                              legitKey)
                          |> ignore

                      Expect.isFalse
                          rebuiltAny
                          $"'{otherKey}': its pre-seeded legit keys must still be cached (no rebuild) after flooding '{floodedKey}' — a flood against one cache must never evict another's entries" ]
