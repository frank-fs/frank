module Frank.LinkedData.Tests.TestHelpers

open System.Net.Http
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
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

/// Build a fixture graph with a ttt:square term using the request origin from HttpContext.
let buildTttGraphWithOrigin (ctx: HttpContext) : IGraph =
    let origin = $"{ctx.Request.Scheme}://{ctx.Request.Host}"
    let graph = new Graph()
    let subject = graph.CreateUriNode(System.Uri(origin + "/tictactoe#square"))

    let rdfType =
        graph.CreateUriNode(System.Uri "http://www.w3.org/1999/02/22-rdf-syntax-ns#type")

    let rdfsClass =
        graph.CreateUriNode(System.Uri "http://www.w3.org/2000/01/rdf-schema#Class")

    graph.Assert(Triple(subject, rdfType, rdfsClass)) |> ignore
    graph :> IGraph

/// Config with GraphFactory so the middleware builds an origin-resolved graph per request.
/// No example.org placeholder — the factory receives the actual request HttpContext.
let sampleConfigWithFactory =
    { Graph = buildFixtureGraph ()
      JsonLdContext = """{"@context":{"ttt":"/tictactoe#"}}"""
      GraphFactory = Some buildTttGraphWithOrigin }

/// Spin a TestServer with LinkedDataMiddleware installed.
/// UseRouting is called before the middleware so ctx.GetEndpoint() resolves correctly.
/// The /data endpoint carries 'config' as metadata so the middleware serves it.
let startServer (config: LinkedDataConfig) =
    let builder = WebApplication.CreateBuilder()
    builder.WebHost.UseTestServer() |> ignore
    builder.Services.AddSingleton(config) |> ignore
    let app = builder.Build()
    app.UseRouting() |> ignore
    app.UseMiddleware<LinkedDataMiddleware>() |> ignore

    app.MapGet("/data", System.Func<string>(fun () -> "downstream")).WithMetadata(config)
    |> ignore

    app.StartAsync().GetAwaiter().GetResult()
    app

/// Build a game graph using the request origin — matches the sample's gameGraphFactory pattern.
let buildGameGraphWithOrigin (ctx: HttpContext) : IGraph =
    let origin = $"{ctx.Request.Scheme}://{ctx.Request.Host}"
    let graph = new Graph()
    let subject = graph.CreateUriNode(System.Uri(origin + "/games/1"))

    let rdfType =
        graph.CreateUriNode(System.Uri "http://www.w3.org/1999/02/22-rdf-syntax-ns#type")

    let gameClass = graph.CreateUriNode(System.Uri "https://schema.org/Game")
    graph.Assert(Triple(subject, rdfType, gameClass)) |> ignore
    graph :> IGraph

/// Build a graph with named namespace prefixes (schema + ttt) to verify they surface in @context (#16).
let buildGraphWithNamespacesAndBaseUri (ctx: HttpContext) : IGraph =
    let origin = $"{ctx.Request.Scheme}://{ctx.Request.Host}"
    let graph = new Graph()
    graph.BaseUri <- System.Uri origin
    graph.NamespaceMap.AddNamespace("schema", UriFactory.Create "https://schema.org/")
    graph.NamespaceMap.AddNamespace("ttt", UriFactory.Create(origin + "/tictactoe#"))
    let sub = graph.CreateUriNode(System.Uri(origin + "/tictactoe#Square"))

    let rdfType =
        graph.CreateUriNode(System.Uri "http://www.w3.org/1999/02/22-rdf-syntax-ns#type")

    let owlClass = graph.CreateUriNode(System.Uri "http://www.w3.org/2002/07/owl#Class")
    graph.Assert(Triple(sub, rdfType, owlClass)) |> ignore
    graph :> IGraph

/// Config using a factory that sets namespace prefixes — for #16 and double-@base tests.
let sampleConfigWithNamespaces =
    { Graph = buildFixtureGraph ()
      JsonLdContext = schemaOrgContext
      GraphFactory = Some buildGraphWithNamespacesAndBaseUri }

/// Build a graph with an external-namespace predicate (schema:, off-origin) and a
/// local-namespace predicate (ttt:, under origin/base') — #394: the inline @context[0]
/// object must exclude the external prefix while body compaction still uses it.
let buildGraphWithExternalAndLocalNamespaces (ctx: HttpContext) : IGraph =
    let origin = $"{ctx.Request.Scheme}://{ctx.Request.Host}"
    let graph = new Graph()
    graph.BaseUri <- System.Uri origin
    graph.NamespaceMap.AddNamespace("schema", UriFactory.Create "https://schema.org/")
    graph.NamespaceMap.AddNamespace("ttt", UriFactory.Create(origin + "/tictactoe#"))
    let subject = graph.CreateUriNode(System.Uri(origin + "/games/1"))

    let actionStatusPred =
        graph.CreateUriNode(System.Uri "https://schema.org/actionStatus")

    let activeStatus =
        graph.CreateUriNode(System.Uri "https://schema.org/ActiveActionStatus")

    graph.Assert(Triple(subject, actionStatusPred, activeStatus)) |> ignore

    let currentPlayerPred =
        graph.CreateUriNode(System.Uri(origin + "/tictactoe#currentPlayer"))

    graph.Assert(Triple(subject, currentPlayerPred, graph.CreateLiteralNode "X"))
    |> ignore

    graph :> IGraph

/// Config for #394 — external (schema:) + local (ttt:) namespace prefixes, both used in triples.
let sampleConfigWithExternalAndLocalNamespaces =
    { Graph = buildFixtureGraph ()
      JsonLdContext = schemaOrgContext
      GraphFactory = Some buildGraphWithExternalAndLocalNamespaces }

/// TestServer with a LinkedData-config endpoint (/data) AND a plain endpoint (/plain) without config.
/// Used for MINOR-3: only LinkedData-owned endpoints should 406 for unsupported RDF Accept.
let startServerWithPlainRoute (config: LinkedDataConfig) =
    let builder = WebApplication.CreateBuilder()
    builder.WebHost.UseTestServer() |> ignore
    builder.Services.AddSingleton(config) |> ignore
    let app = builder.Build()
    app.UseRouting() |> ignore
    app.UseMiddleware<LinkedDataMiddleware>() |> ignore

    app.MapGet("/data", System.Func<string>(fun () -> "downstream")).WithMetadata(config)
    |> ignore

    app.MapGet("/plain", System.Func<string>(fun () -> "plain downstream"))
    |> ignore

    app.StartAsync().GetAwaiter().GetResult()
    app

/// TestServer with /tictactoe and /games/{id} routes, each carrying a GraphFactory config.
/// Mirrors the TicTacToe sample so the origin-DoS guard is tested on both factory paths.
let startServerWithTttRoutes () =
    let builder = WebApplication.CreateBuilder()
    builder.WebHost.UseTestServer() |> ignore
    let app = builder.Build()
    app.UseRouting() |> ignore
    app.UseMiddleware<LinkedDataMiddleware>() |> ignore

    let tttConfig =
        { sampleConfigWithFactory with
            GraphFactory = Some buildTttGraphWithOrigin }

    let gameConfig =
        { sampleConfigWithFactory with
            GraphFactory = Some buildGameGraphWithOrigin }

    app.MapGet("/tictactoe", System.Func<string>(fun () -> "ttt downstream")).WithMetadata(tttConfig)
    |> ignore

    app.MapGet("/games/{id}", System.Func<string>(fun () -> "game downstream")).WithMetadata(gameConfig)
    |> ignore

    app.StartAsync().GetAwaiter().GetResult()
    app
