namespace Frank.LinkedData

open System
open System.IO
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging
open VDS.RDF
open VDS.RDF.Writing

/// The set of Accept media types that this middleware handles.
/// Anything not in this set is either passed through (non-RDF) or 406 (RDF-looking but unsupported).
[<AutoOpen>]
module private AcceptNegotiation =

    let supportedTypes =
        [| "application/ld+json"; "text/turtle"; "application/rdf+xml" |]

    /// RDF media types the middleware recognises as being "in scope" for content negotiation.
    /// If the client asks for one of these but it's not in supportedTypes, we 406.
    let rdfScopeTypes =
        Set.ofArray
            [| "application/ld+json"
               "text/turtle"
               "application/rdf+xml"
               "application/n-triples"
               "text/n3"
               "application/n-quads"
               "application/trig"
               "application/xml" |]

    let parseAccept (acceptHeader: string) : string list =
        acceptHeader.Split(',')
        |> Array.map (fun part ->
            let semicolon = part.IndexOf(';')

            if semicolon >= 0 then
                part.[.. semicolon - 1].Trim()
            else
                part.Trim())
        |> Array.filter (fun s -> not (String.IsNullOrEmpty s))
        |> Array.toList

    type NegotiationResult =
        | Serve of mediaType: string
        | NotAcceptable
        | PassThrough

    let negotiate (acceptHeader: string) : NegotiationResult =
        if String.IsNullOrEmpty acceptHeader then
            PassThrough
        else
            let types = parseAccept acceptHeader

            let firstSupported =
                types |> List.tryFind (fun t -> Array.contains t supportedTypes)

            match firstSupported with
            | Some t -> Serve t
            | None ->
                let anyRdf = types |> List.exists (fun t -> Set.contains t rdfScopeTypes)
                if anyRdf then NotAcceptable else PassThrough

module private Serializers =

    let notAcceptableBody =
        let supported = String.concat ", " AcceptNegotiation.supportedTypes
        $"Not Acceptable. Available representations: {supported}"

    let serializeGraphToString (writer: IRdfWriter) (graph: IGraph) : string =
        let sb = StringBuilder()
        use sw = new System.IO.StringWriter(sb)
        writer.Save(graph, sw :> System.IO.TextWriter)
        sb.ToString()

    let serializeTurtle (graph: IGraph) : string =
        serializeGraphToString (CompressingTurtleWriter()) graph

    let serializeRdfXml (graph: IGraph) : string =
        serializeGraphToString (RdfXmlWriter()) graph

    let serializeGraphJsonLd (graph: IGraph) : string =
        use store = new TripleStore()
        store.Add(graph) |> ignore
        let sb = StringBuilder()
        use sw = new System.IO.StringWriter(sb)
        let writer = JsonLdWriter()
        writer.Save(store :> ITripleStore, sw :> System.IO.TextWriter)
        sb.ToString()

    let buildJsonLdResponse (graph: IGraph) (externalContext: string) : string =
        let graphJson = serializeGraphJsonLd graph

        let contextElement =
            use doc = JsonDocument.Parse(externalContext)
            doc.RootElement.GetProperty("@context").Clone()

        let opts = JsonWriterOptions(Indented = false)
        use outStream = new MemoryStream()
        use jsonWriter = new Utf8JsonWriter(outStream, opts)
        jsonWriter.WriteStartObject()
        jsonWriter.WritePropertyName("@context")
        contextElement.WriteTo(jsonWriter)
        jsonWriter.WritePropertyName("@graph")

        try
            use graphDoc = JsonDocument.Parse(graphJson)
            graphDoc.RootElement.WriteTo(jsonWriter)
        with _ ->
            jsonWriter.WriteStartArray()
            jsonWriter.WriteEndArray()

        jsonWriter.WriteEndObject()
        jsonWriter.Flush()
        Encoding.UTF8.GetString(outStream.ToArray())

    let respond406 (ctx: HttpContext) : Task =
        ctx.Response.StatusCode <- 406
        ctx.Response.ContentType <- "text/plain"
        ctx.Response.WriteAsync(notAcceptableBody)

    let respondTurtle (graph: IGraph) (ctx: HttpContext) : Task =
        let body = serializeTurtle graph
        ctx.Response.StatusCode <- 200
        ctx.Response.ContentType <- "text/turtle"
        ctx.Response.WriteAsync(body)

    let respondRdfXml (graph: IGraph) (ctx: HttpContext) : Task =
        let body = serializeRdfXml graph
        ctx.Response.StatusCode <- 200
        ctx.Response.ContentType <- "application/rdf+xml"
        ctx.Response.WriteAsync(body)

    let respondJsonLd (graph: IGraph) (externalContext: string) (ctx: HttpContext) : Task =
        let body = buildJsonLdResponse graph externalContext
        ctx.Response.StatusCode <- 200
        ctx.Response.ContentType <- "application/ld+json"
        ctx.Response.WriteAsync(body)

/// Content-negotiation middleware serving a pre-built RDF graph in multiple
/// representations: application/ld+json, text/turtle, application/rdf+xml.
/// Non-RDF Accept headers pass through. Unsupported RDF-scoped Accept headers → 406.
type LinkedDataMiddleware(next: RequestDelegate, config: LinkedDataConfig, logger: ILogger<LinkedDataMiddleware>) =

    do
        if isNull (box config.Graph) then
            invalidArg (nameof config) "LinkedDataConfig.Graph must not be null"

        if String.IsNullOrWhiteSpace config.JsonLdContext then
            invalidArg (nameof config) "LinkedDataConfig.JsonLdContext must not be null or whitespace"

    member _.Invoke(ctx: HttpContext) : Task =
        let acceptHeader =
            match ctx.Request.Headers.TryGetValue "Accept" with
            | true, v -> v.ToString()
            | _ -> ""

        match AcceptNegotiation.negotiate acceptHeader with
        | AcceptNegotiation.PassThrough -> next.Invoke ctx
        | AcceptNegotiation.NotAcceptable ->
            logger.LogDebug("LinkedDataMiddleware: 406 for Accept: {Accept}", acceptHeader)
            Serializers.respond406 ctx
        | AcceptNegotiation.Serve mediaType ->
            logger.LogDebug("LinkedDataMiddleware: serving {MediaType}", mediaType)

            match mediaType with
            | "text/turtle" -> Serializers.respondTurtle config.Graph ctx
            | "application/rdf+xml" -> Serializers.respondRdfXml config.Graph ctx
            | "application/ld+json" -> Serializers.respondJsonLd config.Graph config.JsonLdContext ctx
            | _ -> next.Invoke ctx
