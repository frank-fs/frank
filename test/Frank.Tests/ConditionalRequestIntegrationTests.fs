module Frank.Tests.ConditionalRequestIntegrationTests

open System
open System.Net
open System.Net.Http
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging.Abstractions
open Expecto
open Frank

/// Disposable wrapper for test server resources.
type TestServer<'P>(app: WebApplication, client: HttpClient, provider: 'P, cache: ETagCache) =
    member _.Client = client
    member _.Provider = provider
    member _.Cache = cache

    interface IAsyncDisposable with
        member _.DisposeAsync() =
            client.Dispose()
            (cache :> IDisposable).Dispose()
            app.DisposeAsync()

// -- Dictionary-backed IETagProvider for integration tests --

/// Mutable ETag provider wrapping an in-memory dictionary of instance ID -> raw ETag value.
/// Raw values are unquoted; the middleware quotes them via ETagFormat.quote.
type DictionaryETagProvider() =
    let store = System.Collections.Generic.Dictionary<string, string>()

    member _.Set(instanceId: string, rawETag: string) = store.[instanceId] <- rawETag

    member _.Remove(instanceId: string) = store.Remove(instanceId) |> ignore

    interface IETagProvider with
        member _.ComputeETag(instanceId) =
            task {
                match store.TryGetValue(instanceId) with
                | true, v -> return Some v
                | false, _ -> return None
            }

/// Provider factory that returns the given provider for any endpoint with ETagMetadata.
type DictionaryETagProviderFactory(provider: IETagProvider) =
    interface IETagProviderFactory with
        member _.CreateProvider(endpoint) =
            let meta = endpoint.Metadata.GetMetadata<ETagMetadata>()
            if isNull (box meta) then None else Some provider

/// Creates a test server with the conditional request middleware and ETag-enabled endpoints.
/// Returns the HttpClient, the DictionaryETagProvider, and the ETagCache.
let createIntegrationTestServer () =
    let provider = DictionaryETagProvider()
    let factory = DictionaryETagProviderFactory(provider)
    let cache = new ETagCache(100, NullLogger<ETagCache>.Instance)
    let builder = WebApplication.CreateBuilder([||])
    builder.WebHost.UseTestServer() |> ignore
    builder.Services.AddRouting() |> ignore
    builder.Services.AddSingleton<ETagCache>(cache) |> ignore

    builder.Services.AddSingleton<IETagProviderFactory>(factory :> IETagProviderFactory)
    |> ignore

    builder.Services.AddLogging() |> ignore
    let app = builder.Build()

    (app :> IApplicationBuilder).UseRouting() |> ignore

    (app :> IApplicationBuilder).UseMiddleware<ConditionalRequestMiddleware>()
    |> ignore

    let etagMetadata =
        ETagMetadata("items", fun ctx -> ctx.Request.RouteValues.["id"] :?> string)

    // ETag-enabled GET/HEAD endpoint
    app
        .MapMethods(
            "/items/{id}",
            [| "GET"; "HEAD" |],
            Func<HttpContext, Task>(fun ctx ->
                task {
                    let id = ctx.Request.RouteValues.["id"] :?> string
                    do! ctx.Response.WriteAsync(sprintf "Item %s" id)
                }
                :> Task)
        )
        .WithMetadata(etagMetadata)
    |> ignore

    // ETag-enabled POST/PUT/DELETE endpoint
    app
        .MapMethods(
            "/items/{id}",
            [| "POST"; "PUT"; "DELETE" |],
            Func<HttpContext, Task>(fun ctx ->
                task {
                    let id = ctx.Request.RouteValues.["id"] :?> string
                    do! ctx.Response.WriteAsync(sprintf "Mutated %s" id)
                }
                :> Task)
        )
        .WithMetadata(etagMetadata)
    |> ignore

    // Non-ETag endpoint (no ETagMetadata attached)
    app.MapGet(
        "/plain",
        Func<HttpContext, Task>(fun ctx -> task { do! ctx.Response.WriteAsync("No ETag here") } :> Task)
    )
    |> ignore

    app.Start()
    new TestServer<_>(app, app.GetTestClient(), provider, cache)

[<Tests>]
let conditionalRequestIntegrationTests =
    testList
        "ConditionalRequestMiddleware Integration"
        [
          // -- GET returns ETag header --
          testTask "GET includes ETag header when provider has a value" {
              use s = createIntegrationTestServer ()
              s.Provider.Set("42", "abc123")
              let! (response: HttpResponseMessage) = s.Client.GetAsync("/items/42")
              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"
              Expect.isTrue (response.Headers.ETag <> null) "Should have ETag header"
              let etag = response.Headers.ETag.ToString()
              Expect.equal etag "\"abc123\"" "ETag should be quoted raw value"
          }

          // -- ETag changes when provider returns different value --
          testTask "ETag changes when provider returns different value after mutation" {
              use s = createIntegrationTestServer ()
              s.Provider.Set("42", "version1")

              // First GET
              let! (r1: HttpResponseMessage) = s.Client.GetAsync("/items/42")
              Expect.equal r1.StatusCode HttpStatusCode.OK "First GET should return 200"
              let etag1 = r1.Headers.ETag.ToString()
              Expect.equal etag1 "\"version1\"" "First ETag should be version1"

              // Update the provider
              s.Provider.Set("42", "version2")

              // POST triggers cache invalidation and re-computation
              let postReq = new HttpRequestMessage(HttpMethod.Post, "/items/42")
              postReq.Content <- new StringContent("data")
              let! (_postResp: HttpResponseMessage) = s.Client.SendAsync(postReq)

              // Second GET should get the new ETag
              let! (r2: HttpResponseMessage) = s.Client.GetAsync("/items/42")
              Expect.equal r2.StatusCode HttpStatusCode.OK "Second GET should return 200"
              let etag2 = r2.Headers.ETag.ToString()
              Expect.equal etag2 "\"version2\"" "ETag should be updated after mutation"
          }

          // -- Conditional GET with matching If-None-Match returns 304 no body --
          testTask "Conditional GET with matching If-None-Match returns 304 with no body" {
              use s = createIntegrationTestServer ()
              s.Provider.Set("42", "abc123")
              let request = new HttpRequestMessage(HttpMethod.Get, "/items/42")
              request.Headers.TryAddWithoutValidation("If-None-Match", "\"abc123\"") |> ignore
              let! (response: HttpResponseMessage) = s.Client.SendAsync(request)
              Expect.equal response.StatusCode HttpStatusCode.NotModified "Should return 304"
              let! body = response.Content.ReadAsStringAsync()
              Expect.equal body "" "304 should have no body"
          }

          // -- GET with old If-None-Match returns 200 with new ETag --
          testTask "GET with old If-None-Match returns 200 with new ETag" {
              use s = createIntegrationTestServer ()
              s.Provider.Set("42", "newversion")
              let request = new HttpRequestMessage(HttpMethod.Get, "/items/42")

              request.Headers.TryAddWithoutValidation("If-None-Match", "\"oldversion\"")
              |> ignore

              let! (response: HttpResponseMessage) = s.Client.SendAsync(request)
              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200 for non-matching ETag"
              Expect.isTrue (response.Headers.ETag <> null) "Should include new ETag header"
              let etag = response.Headers.ETag.ToString()
              Expect.equal etag "\"newversion\"" "Should return the current ETag"
          }

          // -- If-None-Match: * returns 304 --
          testTask "If-None-Match: * returns 304 when resource has an ETag" {
              use s = createIntegrationTestServer ()
              s.Provider.Set("42", "somevalue")
              let request = new HttpRequestMessage(HttpMethod.Get, "/items/42")
              request.Headers.TryAddWithoutValidation("If-None-Match", "*") |> ignore
              let! (response: HttpResponseMessage) = s.Client.SendAsync(request)
              Expect.equal response.StatusCode HttpStatusCode.NotModified "Wildcard should match existing resource"
          }

          // -- Multiple ETags in If-None-Match, one matches returns 304 --
          testTask "Multiple ETags in If-None-Match, one matches returns 304" {
              use s = createIntegrationTestServer ()
              s.Provider.Set("42", "match_me")
              let request = new HttpRequestMessage(HttpMethod.Get, "/items/42")

              request.Headers.TryAddWithoutValidation("If-None-Match", "\"other\", \"match_me\", \"another\"")
              |> ignore

              let! (response: HttpResponseMessage) = s.Client.SendAsync(request)
              Expect.equal response.StatusCode HttpStatusCode.NotModified "Should match one of the multiple ETags"
          }

          // -- POST with matching If-Match returns 200 --
          testTask "POST with matching If-Match proceeds with 200" {
              use s = createIntegrationTestServer ()
              s.Provider.Set("42", "current_etag")
              let request = new HttpRequestMessage(HttpMethod.Post, "/items/42")

              request.Headers.TryAddWithoutValidation("If-Match", "\"current_etag\"")
              |> ignore

              request.Content <- new StringContent("data")
              let! (response: HttpResponseMessage) = s.Client.SendAsync(request)
              Expect.equal response.StatusCode HttpStatusCode.OK "Matching If-Match should proceed"
          }

          // -- POST with stale If-Match returns 412 --
          testTask "POST with stale If-Match returns 412 Precondition Failed" {
              use s = createIntegrationTestServer ()
              s.Provider.Set("42", "current_etag")
              let request = new HttpRequestMessage(HttpMethod.Post, "/items/42")
              request.Headers.TryAddWithoutValidation("If-Match", "\"stale_etag\"") |> ignore
              request.Content <- new StringContent("data")
              let! (response: HttpResponseMessage) = s.Client.SendAsync(request)

              Expect.equal response.StatusCode HttpStatusCode.PreconditionFailed "Stale If-Match should return 412"

              let! body = response.Content.ReadAsStringAsync()
              Expect.equal body "" "412 should have no body"
          }

          // -- POST without If-Match proceeds normally --
          testTask "POST without If-Match proceeds normally" {
              use s = createIntegrationTestServer ()
              s.Provider.Set("42", "current_etag")
              let request = new HttpRequestMessage(HttpMethod.Post, "/items/42")
              request.Content <- new StringContent("data")
              let! (response: HttpResponseMessage) = s.Client.SendAsync(request)
              Expect.equal response.StatusCode HttpStatusCode.OK "POST without If-Match should proceed"
              let! body = response.Content.ReadAsStringAsync()
              Expect.equal body "Mutated 42" "Should return handler response"
          }

          // -- Resource without provider returns no ETag, no conditional processing --
          testTask "Resource without ETag provider returns no ETag header and no conditional processing" {
              use s = createIntegrationTestServer ()
              let request = new HttpRequestMessage(HttpMethod.Get, "/plain")

              request.Headers.TryAddWithoutValidation("If-None-Match", "\"anything\"")
              |> ignore

              let! (response: HttpResponseMessage) = s.Client.SendAsync(request)
              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200 regardless of If-None-Match"

              Expect.isFalse (response.Headers.Contains("ETag")) "Should not have ETag header on non-ETag resource"

              let! body = response.Content.ReadAsStringAsync()
              Expect.equal body "No ETag here" "Should return normal body"
          }

          // -- 304 has ETag header but no body --
          testTask "304 response includes ETag header but no body" {
              use s = createIntegrationTestServer ()
              s.Provider.Set("42", "etag_value")
              let request = new HttpRequestMessage(HttpMethod.Get, "/items/42")

              request.Headers.TryAddWithoutValidation("If-None-Match", "\"etag_value\"")
              |> ignore

              let! (response: HttpResponseMessage) = s.Client.SendAsync(request)
              Expect.equal response.StatusCode HttpStatusCode.NotModified "Should return 304"
              Expect.isTrue (response.Headers.ETag <> null) "304 should include ETag header"
              let etag = response.Headers.ETag.ToString()
              Expect.equal etag "\"etag_value\"" "304 ETag should match the current value"
              let! body = response.Content.ReadAsStringAsync()
              Expect.equal body "" "304 should have empty body"
          }

          // -- ETag format: quoted hex string --
          testTask "ETag format is a quoted string on the wire" {
              use s = createIntegrationTestServer ()
              // Use a hex-like raw ETag to verify quoting
              s.Provider.Set("42", "0a1b2c3d4e5f6789")
              let! (response: HttpResponseMessage) = s.Client.GetAsync("/items/42")
              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"
              let etag = response.Headers.ETag.ToString()
              // Should be quoted: "0a1b2c3d4e5f6789"
              Expect.isTrue (etag.StartsWith("\"")) "ETag should start with quote"
              Expect.isTrue (etag.EndsWith("\"")) "ETag should end with quote"
              Expect.equal etag "\"0a1b2c3d4e5f6789\"" "ETag should be quoted raw hex value"
          } ]
