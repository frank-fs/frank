module Frank.Discovery.Tests.OpenApiIntegrationTests

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http.Metadata
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Expecto
open Frank.Discovery

/// #400 AC1, end-to-end: an app that references BOTH Frank.Discovery (project-referenced
/// by this test project) AND Frank.OpenApi (also project-referenced — see fsproj) must
/// serve both its ALPS profile (Frank.Discovery's internal, generate-only AddOpenApi()
/// call) and its own hosted /openapi/{document}.json (Frank.OpenApi's useOpenApi(),
/// mirrored here via the same underlying services.AddOpenApi()/app.MapOpenApi() calls
/// Frank.OpenApi's WebHostBuilderExtensions wraps) without collision — each document
/// name is independent (Frank.Discovery: "frank-discovery-internal"; this app: default
/// "v1"), and Frank.Discovery never itself calls MapOpenApi().
type private MoveRequestFixture = { Position: string }

let private startCombinedServer () : WebApplication =
    let builder = WebApplication.CreateBuilder()
    builder.WebHost.UseTestServer() |> ignore
    builder.Services.AddRouting() |> ignore
    // The app's OWN OpenAPI document (default "v1") — what Frank.OpenApi's useOpenApi()
    // registers/hosts. Frank.Discovery's separate, internal "frank-discovery-internal"
    // document (registered by useDiscoveryWith's AddOpenApi() call) never collides with it.
    builder.Services.AddOpenApi() |> ignore

    let config =
        { DiscoveryConfig.Empty with
            ProfileUri = "/alps/test"
            HomeRoute = "/"
            AlpsDescriptors =
                [ { Id = "MoveAction"
                    // Deliberately wrong codegen default — reconciliation via the live POST's
                    // IAcceptsMetadata (below) must override it to "unsafe", proving Frank.Discovery's
                    // correlation is genuinely live-derived even with Frank.OpenApi's own
                    // AddOpenApi() call also present in the same DI container (#400 AC1).
                    Type = "semantic"
                    Doc = None
                    Href = Some "https://schema.org/MoveAction"
                    Descriptors = []
                    Rt = None
                    ClassIri = None
                    RequestClrTypeName = Some typeof<MoveRequestFixture>.FullName } ] }

    builder.Services.AddSingleton(config) |> ignore
    let app = builder.Build()
    app.UseRouting() |> ignore
    app.UseMiddleware<DiscoveryMiddleware.DiscoveryMiddleware>() |> ignore

    app
        .MapMethods("/games/{id}", [| "POST" |], System.Func<string>(fun () -> "moved"))
        .WithMetadata(AcceptsMetadata([| "application/json" |], typeof<MoveRequestFixture>, false) :> obj)
    |> ignore

    app.MapOpenApi() |> ignore

    app.StartAsync().GetAwaiter().GetResult()
    app

[<Tests>]
let tests =
    testList
        "DiscoveryMiddleware + Frank.OpenApi — #400 AC1: coexistence when an app references both"
        [ testCase
              "ALPS profile (Frank.Discovery) and /openapi/v1.json (Frank.OpenApi's own hosted document) both serve 200, and the ALPS Type is genuinely reconciled from the live endpoint"
          <| fun _ ->
              use app = startCombinedServer ()
              use client = app.GetTestClient()

              let alpsResp = client.GetAsync("/alps/test").GetAwaiter().GetResult()
              Expect.equal (int alpsResp.StatusCode) 200 "ALPS profile served"
              let alpsBody = alpsResp.Content.ReadAsStringAsync().GetAwaiter().GetResult()

              Expect.stringContains
                  alpsBody
                  "\"type\":\"unsafe\""
                  "MoveAction reconciled to unsafe from the live POST's IAcceptsMetadata — not left at the deliberately-wrong codegen default 'semantic'"

              let openApiResp = client.GetAsync("/openapi/v1.json").GetAwaiter().GetResult()

              Expect.equal
                  (int openApiResp.StatusCode)
                  200
                  "the app's own hosted OpenAPI document still serves 200 — Frank.Discovery's internal, generate-only AddOpenApi() call (a separate document name) never hosts /openapi/... itself and does not break it" ]
