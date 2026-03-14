module Frank.Tests.ConditionalRequestTests

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

// -- Mock infrastructure --

/// Mock ETag provider that returns configurable ETags by instance ID.
type MockETagProvider(etagByInstanceId: Map<string, string>) =
    interface IETagProvider with
        member _.ComputeETag(instanceId) =
            task { return Map.tryFind instanceId etagByInstanceId }

/// Mutable mock provider that allows changing ETags between requests (for mutation tests).
type MutableMockETagProvider(initialEtags: Map<string, string>) =
    let mutable currentEtags = initialEtags

    member _.Update(newEtags) = currentEtags <- newEtags

    interface IETagProvider with
        member _.ComputeETag(instanceId) =
            task { return Map.tryFind instanceId currentEtags }

/// Mock provider factory that returns the given provider for any endpoint with ETagMetadata.
type MockETagProviderFactory(provider: IETagProvider) =
    interface IETagProviderFactory with
        member _.CreateProvider(endpoint) =
            let meta = endpoint.Metadata.GetMetadata<ETagMetadata>()

            if isNull (box meta) then None else Some provider

/// Creates a test server with the conditional request middleware and ETag-enabled endpoints.
let createTestServer (provider: IETagProvider) =
    let factory = MockETagProviderFactory(provider)
    let cache = new ETagCache(100, NullLogger<ETagCache>.Instance)
    let builder = WebApplication.CreateBuilder([||])
    builder.WebHost.UseTestServer() |> ignore
    builder.Services.AddRouting() |> ignore
    builder.Services.AddSingleton<ETagCache>(cache) |> ignore
    builder.Services.AddSingleton<IETagProviderFactory>(factory) |> ignore
    builder.Services.AddLogging() |> ignore
    let app = builder.Build()

    (app :> IApplicationBuilder).UseRouting() |> ignore

    (app :> IApplicationBuilder).UseMiddleware<ConditionalRequestMiddleware>()
    |> ignore

    let etagMetadata =
        ETagMetadata("test", fun ctx -> ctx.Request.RouteValues.["id"] :?> string)

    // ETag-enabled GET/HEAD endpoint
    app
        .MapMethods(
            "/resource/{id}",
            [| "GET"; "HEAD" |],
            Func<HttpContext, Task>(fun ctx -> task { do! ctx.Response.WriteAsync("OK") } :> Task)
        )
        .WithMetadata(etagMetadata)
    |> ignore

    // ETag-enabled POST/PUT/DELETE endpoint
    app
        .MapMethods(
            "/resource/{id}",
            [| "POST"; "PUT"; "DELETE" |],
            Func<HttpContext, Task>(fun ctx -> task { do! ctx.Response.WriteAsync("MUTATED") } :> Task)
        )
        .WithMetadata(etagMetadata)
    |> ignore

    // Non-ETag endpoint (no ETagMetadata)
    app.MapGet("/no-etag", Func<HttpContext, Task>(fun ctx -> task { do! ctx.Response.WriteAsync("NO ETAG") } :> Task))
    |> ignore

    app.Start()
    (app.GetTestClient(), cache)

[<Tests>]
let conditionalRequestTests =
    testList
        "ConditionalRequestMiddleware"
        [
          // -- Pass-through tests --
          testTask "Non-ETag resource returns 200 with no ETag header" {
              let provider = MockETagProvider(Map.empty)
              let (client, _cache) = createTestServer provider
              let! (response: HttpResponseMessage) = client.GetAsync("/no-etag")
              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

              Expect.isFalse (response.Headers.Contains("ETag")) "Should not have ETag header on non-ETag resource"
          }

          // -- ETag generation on GET --
          testTask "GET to ETag-enabled resource returns ETag header" {
              let provider = MockETagProvider(Map.ofList [ "42", "abc123" ])
              let (client, _cache) = createTestServer provider
              let! (response: HttpResponseMessage) = client.GetAsync("/resource/42")
              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"
              Expect.isTrue (response.Headers.ETag <> null) "Should have ETag header"
              let etag = response.Headers.ETag.ToString()
              Expect.equal etag "\"abc123\"" "ETag should be quoted raw value"
          }

          // -- 304 Not Modified --
          testTask "GET with matching If-None-Match returns 304 with no body" {
              let provider = MockETagProvider(Map.ofList [ "42", "abc123" ])
              let (client, _cache) = createTestServer provider
              let request = new HttpRequestMessage(HttpMethod.Get, "/resource/42")
              request.Headers.TryAddWithoutValidation("If-None-Match", "\"abc123\"") |> ignore
              let! (response: HttpResponseMessage) = client.SendAsync(request)
              Expect.equal response.StatusCode HttpStatusCode.NotModified "Should return 304"
              Expect.isTrue (response.Headers.ETag <> null) "304 should include ETag header"
              let! body = response.Content.ReadAsStringAsync()
              Expect.equal body "" "304 should have no body"
          }

          testTask "GET with non-matching If-None-Match returns 200 with ETag" {
              let provider = MockETagProvider(Map.ofList [ "42", "abc123" ])
              let (client, _cache) = createTestServer provider
              let request = new HttpRequestMessage(HttpMethod.Get, "/resource/42")

              request.Headers.TryAddWithoutValidation("If-None-Match", "\"different\"")
              |> ignore

              let! (response: HttpResponseMessage) = client.SendAsync(request)
              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"
              Expect.isTrue (response.Headers.ETag <> null) "Should include ETag header"
          }

          testTask "GET with If-None-Match: * on existing resource returns 304" {
              let provider = MockETagProvider(Map.ofList [ "42", "abc123" ])
              let (client, _cache) = createTestServer provider
              let request = new HttpRequestMessage(HttpMethod.Get, "/resource/42")
              request.Headers.TryAddWithoutValidation("If-None-Match", "*") |> ignore
              let! (response: HttpResponseMessage) = client.SendAsync(request)
              Expect.equal response.StatusCode HttpStatusCode.NotModified "Wildcard should match existing resource"
          }

          testTask "GET with If-None-Match: * on non-existent resource returns 200" {
              let provider = MockETagProvider(Map.empty)
              let (client, _cache) = createTestServer provider
              let request = new HttpRequestMessage(HttpMethod.Get, "/resource/999")
              request.Headers.TryAddWithoutValidation("If-None-Match", "*") |> ignore
              let! (response: HttpResponseMessage) = client.SendAsync(request)
              Expect.equal response.StatusCode HttpStatusCode.OK "Wildcard on non-existent should return 200"
          }

          // -- 412 Precondition Failed --
          testTask "POST with matching If-Match proceeds normally" {
              let provider = MockETagProvider(Map.ofList [ "42", "abc123" ])
              let (client, _cache) = createTestServer provider
              let request = new HttpRequestMessage(HttpMethod.Post, "/resource/42")
              request.Headers.TryAddWithoutValidation("If-Match", "\"abc123\"") |> ignore
              request.Content <- new StringContent("data")
              let! (response: HttpResponseMessage) = client.SendAsync(request)
              Expect.equal response.StatusCode HttpStatusCode.OK "Matching If-Match should proceed"
          }

          testTask "POST with non-matching If-Match returns 412 with no body" {
              let provider = MockETagProvider(Map.ofList [ "42", "abc123" ])
              let (client, _cache) = createTestServer provider
              let request = new HttpRequestMessage(HttpMethod.Post, "/resource/42")
              request.Headers.TryAddWithoutValidation("If-Match", "\"stale\"") |> ignore
              request.Content <- new StringContent("data")
              let! (response: HttpResponseMessage) = client.SendAsync(request)

              Expect.equal
                  response.StatusCode
                  HttpStatusCode.PreconditionFailed
                  "Non-matching If-Match should return 412"

              let! body = response.Content.ReadAsStringAsync()
              Expect.equal body "" "412 should have no body"
          }

          testTask "POST with If-Match: * on existing resource proceeds" {
              let provider = MockETagProvider(Map.ofList [ "42", "abc123" ])
              let (client, _cache) = createTestServer provider
              let request = new HttpRequestMessage(HttpMethod.Post, "/resource/42")
              request.Headers.TryAddWithoutValidation("If-Match", "*") |> ignore
              request.Content <- new StringContent("data")
              let! (response: HttpResponseMessage) = client.SendAsync(request)
              Expect.equal response.StatusCode HttpStatusCode.OK "Wildcard If-Match on existing resource should proceed"
          }

          testTask "POST with If-Match: * on non-existent resource returns 412" {
              let provider = MockETagProvider(Map.empty)
              let (client, _cache) = createTestServer provider
              let request = new HttpRequestMessage(HttpMethod.Post, "/resource/999")
              request.Headers.TryAddWithoutValidation("If-Match", "*") |> ignore
              request.Content <- new StringContent("data")
              let! (response: HttpResponseMessage) = client.SendAsync(request)

              Expect.equal
                  response.StatusCode
                  HttpStatusCode.PreconditionFailed
                  "Wildcard If-Match on non-existent resource should return 412"
          }

          // -- No conditional headers --
          testTask "GET without If-None-Match returns 200 with ETag" {
              let provider = MockETagProvider(Map.ofList [ "42", "abc123" ])
              let (client, _cache) = createTestServer provider
              let! (response: HttpResponseMessage) = client.GetAsync("/resource/42")
              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"
              Expect.isTrue (response.Headers.ETag <> null) "Should include ETag header"
          }

          testTask "POST without If-Match proceeds normally" {
              let provider = MockETagProvider(Map.ofList [ "42", "abc123" ])
              let (client, _cache) = createTestServer provider
              let request = new HttpRequestMessage(HttpMethod.Post, "/resource/42")
              request.Content <- new StringContent("data")
              let! (response: HttpResponseMessage) = client.SendAsync(request)
              Expect.equal response.StatusCode HttpStatusCode.OK "POST without If-Match should proceed"
          }

          // -- Cache invalidation after mutation --
          testTask "Successful mutation invalidates cache so next GET computes fresh ETag" {
              let mutableProvider = MutableMockETagProvider(Map.ofList [ "42", "version1" ])
              let (client, _cache) = createTestServer mutableProvider

              // First GET populates cache
              let! (response1: HttpResponseMessage) = client.GetAsync("/resource/42")
              Expect.equal response1.StatusCode HttpStatusCode.OK "Initial GET should succeed"
              let etag1 = response1.Headers.ETag.ToString()
              Expect.equal etag1 "\"version1\"" "Initial ETag should be version1"

              // Update the provider to return a new ETag
              mutableProvider.Update(Map.ofList [ "42", "version2" ])

              // POST invalidates the cache and caches the new value
              let postRequest = new HttpRequestMessage(HttpMethod.Post, "/resource/42")
              postRequest.Content <- new StringContent("data")
              let! (_postResponse: HttpResponseMessage) = client.SendAsync(postRequest)

              // Next GET should return the new ETag
              let! (response2: HttpResponseMessage) = client.GetAsync("/resource/42")
              Expect.equal response2.StatusCode HttpStatusCode.OK "Second GET should succeed"
              let etag2 = response2.Headers.ETag.ToString()
              Expect.equal etag2 "\"version2\"" "ETag should be updated after mutation"
          }

          // -- PUT and DELETE methods --
          testTask "PUT with non-matching If-Match returns 412" {
              let provider = MockETagProvider(Map.ofList [ "42", "abc123" ])
              let (client, _cache) = createTestServer provider
              let request = new HttpRequestMessage(HttpMethod.Put, "/resource/42")
              request.Headers.TryAddWithoutValidation("If-Match", "\"stale\"") |> ignore
              request.Content <- new StringContent("data")
              let! (response: HttpResponseMessage) = client.SendAsync(request)

              Expect.equal
                  response.StatusCode
                  HttpStatusCode.PreconditionFailed
                  "PUT with stale If-Match should return 412"
          }

          testTask "DELETE with non-matching If-Match returns 412" {
              let provider = MockETagProvider(Map.ofList [ "42", "abc123" ])
              let (client, _cache) = createTestServer provider
              let request = new HttpRequestMessage(HttpMethod.Delete, "/resource/42")
              request.Headers.TryAddWithoutValidation("If-Match", "\"stale\"") |> ignore
              let! (response: HttpResponseMessage) = client.SendAsync(request)

              Expect.equal
                  response.StatusCode
                  HttpStatusCode.PreconditionFailed
                  "DELETE with stale If-Match should return 412"
          }

          // -- HEAD method --
          testTask "HEAD with matching If-None-Match returns 304" {
              let provider = MockETagProvider(Map.ofList [ "42", "abc123" ])
              let (client, _cache) = createTestServer provider
              let request = new HttpRequestMessage(HttpMethod.Head, "/resource/42")
              request.Headers.TryAddWithoutValidation("If-None-Match", "\"abc123\"") |> ignore
              let! (response: HttpResponseMessage) = client.SendAsync(request)

              Expect.equal
                  response.StatusCode
                  HttpStatusCode.NotModified
                  "HEAD with matching If-None-Match should return 304"
          }

          // -- Multiple ETags in If-None-Match --
          testTask "GET with multiple If-None-Match values matches correctly" {
              let provider = MockETagProvider(Map.ofList [ "42", "abc123" ])
              let (client, _cache) = createTestServer provider
              let request = new HttpRequestMessage(HttpMethod.Get, "/resource/42")

              request.Headers.TryAddWithoutValidation("If-None-Match", "\"other\", \"abc123\", \"another\"")
              |> ignore

              let! (response: HttpResponseMessage) = client.SendAsync(request)
              Expect.equal response.StatusCode HttpStatusCode.NotModified "Should match one of the multiple ETags"
          } ]
