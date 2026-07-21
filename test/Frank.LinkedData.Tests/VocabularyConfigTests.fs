module Frank.LinkedData.Tests.VocabularyConfigTests

/// #420 expert-review follow-up (findings 1+2+3): LinkedDataVocabularyConfig is an
/// app-wide, DI-registered singleton (mirrors DiscoveryConfig.HomeRoute/ProfileUri) —
/// NOT a per-resource LinkedDataConfig.VocabularyUri field. This proves:
///   - finding 1+2: a codegen-path resource (GeneratedLinkedDataResolver.resolveFromType,
///     unchanged, carrying zero per-resource vocabulary config) gets the describedby
///     Link header for free once the app configures useLinkedDataVocabulary.
///   - finding 3: the header fires on EVERY safe-method response for a LinkedData-owned
///     endpoint, BEFORE representation negotiation — including plain-JSON and no-Accept
///     requests, not only RDF-negotiated ones.
open System
open System.Net.Http
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.TestHost
open Expecto
open Frank.Builder
open Frank.LinkedData
open Frank.LinkedData.Tests.TestHelpers

/// Extract the rel="describedby" Link header target, if present.
let private describedByTarget (resp: HttpResponseMessage) : string option =
    match resp.Headers.TryGetValues "Link" with
    | true, values ->
        values
        |> Seq.tryPick (fun v ->
            if v.Contains "rel=\"describedby\"" then
                let s = v.IndexOf '<'
                let e = v.IndexOf '>'
                Some(v.Substring(s + 1, e - s - 1))
            else
                None)
    | false, _ -> None

/// Runs the real useLinkedData/useLinkedDataVocabulary WebHostBuilder CE operations
/// (spec.Services / spec.Middleware, composed exactly as the CE block would apply them)
/// against a TestServer with one GET endpoint carrying `ldConfig` as LinkedDataConfig
/// metadata — proving the DI wiring end-to-end rather than hand-rolling an
/// AddSingleton<LinkedDataVocabularyConfig> that bypasses the CE operations under test.
let private startServerWithSpec (spec: WebHostSpec) (ldConfig: LinkedDataConfig) : WebApplication =
    let builder = WebApplication.CreateBuilder()
    builder.WebHost.UseTestServer() |> ignore
    spec.Services builder.Services |> ignore
    let app = builder.Build()
    app.UseRouting() |> ignore
    (app :> IApplicationBuilder) |> spec.Middleware |> ignore

    app
        .MapGet("/vocab", Func<HttpContext, Task>(fun ctx -> ctx.Response.WriteAsync "downstream"))
        .WithMetadata(ldConfig)
    |> ignore

    app.StartAsync().GetAwaiter().GetResult()
    app

[<Tests>]
let tests =
    testList
        "LinkedDataVocabularyConfig — app-wide describedby route (#420 expert-review)"
        [ testCase
              "codegen-path resource (resolveFromType, zero per-resource vocab config) gets describedby once app-level route is configured"
          <| fun _ ->
              // Mirrors the MSBuild codegen path exactly (GeneratedLinkedDataResolver.resolveFromType) —
              // no per-resource vocabulary field anywhere on this config.
              let codegenConfig =
                  match GeneratedLinkedDataResolver.resolveFromType typeof<Frank.LinkedData.Tests.GeneratedLinkedData> with
                  | Ok c -> c
                  | Error e -> failtest $"fixture resolution failed: {e}"

              let builder = WebHostBuilder([||])

              let spec =
                  WebHostSpec.Empty
                  |> fun s -> builder.UseLinkedData(s)
                  |> fun s -> builder.UseLinkedDataVocabulary(s, "/vocabulary")

              use app = startServerWithSpec spec codegenConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/vocab")
              req.Headers.Add("Accept", "text/turtle")
              let resp = client.SendAsync(req).GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "200 OK, RDF served"

              Expect.equal
                  (describedByTarget resp)
                  (Some "/vocabulary")
                  "codegen-path resource carries the app-level describedby Link with zero per-resource config"

          testCase "describedby header appears on Accept: application/json (non-RDF, PassThrough) — finding 3"
          <| fun _ ->
              let builder = WebHostBuilder([||])

              let spec =
                  WebHostSpec.Empty
                  |> fun s -> builder.UseLinkedData(s)
                  |> fun s -> builder.UseLinkedDataVocabulary(s, "/vocabulary")

              use app = startServerWithSpec spec sampleConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/vocab")
              req.Headers.Add("Accept", "application/json")
              let resp = client.SendAsync(req).GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "downstream 200 (PassThrough, non-RDF)"
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              Expect.stringContains body "downstream" "downstream handler ran"

              Expect.equal
                  (describedByTarget resp)
                  (Some "/vocabulary")
                  "describedby Link present even though the representation is plain JSON, not RDF-negotiated"

          testCase "describedby header appears with NO Accept header at all (naive-client thesis case) — finding 3"
          <| fun _ ->
              let builder = WebHostBuilder([||])

              let spec =
                  WebHostSpec.Empty
                  |> fun s -> builder.UseLinkedData(s)
                  |> fun s -> builder.UseLinkedDataVocabulary(s, "/vocabulary")

              use app = startServerWithSpec spec sampleConfig
              use client = app.GetTestClient()
              let resp = client.GetAsync("/vocab").GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "downstream 200 (no Accept header)"

              Expect.equal
                  (describedByTarget resp)
                  (Some "/vocabulary")
                  "describedby Link present with no Accept header at all"

          testCase "describedby header appears on 406 NotAcceptable (unsupported RDF Accept) — finding 3"
          <| fun _ ->
              let builder = WebHostBuilder([||])

              let spec =
                  WebHostSpec.Empty
                  |> fun s -> builder.UseLinkedData(s)
                  |> fun s -> builder.UseLinkedDataVocabulary(s, "/vocabulary")

              use app = startServerWithSpec spec sampleConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/vocab")
              req.Headers.Add("Accept", "application/xml")
              let resp = client.SendAsync(req).GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 406 "406 Not Acceptable"

              Expect.equal
                  (describedByTarget resp)
                  (Some "/vocabulary")
                  "describedby Link present even on the 406 NotAcceptable outcome"

          testCase "no useLinkedDataVocabulary configured → default None, no describedby header anywhere"
          <| fun _ ->
              let builder = WebHostBuilder([||])
              let spec = WebHostSpec.Empty |> fun s -> builder.UseLinkedData(s)

              use app = startServerWithSpec spec sampleConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/vocab")
              req.Headers.Add("Accept", "text/turtle")
              let resp = client.SendAsync(req).GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "200 OK, RDF served"

              Expect.equal
                  (describedByTarget resp)
                  None
                  "default LinkedDataVocabularyConfig.None → no describedby header"

          testCase "useLinkedDataVocabulary rejects a whitespace route"
          <| fun _ ->
              let builder = WebHostBuilder([||])

              Expect.throwsT<ArgumentException>
                  (fun () -> builder.UseLinkedDataVocabulary(WebHostSpec.Empty, "   ") |> ignore)
                  "whitespace vocabularyRoute must raise invalidArg"

          testCase
              "useLinkedDataVocabulary called BEFORE useLinkedData (reversed order) still yields describedby — TryAdd-default/Add-override must be order-independent (#420)"
          <| fun _ ->
              let builder = WebHostBuilder([||])

              // Reversed order vs every other test in this file: the vocabulary route is
              // configured FIRST, then useLinkedData second. Prior to the TryAddSingleton
              // fix, useLinkedData's unconditional AddSingleton<LinkedDataVocabularyConfig>
              // ran last and clobbered the override back to LinkedDataVocabularyConfig.None.
              let spec =
                  WebHostSpec.Empty
                  |> fun s -> builder.UseLinkedDataVocabulary(s, "/vocabulary")
                  |> fun s -> builder.UseLinkedData(s)

              use app = startServerWithSpec spec sampleConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/vocab")
              req.Headers.Add("Accept", "text/turtle")
              let resp = client.SendAsync(req).GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "200 OK, RDF served"

              Expect.equal
                  (describedByTarget resp)
                  (Some "/vocabulary")
                  "describedby Link present even when useLinkedDataVocabulary is called before useLinkedData" ]
