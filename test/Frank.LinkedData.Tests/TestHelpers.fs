module Frank.LinkedData.Tests.TestHelpers

open System.Net.Http
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open VDS.RDF
open Frank.LinkedData

/// Build a minimal fixture IGraph with one outbound triple (seeAlso to schema.org/Game).
let buildFixtureGraph () : IGraph =
    let graph = new Graph()
    let subject = graph.CreateUriNode(System.Uri("https://example.org/game/1"))

    let predicate =
        graph.CreateUriNode(System.Uri("http://www.w3.org/2000/01/rdf-schema#seeAlso"))

    let obj = graph.CreateUriNode(System.Uri("https://schema.org/Game"))
    graph.Assert(Triple(subject, predicate, obj)) |> ignore
    graph :> IGraph

/// External @context referencing schema.org — the canonical fixture context string.
let schemaOrgContext = """{"@context":["https://schema.org"]}"""

let sampleConfig =
    { Graph = buildFixtureGraph ()
      JsonLdContext = schemaOrgContext
      GraphFactory = None }

/// Build a fixture graph with a ttt:square term using the given request origin.
let buildTttGraphWithOrigin (origin: string) : IGraph =
    let graph = new Graph()
    let subject = graph.CreateUriNode(System.Uri(origin + "/tictactoe#square"))
    let rdfType = graph.CreateUriNode(System.Uri "http://www.w3.org/1999/02/22-rdf-syntax-ns#type")
    let rdfsClass = graph.CreateUriNode(System.Uri "http://www.w3.org/2000/01/rdf-schema#Class")
    graph.Assert(Triple(subject, rdfType, rdfsClass)) |> ignore
    graph :> IGraph

/// Config with GraphFactory so the middleware builds an origin-resolved graph per request.
/// No example.org placeholder — the factory receives the actual request origin.
let sampleConfigWithFactory =
    { Graph = buildFixtureGraph ()
      JsonLdContext = """{"@context":{"ttt":"/tictactoe#"}}"""
      GraphFactory = Some buildTttGraphWithOrigin }

/// Spin a TestServer with LinkedDataMiddleware installed and a no-op next delegate.
let startServer (config: LinkedDataConfig) =
    let builder = WebApplication.CreateBuilder()
    builder.WebHost.UseTestServer() |> ignore
    builder.Services.AddSingleton(config) |> ignore
    let app = builder.Build()
    app.UseMiddleware<LinkedDataMiddleware>() |> ignore

    app.MapGet("/data", System.Func<string>(fun () -> "downstream")) |> ignore

    app.StartAsync().GetAwaiter().GetResult()
    app
