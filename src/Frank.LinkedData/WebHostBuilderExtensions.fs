namespace Frank.LinkedData

open System
open System.IO
open System.Reflection
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Microsoft.Net.Http.Headers
open Frank.Builder
open Frank.LinkedData.Negotiation
open Frank.LinkedData.Rdf.RdfUriHelpers
open VDS.RDF

[<AutoOpen>]
module WebHostBuilderExtensions =

    let private supportedMediaTypes =
        [ "application/ld+json"; "text/turtle"; "application/rdf+xml" ]

    /// Determines the RDF media type from the Accept header, if any.
    /// Uses MediaTypeHeaderValue for proper RFC 9110 Section 12.5.1 parsing with quality factors.
    let negotiateRdfType (accept: string) =
        if String.IsNullOrWhiteSpace accept then
            None
        else
            let (success, mediaTypes) =
                MediaTypeHeaderValue.TryParseList(Microsoft.Extensions.Primitives.StringValues(accept))

            if not success || isNull mediaTypes then
                None
            else
                mediaTypes
                |> Seq.sortByDescending (fun mt -> mt.Quality |> Option.ofNullable |> Option.defaultValue 1.0)
                |> Seq.tryPick (fun mt ->
                    supportedMediaTypes
                    |> List.tryFind (fun s -> mt.MediaType.Equals(s, StringComparison.OrdinalIgnoreCase)))

    /// Writes an IGraph to the stream in the specified RDF media type.
    let writeRdf (mediaType: string) (graph: IGraph) (stream: Stream) =
        match mediaType with
        | "text/turtle" -> TurtleFormatter.writeTurtle graph stream
        | "application/ld+json" -> JsonLdFormatter.writeJsonLd graph stream
        | "application/rdf+xml" -> RdfXmlFormatter.writeRdfXml graph stream
        | _ -> ()

    /// Projects a JSON response body to an RDF graph using the ontology property index.
    let projectJsonToRdf (ontologyGraph: IGraph) (subjectUri: Uri) (json: JsonElement) : IGraph =
        let output = new Graph()
        let subject = output.CreateUriNode(UriFactory.Root.Create(subjectUri.ToString()))

        // Build ontology index: lowercase property local name -> full predicate URI
        let ontologyIndex =
            let dict =
                System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)

            for triple in ontologyGraph.Triples do
                match triple.Subject with
                | :? IUriNode as uriNode ->
                    let uri = uriNode.Uri.ToString()
                    let name = localName uri

                    if name <> uri then
                        if not (dict.ContainsKey(name)) then
                            dict.[name] <- uri
                | _ -> ()

            dict

        let xsd suffix =
            UriFactory.Root.Create(sprintf "http://www.w3.org/2001/XMLSchema#%s" suffix)

        match json.ValueKind with
        | JsonValueKind.Object ->
            for prop in json.EnumerateObject() do
                match ontologyIndex.TryGetValue(prop.Name) with
                | false, _ -> () // No matching ontology property
                | true, predicateUri ->
                    let predicate = output.CreateUriNode(UriFactory.Root.Create(predicateUri))

                    match prop.Value.ValueKind with
                    | JsonValueKind.String ->
                        let literal = output.CreateLiteralNode(prop.Value.GetString())
                        output.Assert(Triple(subject, predicate, literal)) |> ignore
                    | JsonValueKind.Number ->
                        if prop.Value.TryGetInt64() |> fst then
                            let v = prop.Value.GetInt64()
                            let literal = output.CreateLiteralNode(v.ToString(), xsd "integer")
                            output.Assert(Triple(subject, predicate, literal)) |> ignore
                        else
                            let v = prop.Value.GetDouble()
                            let literal = output.CreateLiteralNode(v.ToString("R"), xsd "double")
                            output.Assert(Triple(subject, predicate, literal)) |> ignore
                    | JsonValueKind.True ->
                        let literal = output.CreateLiteralNode("true", xsd "boolean")
                        output.Assert(Triple(subject, predicate, literal)) |> ignore
                    | JsonValueKind.False ->
                        let literal = output.CreateLiteralNode("false", xsd "boolean")
                        output.Assert(Triple(subject, predicate, literal)) |> ignore
                    | _ -> () // Skip null, arrays, nested objects for Phase 1
        | _ -> ()

        output

    /// Content negotiation middleware logic shared between useLinkedData overloads.
    let linkedDataMiddleware (logger: ILogger) (ctx: HttpContext) (next: RequestDelegate) =
        let endpoint = ctx.GetEndpoint()

        let hasLinkedData =
            not (isNull endpoint)
            && not (obj.ReferenceEquals(endpoint.Metadata.GetMetadata<LinkedDataMarker>(), null))

        if not hasLinkedData then
            next.Invoke(ctx)
        else
            let accept = ctx.Request.Headers.Accept.ToString()

            match negotiateRdfType accept with
            | None -> next.Invoke(ctx)
            | Some mediaType ->
                let originalBody = ctx.Response.Body

                task {
                    use buffer = new MemoryStream()
                    ctx.Response.Body <- buffer

                    do! next.Invoke(ctx)

                    buffer.Seek(0L, SeekOrigin.Begin) |> ignore
                    let handlerOutput = buffer.ToArray()

                    if handlerOutput.Length > 0 then
                        try
                            let config = ctx.RequestServices.GetRequiredService<LinkedDataConfig>()
                            let json = JsonDocument.Parse(handlerOutput)
                            let baseUri = Uri(config.BaseUri + ctx.Request.Path.Value)
                            let rdfGraph = projectJsonToRdf config.OntologyGraph baseUri json.RootElement
                            // Write RDF to a temp buffer (synchronous writers),
                            // then async-copy to the original response stream.
                            use rdfBuffer = new MemoryStream()
                            writeRdf mediaType rdfGraph rdfBuffer
                            let rdfBytes = rdfBuffer.ToArray()
                            ctx.Response.Body <- originalBody
                            ctx.Response.ContentType <- mediaType
                            ctx.Response.Headers.Remove("Content-Length") |> ignore
                            do! originalBody.WriteAsync(rdfBytes, 0, rdfBytes.Length)
                        with
                        | :? InvalidOperationException as ex ->
                            // Recoverable: configuration/serialization issue, fall back to original response
                            logger.LogWarning(ex, "LinkedData content negotiation failed for {Path}", ctx.Request.Path)
                            ctx.Response.Body <- originalBody
                            do! originalBody.WriteAsync(handlerOutput, 0, handlerOutput.Length)
                        | :? JsonException as ex ->
                            // Recoverable: response body was not valid JSON, fall back
                            logger.LogWarning(ex, "LinkedData JSON parsing failed for {Path}", ctx.Request.Path)
                            ctx.Response.Body <- originalBody
                            do! originalBody.WriteAsync(handlerOutput, 0, handlerOutput.Length)
                        | ex ->
                            // Unrecoverable: log and re-raise (fail-fast per Constitution VII)
                            logger.LogError(
                                ex,
                                "Unhandled exception in LinkedData middleware for {Path}",
                                ctx.Request.Path
                            )

                            ctx.Response.Body <- originalBody
                            raise (exn ("LinkedData middleware failure", ex))
                    else
                        ctx.Response.Body <- originalBody
                }
                :> Task

    type WebHostBuilder with
        /// Registers LinkedDataConfig from embedded resources and adds content
        /// negotiation middleware for endpoints marked with linkedData.
        [<CustomOperation("useLinkedData")>]
        member _.UseLinkedData(spec: WebHostSpec) : WebHostSpec =
            { spec with
                Services =
                    spec.Services
                    >> fun services ->
                        let assembly =
                            match Assembly.GetEntryAssembly() with
                            | null ->
                                failwith
                                    "Assembly.GetEntryAssembly() returned null. This may occur in test host scenarios — provide the assembly explicitly via useLinkedDataWith."
                            | asm -> asm

                        match LinkedDataConfig.loadConfig assembly with
                        | Ok config -> services.AddSingleton<LinkedDataConfig>(config) |> ignore
                        | Error msg ->
                            raise (InvalidOperationException(sprintf "LinkedData configuration error: %s" msg))

                        services
                Middleware =
                    spec.Middleware
                    >> fun app ->
                        let logger =
                            app.ApplicationServices
                                .GetRequiredService<ILoggerFactory>()
                                .CreateLogger("Frank.LinkedData.Middleware")

                        app.Use(Func<HttpContext, RequestDelegate, Task>(linkedDataMiddleware logger))
                        |> ignore

                        app }

        /// Registers a pre-loaded LinkedDataConfig and adds content negotiation middleware.
        [<CustomOperation("useLinkedDataWith")>]
        member _.UseLinkedDataWith(spec: WebHostSpec, config: LinkedDataConfig) : WebHostSpec =
            { spec with
                Services =
                    spec.Services
                    >> fun services ->
                        services.AddSingleton<LinkedDataConfig>(config) |> ignore
                        services
                Middleware =
                    spec.Middleware
                    >> fun app ->
                        let logger =
                            app.ApplicationServices
                                .GetRequiredService<ILoggerFactory>()
                                .CreateLogger("Frank.LinkedData.Middleware")

                        app.Use(Func<HttpContext, RequestDelegate, Task>(linkedDataMiddleware logger))
                        |> ignore

                        app }
