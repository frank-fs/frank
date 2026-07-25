/// #468 AT5: DI wiring, no manual cache construction, observably. `registerBoundedMemoryCaches`
/// is the ONE registration function both WebHostBuilder.Run TFM branches call (`#if
/// NET10_0_OR_GREATER` and the `#else` branch, src/Frank/Builder.fs) — verified by direct
/// source inspection since these test projects are net10.0-only (no net8.0/net9.0 build
/// configuration exists here to dynamically exercise the `#else` branch, the same structural
/// carve-out AT4 already accepts for this issue's net10.0-only consumers) — so exercising the
/// function itself once, thoroughly, proves both call sites correct without duplicating the
/// proof per TFM. This file proves: (a) all four keyed IMemoryCache registrations resolve to
/// non-null instances; (b) the SAME key resolves to the SAME reference across repeated
/// resolutions (singleton lifetime, not per-resolution construction); (c) DISTINCT keys
/// resolve to DISTINCT references (independent budgets, not a shared instance). The
/// complementary "two independently-constructed middleware instances observably share cache
/// state" proof lives in Frank.Discovery.Tests (DiCacheSharingTests.fs), which alone has
/// InternalsVisibleTo access to DiscoveryMiddleware's internal build-count test hooks.
module Frank.Tests.CacheDIWiringTests

open System
open Microsoft.Extensions.Caching.Memory
open Microsoft.Extensions.DependencyInjection
open Expecto
open Frank.Builder

let private cacheKeys =
    [ "discovery:alps"
      "discovery:home"
      "validation:shapes"
      "linkeddata:staticbody" ]

let private newServiceProvider () : IServiceProvider =
    let services = ServiceCollection()
    registerBoundedMemoryCaches services |> ignore
    services.BuildServiceProvider()

[<Tests>]
let tests =
    testList
        "Keyed IMemoryCache DI wiring (#468 AT5)"
        [ testCase "all four keyed IMemoryCache registrations resolve to non-null instances"
          <| fun _ ->
              let sp = newServiceProvider ()

              for key in cacheKeys do
                  let cache = sp.GetRequiredKeyedService<IMemoryCache>(key)
                  Expect.isNotNull (box cache) $"'{key}' must resolve to a non-null IMemoryCache"

          testCase "the same key resolves to the SAME IMemoryCache reference across repeated resolutions (singleton)"
          <| fun _ ->
              let sp = newServiceProvider ()

              for key in cacheKeys do
                  let first = sp.GetRequiredKeyedService<IMemoryCache>(key)
                  let second = sp.GetRequiredKeyedService<IMemoryCache>(key)

                  Expect.isTrue
                      (Object.ReferenceEquals(first, second))
                      $"'{key}' resolved twice must yield the SAME instance — DI-managed singleton lifetime, not per-resolution construction"

          testCase "distinct keys resolve to DISTINCT IMemoryCache references (independent budgets, not a shared pool)"
          <| fun _ ->
              let sp = newServiceProvider ()
              let resolved = cacheKeys |> List.map (sp.GetRequiredKeyedService<IMemoryCache>)

              let distinctByReference =
                  resolved
                  |> List.distinctBy (fun c -> System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode c)

              Expect.equal
                  distinctByReference.Length
                  cacheKeys.Length
                  "each of the four keys must resolve to its OWN distinct MemoryCache instance" ]
