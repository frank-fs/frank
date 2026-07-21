module Frank.Validation.Tests.RoundTripTests

open System.Net.Http
open System.Text
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open VDS.RDF
open Expecto
open Frank.LinkedData
open Frank.Validation

/// #414 AC1: a client that GETs a resource's own served JSON-LD @context and POSTs it
/// straight back must be accepted by validation — never 400 due to an unresolvable
/// @context entry. This drives the REAL LinkedDataMiddleware (serving) and REAL
/// ValidationMiddleware (validating) together in one pipeline — the same two middlewares
/// TicTacToe-v732's Program.fs composes via `useLinkedData`/`useValidation` — using the
/// EXACT literal JsonLdContext shape that sample's /tictactoe vocabulary resource serves
/// (rdf/rdfs/owl + the real, versioned schema.org context-document URL, #414 causes a+b
/// together), so this is not a synthetic fixture drifting from what's actually served.
let private tttStyleJsonLdContext =
    """{"@context":["http://www.w3.org/1999/02/22-rdf-syntax-ns#","http://www.w3.org/2000/01/rdf-schema#","http://www.w3.org/2002/07/owl#","https://schema.org/version/latest/schemaorg-current-https.jsonld"]}"""

let private buildFixtureGraph () : IGraph =
    let g = new Graph()
    g.NamespaceMap.AddNamespace("schema", UriFactory.Create "https://schema.org/")
    g.NamespaceMap.AddNamespace("owl", UriFactory.Create "http://www.w3.org/2002/07/owl#")
    let subj = g.CreateUriNode(UriFactory.Create "https://example.org/vocab#Square")

    let rdfType =
        g.CreateUriNode(UriFactory.Create "http://www.w3.org/1999/02/22-rdf-syntax-ns#type")

    let owlClass =
        g.CreateUriNode(UriFactory.Create "http://www.w3.org/2002/07/owl#Class")

    g.Assert(Triple(subj, rdfType, owlClass)) |> ignore
    g :> IGraph

/// Empty ShapesGraph — always conforms, isolating this test to the context-RESOLUTION
/// concern (#414) rather than domain-specific SHACL shape authoring.
let private emptyValidationConfig () : ValidationConfig =
    { Shapes = Shapes.toShapesGraph []
      ContextLoader = JsonLdLoader.synthesizing [ "https://schema.org/" ]
      MaxBodyBytes = ValidationConfig.defaultMaxBodyBytes
      HostRelativeProperties = [] }

/// Wires a REAL LinkedDataMiddleware (GET /vocab serves the fixture graph with the
/// tictactoe-sample-style @context) and a REAL ValidationMiddleware (POST /vocab
/// validates ld+json bodies) in the same pipeline, mirroring Program.fs's composition.
let private startRoundTripServer () =
    let builder = WebApplication.CreateBuilder()
    builder.WebHost.UseTestServer() |> ignore
    builder.Services.AddSingleton(emptyValidationConfig ()) |> ignore
    builder.Services.AddSingleton(LinkedDataVocabularyConfig.None) |> ignore
    let app = builder.Build()
    app.UseMiddleware<LinkedDataMiddleware>() |> ignore
    app.UseMiddleware<ValidationMiddleware>() |> ignore

    let ldConfig: LinkedDataConfig =
        { Graph = buildFixtureGraph ()
          JsonLdContext = tttStyleJsonLdContext
          GraphFactory = None }

    app
        .MapGet(
            "/vocab",
            RequestDelegate(fun ctx ->
                ctx.Response.WriteAsync "unreachable — LinkedDataMiddleware always serves GET here")
        )
        .Add(fun eb -> eb.Metadata.Add(box ldConfig))
    |> ignore

    app.MapPost("/vocab", RequestDelegate(fun ctx -> ctx.Response.WriteAsync "downstream: validated"))
    |> ignore

    app.StartAsync().GetAwaiter().GetResult()
    app

[<Tests>]
let tests =
    testList
        "LinkedData → Validation round trip (#414)"
        [ testCase "GET /vocab serves ld+json with a served @context (sanity — fixture actually round-trippable)"
          <| fun _ ->
              use app = startRoundTripServer ()
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/vocab")
              req.Headers.Add("Accept", "application/ld+json")
              let resp = client.SendAsync(req).GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "GET must serve 200"
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              Expect.stringContains body "@context" "served body carries @context"

              Expect.stringContains
                  body
                  "schemaorg-current-https.jsonld"
                  "served body cites the real schema.org context-document URL"

          testCase "POSTing the resource's own served @context body back is never rejected with 400"
          <| fun _ ->
              use app = startRoundTripServer ()
              use client = app.GetTestClient()
              use getReq = new HttpRequestMessage(HttpMethod.Get, "/vocab")
              getReq.Headers.Add("Accept", "application/ld+json")
              let getResp = client.SendAsync(getReq).GetAwaiter().GetResult()
              Expect.equal (int getResp.StatusCode) 200 "precondition: GET served 200"
              let servedBody = getResp.Content.ReadAsStringAsync().GetAwaiter().GetResult()

              use content = new StringContent(servedBody, Encoding.UTF8, "application/ld+json")
              let postResp = client.PostAsync("/vocab", content).GetAwaiter().GetResult()

              Expect.equal
                  (int postResp.StatusCode)
                  200
                  "POSTing the exact served @context body back must be accepted (context resolves, empty shapes conform, passes through to the handler) — never 400" ]
