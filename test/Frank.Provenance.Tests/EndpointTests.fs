module Frank.Provenance.Tests.EndpointTests

open System
open System.Net.Http
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging.Abstractions
open Expecto
open Frank.Provenance

let private mkRecord id resource =
    { Id = id
      ResourceUri = resource
      HttpMethod = "GET"
      StatusCode = 200
      DomainType = None
      Agent = { Id = "urn:agent:anon"; Label = None }
      StartedAt = DateTimeOffset.UnixEpoch
      EndedAt = DateTimeOffset.UnixEpoch }

let private countOccurrences (sub: string) (s: string) =
    let mutable count = 0
    let mutable idx = 0

    while idx <= s.Length - sub.Length do
        if s.[idx .. idx + sub.Length - 1] = sub then
            count <- count + 1
            idx <- idx + sub.Length
        else
            idx <- idx + 1

    count

let private startEndpointServer () =
    let builder = WebApplication.CreateBuilder()
    builder.WebHost.UseTestServer() |> ignore

    let store =
        new MailboxProcessorProvenanceStore(ProvenanceStoreConfig.defaults, NullLogger.Instance) :> IProvenanceStore

    builder.Services.AddSingleton<IProvenanceStore>(store) |> ignore
    let app = builder.Build()
    let resolvedStore = app.Services.GetRequiredService<IProvenanceStore>()
    resolvedStore.Append(mkRecord "urn:uuid:act-1" "/r")
    resolvedStore.Append(mkRecord "urn:uuid:act-2" "/r")

    app.MapGet("/provenance", Func<HttpContext, System.Threading.Tasks.Task>(ProvenanceEndpoint.handle resolvedStore))
    |> ignore

    app.StartAsync().GetAwaiter().GetResult()
    app

[<Tests>]
let tests =
    testList
        "ProvenanceEndpoint"
        [ testCaseAsync "GET /provenance?resource=/r returns 200 ld+json with two Activity entries"
          <| async {
              use app = startEndpointServer ()
              use client = app.GetTestClient()

              let! (resp: HttpResponseMessage) = client.GetAsync("/provenance?resource=/r") |> Async.AwaitTask

              let! body = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
              Expect.equal (int resp.StatusCode) 200 "status 200"

              Expect.isTrue
                  (resp.Content.Headers.ContentType.MediaType.StartsWith("application/ld+json"))
                  "content-type is ld+json"

              let count = countOccurrences "http://www.w3.org/ns/prov#Activity" body

              Expect.isGreaterThanOrEqual count 2 "at least two prov:Activity IRIs in body"
          }

          testCaseAsync "GET /provenance without resource param returns 400 problem+json"
          <| async {
              use app = startEndpointServer ()
              use client = app.GetTestClient()
              let! (resp: HttpResponseMessage) = client.GetAsync("/provenance") |> Async.AwaitTask
              let! body = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
              Expect.equal (int resp.StatusCode) 400 "status 400"

              Expect.equal
                  resp.Content.Headers.ContentType.MediaType
                  "application/problem+json"
                  "content-type is problem+json"

              Expect.stringContains body "Missing required query parameter" "title in body"
          } ]
