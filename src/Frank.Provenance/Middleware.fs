namespace Frank.Provenance

open System
open System.IO
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open VDS.RDF

/// Custom media types for provenance content negotiation.
[<RequireQualifiedAccess>]
module ProvenanceMediaTypes =
    [<Literal>]
    let ProvenanceJson = "application/vnd.frank.provenance+json"

    [<Literal>]
    let ProvenanceLdJson = "application/vnd.frank.provenance+ld+json"

    [<Literal>]
    let ProvenanceTurtle = "application/vnd.frank.provenance+turtle"

    [<Literal>]
    let ProvenanceRdfXml = "application/vnd.frank.provenance+rdf+xml"

    /// Prefix shared by all provenance media types.
    [<Literal>]
    let Prefix = "application/vnd.frank.provenance+"

/// Provenance content negotiation middleware.
module ProvenanceMiddleware =

    /// Check if the Accept header contains a provenance media type.
    /// Returns the matched media type string or None.
    let tryMatchProvenanceAccept (acceptHeader: string) : string option =
        if isNull acceptHeader || not (acceptHeader.Contains(ProvenanceMediaTypes.Prefix)) then
            None
        else
            // Check for exact matches (most specific first).
            // Check +ld+json before +json since both contain "+json".
            let types =
                [ ProvenanceMediaTypes.ProvenanceLdJson
                  ProvenanceMediaTypes.ProvenanceJson
                  ProvenanceMediaTypes.ProvenanceTurtle
                  ProvenanceMediaTypes.ProvenanceRdfXml ]

            types |> List.tryFind (fun t -> acceptHeader.Contains(t))

    /// Map a provenance media type to the standard RDF media type for serialization.
    let private standardMediaType (mediaType: string) =
        match mediaType with
        | ProvenanceMediaTypes.ProvenanceJson
        | ProvenanceMediaTypes.ProvenanceLdJson -> "application/ld+json"
        | ProvenanceMediaTypes.ProvenanceTurtle -> "text/turtle"
        | ProvenanceMediaTypes.ProvenanceRdfXml -> "application/rdf+xml"
        | _ -> "text/turtle"

    /// Serialize an IGraph to the HTTP response using Frank.LinkedData formatters.
    let private writeGraphToResponse (context: HttpContext) (graph: IGraph) (mediaType: string) =
        task {
            context.Response.ContentType <- mediaType
            context.Response.StatusCode <- 200
            use buffer = new MemoryStream()
            let rdfMediaType = standardMediaType mediaType
            Frank.LinkedData.WebHostBuilderExtensions.writeRdf rdfMediaType graph buffer
            let bytes = buffer.ToArray()
            do! context.Response.Body.WriteAsync(bytes, 0, bytes.Length)
        }

    /// The middleware request delegate.
    let provenanceMiddleware (next: RequestDelegate) (context: HttpContext) =
        task {
            // Only intercept GET requests.
            if not (HttpMethods.IsGet(context.Request.Method)) then
                do! next.Invoke(context)
            else
                let acceptHeader = context.Request.Headers.Accept.ToString()

                match tryMatchProvenanceAccept acceptHeader with
                | None ->
                    // Not a provenance request -- pass through (zero overhead path).
                    do! next.Invoke(context)

                | Some mediaType ->
                    let logger =
                        context.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("Frank.Provenance.Middleware")

                    let store = context.RequestServices.GetRequiredService<IProvenanceStore>()

                    try
                        // Extract resource URI from request path.
                        let resourceUri = context.Request.Path.Value

                        // Query provenance store.
                        let! records = store.QueryByResource(resourceUri)

                        if records.IsEmpty then
                            logger.LogDebug(
                                "No provenance records for {ResourceUri}, returning empty graph",
                                resourceUri
                            )

                        // Build RDF graph.
                        let graph = GraphBuilder.toGraph records

                        // Serialize and write response.
                        do! writeGraphToResponse context graph mediaType

                        logger.LogDebug(
                            "Served provenance for {ResourceUri} with {RecordCount} records as {MediaType}",
                            resourceUri,
                            records.Length,
                            mediaType
                        )
                    with ex ->
                        logger.LogError(ex, "Failed to serve provenance for {Path}", context.Request.Path.Value)
                        context.Response.StatusCode <- 500
                        do! context.Response.WriteAsync("Internal server error serving provenance")
        }
        :> Task
