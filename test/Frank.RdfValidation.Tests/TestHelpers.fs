module Frank.RdfValidation.Tests.TestHelpers

open System
open System.IO
open System.Net.Http
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.DependencyInjection.Extensions
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Abstractions
open VDS.RDF
open VDS.RDF.Parsing
open VDS.RDF.Query
open VDS.RDF.Query.Datasets
open Frank.Builder
open Frank.LinkedData
open Frank.LinkedData.Rdf
open Frank.Provenance

// ---------------------------------------------------------------------------
// Minimal observable subject (avoids System.Reactive dependency)
// Reuses the pattern from Frank.Provenance.Tests.IntegrationTests.
// ---------------------------------------------------------------------------

/// A minimal observable subject that allows manual event dispatch in tests.
type TransitionSubject() =
    let observers = System.Collections.Generic.List<IObserver<TransitionEvent>>()

    member _.OnNext(event: TransitionEvent) =
        for obs in observers do
            obs.OnNext(event)

    interface IObservable<TransitionEvent> with
        member _.Subscribe(observer) =
            observers.Add(observer)

            { new IDisposable with
                member _.Dispose() = observers.Remove(observer) |> ignore }

// ---------------------------------------------------------------------------
// Ontology builder
// ---------------------------------------------------------------------------

/// Creates a test ontology graph with OWL property declarations for Person and Order types.
/// Defines properties matching the JSON keys in sample endpoint responses.
let createTestOntology () : IGraph =
    let ontology = new Graph()

    // Register namespace prefixes
    ontology.NamespaceMap.AddNamespace("rdf", UriFactory.Root.Create("http://www.w3.org/1999/02/22-rdf-syntax-ns#"))
    ontology.NamespaceMap.AddNamespace("rdfs", UriFactory.Root.Create("http://www.w3.org/2000/01/rdf-schema#"))
    ontology.NamespaceMap.AddNamespace("owl", UriFactory.Root.Create("http://www.w3.org/2002/07/owl#"))
    ontology.NamespaceMap.AddNamespace("xsd", UriFactory.Root.Create("http://www.w3.org/2001/XMLSchema#"))
    ontology.NamespaceMap.AddNamespace("ex", UriFactory.Root.Create("http://example.org/api/properties/"))

    let rdfType =
        ontology.CreateUriNode(UriFactory.Root.Create("http://www.w3.org/1999/02/22-rdf-syntax-ns#type"))

    let owlDatatypeProperty =
        ontology.CreateUriNode(UriFactory.Root.Create("http://www.w3.org/2002/07/owl#DatatypeProperty"))

    let owlObjectProperty =
        ontology.CreateUriNode(UriFactory.Root.Create("http://www.w3.org/2002/07/owl#ObjectProperty"))

    // Person properties
    let nameNode =
        ontology.CreateUriNode(UriFactory.Root.Create("http://example.org/api/properties/Person/Name"))

    let ageNode =
        ontology.CreateUriNode(UriFactory.Root.Create("http://example.org/api/properties/Person/Age"))

    ontology.Assert(Triple(nameNode, rdfType, owlDatatypeProperty)) |> ignore
    ontology.Assert(Triple(ageNode, rdfType, owlDatatypeProperty)) |> ignore

    // Order properties
    let productNode =
        ontology.CreateUriNode(UriFactory.Root.Create("http://example.org/api/properties/Order/Product"))

    let quantityNode =
        ontology.CreateUriNode(UriFactory.Root.Create("http://example.org/api/properties/Order/Quantity"))

    ontology.Assert(Triple(productNode, rdfType, owlDatatypeProperty)) |> ignore
    ontology.Assert(Triple(quantityNode, rdfType, owlDatatypeProperty)) |> ignore

    ontology

// ---------------------------------------------------------------------------
// LinkedDataConfig builder
// ---------------------------------------------------------------------------

/// Creates a LinkedDataConfig from the provided ontology graph.
let makeLinkedDataConfig (ontology: IGraph) : LinkedDataConfig =
    let manifest: SemanticManifest =
        { Version = "1.0.0"
          BaseUri = "http://example.org/api"
          SourceHash = "test-hash"
          Vocabularies = []
          GeneratedAt = DateTimeOffset.UtcNow }

    { OntologyGraph = ontology
      ShapesGraph = new Graph()
      BaseUri = "http://example.org/api"
      Manifest = manifest }

// ---------------------------------------------------------------------------
// TestHost creation (T004)
// ---------------------------------------------------------------------------

/// Creates a TestHost with both LinkedData and Provenance middleware,
/// sample resource endpoints, and proper DI registration.
///
/// Endpoints:
///   GET /person/1 -> {"Name":"Alice","Age":30} (with LinkedDataMarker)
///   GET /order/42 -> {"Product":"Widget","Quantity":5} (with LinkedDataMarker)
///   POST /person  -> 201 Created (with LinkedDataMarker, unsafe transition)
///
/// Returns the started IHost. Caller must dispose via `use`.
let createTestHost () : IHost =
    let ontology = createTestOntology ()
    let config = makeLinkedDataConfig ontology
    let subject = TransitionSubject()

    let hostBuilder =
        HostBuilder()
            .ConfigureWebHost(fun webBuilder ->
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(fun services ->
                        services.AddSingleton<LinkedDataConfig>(config) |> ignore
                        services.AddRouting() |> ignore
                        services.AddLogging() |> ignore

                        // Provenance services
                        services.AddSingleton<IObservable<TransitionEvent>>(subject :> IObservable<TransitionEvent>)
                        |> ignore

                        services.TryAddSingleton<IProvenanceStore>(fun sp ->
                            let logger = sp.GetRequiredService<ILogger<MailboxProcessorProvenanceStore>>()
                            new MailboxProcessorProvenanceStore(ProvenanceStoreConfig.defaults, logger) :> IProvenanceStore)

                        services.AddHostedService<ProvenanceSubscriptionManager>() |> ignore)
                    .Configure(fun (app: IApplicationBuilder) ->
                        app.UseRouting() |> ignore

                        // LinkedData middleware
                        app.Use(
                            Func<HttpContext, RequestDelegate, Task>(
                                WebHostBuilderExtensions.linkedDataMiddleware NullLogger.Instance
                            )
                        )
                        |> ignore

                        // Provenance middleware
                        let loggerFactory = app.ApplicationServices.GetRequiredService<ILoggerFactory>()

                        app.Use(
                            Func<RequestDelegate, RequestDelegate>(fun next ->
                                ProvenanceMiddleware.createProvenanceMiddleware loggerFactory next)
                        )
                        |> ignore

                        app.UseEndpoints(fun endpoints ->
                            // GET /person/1 with LinkedDataMarker
                            endpoints
                                .MapGet(
                                    "/person/1",
                                    RequestDelegate(fun ctx ->
                                        ctx.Response.ContentType <- "application/json"
                                        ctx.Response.WriteAsync("""{"Name":"Alice","Age":30}"""))
                                )
                                .WithMetadata(LinkedDataMarker)
                            |> ignore

                            // GET /order/42 with LinkedDataMarker
                            endpoints
                                .MapGet(
                                    "/order/42",
                                    RequestDelegate(fun ctx ->
                                        ctx.Response.ContentType <- "application/json"
                                        ctx.Response.WriteAsync("""{"Product":"Widget","Quantity":5}"""))
                                )
                                .WithMetadata(LinkedDataMarker)
                            |> ignore

                            // POST /person with LinkedDataMarker (unsafe transition)
                            endpoints
                                .MapPost(
                                    "/person",
                                    RequestDelegate(fun ctx ->
                                        ctx.Response.StatusCode <- 201
                                        ctx.Response.ContentType <- "application/json"
                                        ctx.Response.WriteAsync("""{"Name":"Created","Age":0}"""))
                                )
                                .WithMetadata(LinkedDataMarker)
                            |> ignore)
                        |> ignore)
                |> ignore)

    let host = hostBuilder.Build()
    host.Start()
    host

// ---------------------------------------------------------------------------
// RDF graph loading helpers (T005)
// ---------------------------------------------------------------------------

/// Parse a Turtle string into an IGraph.
/// Caller is responsible for disposing the returned graph via `use`.
let loadTurtleGraph (turtle: string) : IGraph =
    let graph = new Graph()
    use reader = new StringReader(turtle)
    let parser = TurtleParser()
    parser.Load(graph, reader)
    graph

/// Parse an RDF/XML string into an IGraph.
/// Caller is responsible for disposing the returned graph via `use`.
let loadRdfXmlGraph (rdfxml: string) : IGraph =
    let graph = new Graph()
    use reader = new StringReader(rdfxml)
    let parser = RdfXmlParser()
    parser.Load(graph, reader)
    graph

/// Parse a JSON-LD string into an IGraph.
/// dotNetRdf.Core does not include a JSON-LD parser, so this uses the
/// custom JSON-LD format produced by Frank.LinkedData.Negotiation.JsonLdFormatter.
/// It parses the JSON-LD @context and @id to reconstruct triples.
let loadJsonLdGraph (jsonLd: string) : IGraph =
    let graph = new Graph()
    use doc = System.Text.Json.JsonDocument.Parse(jsonLd)
    let root = doc.RootElement

    // Extract @context mappings: property name -> full URI
    let context =
        match root.TryGetProperty("@context") with
        | true, ctx ->
            ctx.EnumerateObject()
            |> Seq.map (fun p -> p.Name, p.Value.GetString())
            |> Map.ofSeq
        | false, _ -> Map.empty

    let parseSubject (element: System.Text.Json.JsonElement) =
        let subjectId =
            match element.TryGetProperty("@id") with
            | true, id -> id.GetString()
            | false, _ -> null

        if not (isNull subjectId) then
            let subject = graph.CreateUriNode(UriFactory.Root.Create(subjectId))

            for prop in element.EnumerateObject() do
                if prop.Name <> "@id" && prop.Name <> "@context" && prop.Name <> "@graph" then
                    let predicateUri =
                        match context |> Map.tryFind prop.Name with
                        | Some uri -> uri
                        | None -> prop.Name

                    let predicate = graph.CreateUriNode(UriFactory.Root.Create(predicateUri))

                    match prop.Value.ValueKind with
                    | System.Text.Json.JsonValueKind.String ->
                        let literal = graph.CreateLiteralNode(prop.Value.GetString())
                        graph.Assert(Triple(subject, predicate, literal)) |> ignore
                    | System.Text.Json.JsonValueKind.Number ->
                        if prop.Value.TryGetInt64() |> fst then
                            let v = prop.Value.GetInt64()

                            let literal =
                                graph.CreateLiteralNode(
                                    v.ToString(),
                                    UriFactory.Root.Create("http://www.w3.org/2001/XMLSchema#integer")
                                )

                            graph.Assert(Triple(subject, predicate, literal)) |> ignore
                        else
                            let v = prop.Value.GetDouble()

                            let literal =
                                graph.CreateLiteralNode(
                                    v.ToString("R"),
                                    UriFactory.Root.Create("http://www.w3.org/2001/XMLSchema#double")
                                )

                            graph.Assert(Triple(subject, predicate, literal)) |> ignore
                    | System.Text.Json.JsonValueKind.True ->
                        let literal =
                            graph.CreateLiteralNode(
                                "true",
                                UriFactory.Root.Create("http://www.w3.org/2001/XMLSchema#boolean")
                            )

                        graph.Assert(Triple(subject, predicate, literal)) |> ignore
                    | System.Text.Json.JsonValueKind.False ->
                        let literal =
                            graph.CreateLiteralNode(
                                "false",
                                UriFactory.Root.Create("http://www.w3.org/2001/XMLSchema#boolean")
                            )

                        graph.Assert(Triple(subject, predicate, literal)) |> ignore
                    | System.Text.Json.JsonValueKind.Object ->
                        // Nested object with @id -> URI reference
                        match prop.Value.TryGetProperty("@id") with
                        | true, nestedId ->
                            let objNode = graph.CreateUriNode(UriFactory.Root.Create(nestedId.GetString()))
                            graph.Assert(Triple(subject, predicate, objNode)) |> ignore
                        | false, _ -> ()
                    | _ -> ()

    // Handle single subject (flat) or multi-subject (@graph)
    match root.TryGetProperty("@graph") with
    | true, graphArray ->
        for element in graphArray.EnumerateArray() do
            parseSubject element
    | false, _ -> parseSubject root

    graph

/// Send a GET request with a specific Accept header and return the response body.
let getRdfResponse (client: HttpClient) (path: string) (accept: string) : Async<string> =
    async {
        use request = new HttpRequestMessage(HttpMethod.Get, path)
        request.Headers.Add("Accept", accept)
        let! (response: HttpResponseMessage) = client.SendAsync(request) |> Async.AwaitTask
        response.EnsureSuccessStatusCode() |> ignore
        let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
        return body
    }

// ---------------------------------------------------------------------------
// SPARQL execution helpers (T006)
// ---------------------------------------------------------------------------

/// Execute a SPARQL SELECT query against a single graph and return the result set.
let executeSparql (graph: IGraph) (sparql: string) : SparqlResultSet =
    let store = new TripleStore()
    store.Add(graph) |> ignore
    let dataset = new InMemoryDataset(store)
    let processor = new LeviathanQueryProcessor(dataset :> ISparqlDataset)
    let parser = new SparqlQueryParser()
    let query = parser.ParseFromString(sparql)

    match processor.ProcessQuery(query) with
    | :? SparqlResultSet as results -> results
    | _ -> failwith "Expected SparqlResultSet from SELECT/ASK query"

/// Execute a SPARQL query against a TripleStore with named graphs.
let executeSparqlOnDataset (store: TripleStore) (sparql: string) : SparqlResultSet =
    let dataset = new InMemoryDataset(store)
    let processor = new LeviathanQueryProcessor(dataset :> ISparqlDataset)
    let parser = new SparqlQueryParser()
    let query = parser.ParseFromString(sparql)

    match processor.ProcessQuery(query) with
    | :? SparqlResultSet as results -> results
    | _ -> failwith "Expected SparqlResultSet from SELECT/ASK query"

/// Execute a SPARQL ASK query and return the boolean result.
let executeSparqlAsk (graph: IGraph) (sparql: string) : bool =
    let store = new TripleStore()
    store.Add(graph) |> ignore
    let dataset = new InMemoryDataset(store)
    let processor = new LeviathanQueryProcessor(dataset :> ISparqlDataset)
    let parser = new SparqlQueryParser()
    let query = parser.ParseFromString(sparql)

    match processor.ProcessQuery(query) with
    | :? SparqlResultSet as results -> results.Result
    | _ -> failwith "Expected SparqlResultSet from ASK query"
