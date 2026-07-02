module Frank.Provenance.Tests.EndpointTests

open System
open System.Net.Http
open System.Text.Json
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
      EndedAt = DateTimeOffset.UnixEpoch
      BodyAttributes = [] }

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
    resolvedStore.Append(mkRecord "urn:uuid:act-1" "http://localhost/r")
    resolvedStore.Append(mkRecord "urn:uuid:act-2" "http://localhost/r")

    app.MapGet("/provenance", Func<HttpContext, System.Threading.Tasks.Task>(ProvenanceEndpoint.handle resolvedStore))
    |> ignore

    app.StartAsync().GetAwaiter().GetResult()
    app

/// Server whose records carry a schema:agent body attribute so compaction is observable.
let private startEndpointServerWithSchemaAttrs () =
    let builder = WebApplication.CreateBuilder()
    builder.WebHost.UseTestServer() |> ignore

    let store =
        new MailboxProcessorProvenanceStore(ProvenanceStoreConfig.defaults, NullLogger.Instance) :> IProvenanceStore

    builder.Services.AddSingleton<IProvenanceStore>(store) |> ignore
    let app = builder.Build()
    let resolvedStore = app.Services.GetRequiredService<IProvenanceStore>()

    let mkWithAttr id resource =
        { mkRecord id resource with
            BodyAttributes = [ "https://schema.org/agent", Literal "alice" ] }

    resolvedStore.Append(mkWithAttr "urn:uuid:act-1" "http://localhost/r")
    resolvedStore.Append(mkWithAttr "urn:uuid:act-2" "http://localhost/r")

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

              let count = countOccurrences "prov:Activity" body

              Expect.isGreaterThanOrEqual count 2 "at least two prov:Activity CURIEs in body"
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
          }

          testCaseAsync "#16 provenance endpoint @context includes schema and ttt prefixes"
          <| async {
              // Use a server with schema body attrs so compaction is observable.
              use app = startEndpointServerWithSchemaAttrs ()
              use client = app.GetTestClient()
              let! (resp: HttpResponseMessage) = client.GetAsync("/provenance?resource=http://localhost/r") |> Async.AwaitTask
              let! body = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
              Expect.equal (int resp.StatusCode) 200 "status 200"
              let mutable schemaEl = Unchecked.defaultof<JsonElement>
              let mutable tttEl = Unchecked.defaultof<JsonElement>
              use doc = JsonDocument.Parse body
              let root = doc.RootElement

              let tryGetCtxObj () =
                  let mutable ctxEl = Unchecked.defaultof<JsonElement>

                  if root.TryGetProperty("@context", &ctxEl) then
                      match ctxEl.ValueKind with
                      | JsonValueKind.Object -> Some ctxEl
                      | JsonValueKind.Array ->
                          ctxEl.EnumerateArray()
                          |> Seq.tryFind (fun e -> e.ValueKind = JsonValueKind.Object)
                      | _ -> None
                  else
                      None

              let ctxObj = tryGetCtxObj ()
              Expect.isSome ctxObj "@context object must be present"
              let obj = ctxObj.Value
              Expect.isTrue (obj.TryGetProperty("schema", &schemaEl)) "@context has 'schema' prefix"
              Expect.isTrue (obj.TryGetProperty("ttt", &tttEl)) "@context has 'ttt' prefix"
              // #16 real compaction: schema body attr must compact when schema is in extraContext.
              Expect.stringContains body "schema:agent" "schema:agent must be compacted in provenance body"

              Expect.isFalse
                  (body.Contains "\"https://schema.org/agent\"")
                  "full schema.org agent IRI must not appear as JSON property key after compaction"
          } ]
