/// #468 expert-review [FOWLER-MINOR]: LinkedDataMiddleware's staticBodyCache is now ONE
/// shared IMemoryCache region (SizeLimit = Frank.Builder.CacheCapacity) across EVERY
/// LinkedDataConfig a middleware instance serves — before #468, ConditionalWeakTable gave
/// each LinkedDataConfig its OWN inner BoundedCache with an independent per-config budget.
/// This was a deliberate, already-reviewed #468 design decision (dynamic per-config keyed-DI
/// registration isn't feasible — LinkedDataConfig instances aren't known until each
/// `resource {}` CE block registers an endpoint, after Builder.fs's static
/// AddKeyedSingleton registrations already ran), not something to redesign here — but it
/// needs to be a PROVEN, explicit trade-off rather than a doc-comment-only aside. This file
/// proves both halves:
///   (a) the budget IS shared: flooding one config's origin-space can evict entries
///       belonging to a DIFFERENT config once the combined distinct-key count exceeds the
///       one shared capacity.
///   (b) VALUE isolation is NOT compromised despite the shared budget: config A's cached
///       body for origin X is never returned for config B's request to the SAME origin X —
///       the compound key's ReferenceEquals-on-Config check (StaticBodyCacheKey,
///       LinkedDataMiddleware.fs) still prevents cross-config value confusion.
module Frank.LinkedData.Tests.SharedCacheBudgetTests

open System
open System.IO
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging.Abstractions
open Microsoft.Extensions.Primitives
open Expecto
open VDS.RDF
open Frank.LinkedData
open Frank.LinkedData.Tests.TestHelpers

let private makeContext (scheme: string) (host: string) (accept: string) (config: LinkedDataConfig) : HttpContext =
    let ctx = new DefaultHttpContext()
    ctx.Request.Method <- "GET"
    ctx.Request.Scheme <- scheme
    ctx.Request.Host <- HostString host
    ctx.Request.Path <- PathString "/data"
    ctx.Request.Headers.Add("Accept", StringValues accept)
    ctx.Response.Body <- new MemoryStream()
    let metadata = EndpointMetadataCollection([ box config ])

    let endpoint =
        Endpoint(RequestDelegate(fun _ -> Task.CompletedTask), metadata, "test")

    ctx.SetEndpoint(endpoint)
    ctx :> HttpContext

let private invoke (middleware: LinkedDataMiddleware) (ctx: HttpContext) : int =
    middleware.InvokeAsync(ctx).GetAwaiter().GetResult()
    ctx.Response.StatusCode

let private readBody (ctx: HttpContext) : string =
    ctx.Response.Body.Position <- 0L
    use reader = new StreamReader(ctx.Response.Body)
    reader.ReadToEnd()

let private newMiddleware () =
    let next =
        RequestDelegate(fun ctx ->
            ctx.Response.StatusCode <- 200
            Task.CompletedTask)

    LinkedDataMiddleware(
        next,
        NullLogger<LinkedDataMiddleware>.Instance,
        LinkedDataVocabularyConfig.None,
        Frank.Builder.newBoundedMemoryCache ()
    )

/// A second fixture graph, distinct from TestHelpers.buildFixtureGraph — a different
/// subject/predicate/object triple so config A's and config B's serialized bodies are
/// textually distinguishable (proving VALUE isolation, not just "some string came back").
let private buildMarkerGraph () : IGraph =
    let graph = new Graph()
    let subject = graph.CreateUriNode(Uri "https://example.org/config-b-marker")

    let predicate =
        graph.CreateUriNode(Uri "http://www.w3.org/2000/01/rdf-schema#seeAlso")

    let obj = graph.CreateUriNode(Uri "https://schema.org/Thing")
    graph.Assert(Triple(subject, predicate, obj)) |> ignore
    graph :> IGraph

[<Tests>]
let tests =
    testList
        "LinkedDataMiddleware staticBodyCache shared-budget trade-off (#468 Fowler-minor)"
        [ testCase
              "the shared budget IS shared across configs: flooding config A's origin-space evicts config B's entry once combined distinct-key count exceeds capacity (a)"
          <| fun _ ->
              let middleware = newMiddleware ()
              let configA = sampleConfig

              let configB =
                  { LinkedDataConfig.Empty with
                      Graph = buildMarkerGraph ()
                      JsonLdContext = schemaOrgContext }

              invoke middleware (makeContext "http" "configb-legit.example" "application/ld+json" configB)
              |> ignore

              Expect.equal
                  middleware.StaticBodyBuildCount
                  1
                  "config B's legitimate entry builds once on its first request"

              for i in 1..10_000 do
                  invoke middleware (makeContext "http" $"flood-{i}.example" "application/ld+json" configA)
                  |> ignore

              // Every one of the 10,000 flood keys (under config A) is itself a
              // guaranteed-fresh key, so the flood alone contributes exactly 10,000 builds
              // to this middleware-global counter — isolate "was config B's entry
              // SPECIFICALLY evicted (by config A's flood, sharing the ONE budget)" as the
              // delta across the recheck request, not an absolute count.
              let buildCountAfterFlood = middleware.StaticBodyBuildCount

              invoke middleware (makeContext "http" "configb-legit.example" "application/ld+json" configB)
              |> ignore

              Expect.equal
                  middleware.StaticBodyBuildCount
                  (buildCountAfterFlood + 1)
                  "flooding config A's origin-space past the shared capacity must eventually evict config B's entry — proving the ONE shared budget (the accepted #468 trade-off), not an independent per-config budget"

          testCase
              "VALUE isolation holds despite the shared budget: config A's and config B's cached bodies for the SAME origin never cross-contaminate (b)"
          <| fun _ ->
              let middleware = newMiddleware ()
              let configA = sampleConfig

              let configB =
                  { LinkedDataConfig.Empty with
                      Graph = buildMarkerGraph ()
                      JsonLdContext = schemaOrgContext }

              let bodyFor (config: LinkedDataConfig) =
                  let ctx = makeContext "http" "shared-origin.example" "application/ld+json" config
                  invoke middleware ctx |> ignore
                  readBody ctx

              let bodyA1 = bodyFor configA
              let bodyB1 = bodyFor configB
              // Repeated requests to the SAME origin, now cache HITS for each config.
              let bodyA2 = bodyFor configA
              let bodyB2 = bodyFor configB

              Expect.notEqual
                  bodyA1
                  bodyB1
                  "config A and config B must never share a cached value for the SAME origin — their graphs differ, so their bodies must differ"

              Expect.equal
                  bodyA2
                  bodyA1
                  "config A's repeated request to the same origin returns its OWN cached body (cache hit), not config B's"

              Expect.equal
                  bodyB2
                  bodyB1
                  "config B's repeated request to the same origin returns its OWN cached body (cache hit), not config A's"

              Expect.equal
                  middleware.StaticBodyBuildCount
                  2
                  "exactly 2 builds total (one per config for the shared origin) — proving no accidental cross-config cache HIT ever occurred, despite the shared underlying cache region" ]
