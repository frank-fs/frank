namespace Frank.LinkedData

open System
open System.IO
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging
open Microsoft.Net.Http.Headers
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

    type NegotiationResult =
        | Serve of mediaType: string
        | NotAcceptable
        | PassThrough

    /// Returns true if the Accept entry is a concrete (non-wildcard) type/subtype.
    let private isConcrete (entry: MediaTypeHeaderValue) =
        entry.Type.Value <> "*" && entry.SubType.Value <> "*"

    /// Returns true if the Accept entry (which may be a wildcard) matches the candidate media type.
    let private matchesType (entry: MediaTypeHeaderValue) (candidate: string) =
        let slash = candidate.IndexOf('/')
        let mainType = candidate.[.. slash - 1]
        let subType = candidate.[slash + 1 ..]
        let eMain = entry.Type.Value
        let eSub = entry.SubType.Value

        (eMain = "*" && eSub = "*")
        || (eMain = mainType && eSub = "*")
        || (eMain = mainType && eSub = subType)

    /// Returns true if the entry carries a non-empty `profile` parameter.
    /// RFC 6906 / JSON-LD: profile is a separate concern from the base media type.
    /// LinkedData has no profile of its own, so a profiled ld+json request is not ours to serve.
    let private hasProfileParam (entry: MediaTypeHeaderValue) =
        entry.Parameters
        |> Seq.exists (fun p ->
            p.Name.Value.Equals("profile", StringComparison.OrdinalIgnoreCase)
            && not (String.IsNullOrEmpty p.Value.Value))

    /// Parse Accept header into (mediaType, q) pairs sorted by q descending (then by header order for ties).
    /// q=0 entries are retained so callers can apply exclusions.
    let private parseAcceptWithQ (acceptHeader: string) : (MediaTypeHeaderValue * double) list =
        let entries =
            MediaTypeHeaderValue.ParseList(Collections.Generic.List([ acceptHeader ]))

        entries
        |> Seq.mapi (fun i e ->
            let q = if e.Quality.HasValue then e.Quality.Value else 1.0
            (e, q, i))
        |> Seq.sortWith (fun (_, q1, i1) (_, q2, i2) ->
            let cq = compare q2 q1
            if cq <> 0 then cq else compare i1 i2)
        |> Seq.map (fun (e, q, _) -> (e, q))
        |> Seq.toList

    /// Returns true if the candidate is excluded by any q=0 entry in the list.
    let private isExcluded (entries: (MediaTypeHeaderValue * double) list) (candidate: string) =
        entries |> List.exists (fun (e, q) -> q = 0.0 && matchesType e candidate)

    let private isRdfMentioned (entry: MediaTypeHeaderValue) : bool =
        isConcrete entry
        && rdfScopeTypes
           |> Set.exists (fun candidate ->
               matchesType entry candidate
               && not (candidate = "application/ld+json" && hasProfileParam entry))

    let negotiate (acceptHeader: string) : NegotiationResult =
        if String.IsNullOrEmpty acceptHeader then
            PassThrough
        else
            let entries = parseAcceptWithQ acceptHeader
            let concreteNonZero = entries |> List.filter (fun (e, q) -> q > 0.0 && isConcrete e)

            let bestSupported =
                concreteNonZero
                |> List.tryPick (fun (entry, _) ->
                    supportedTypes
                    |> Array.tryFind (fun candidate ->
                        matchesType entry candidate
                        && not (isExcluded entries candidate)
                        && not (candidate = "application/ld+json" && hasProfileParam entry)))

            match bestSupported with
            | Some t -> Serve t
            | None ->
                if entries |> List.exists (fun (entry, _) -> isRdfMentioned entry) then
                    NotAcceptable
                else
                    PassThrough

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
        Frank.Semantic.RdfSerialization.serializeGraphJsonLd graph

    let private collectNamespacePairs (graph: IGraph) : (string * string) list =
        [ for prefix in graph.NamespaceMap.Prefixes do
              yield prefix, (graph.NamespaceMap.GetNamespaceUri prefix).AbsoluteUri ]

    /// Write the @graph array from a compacted JSON-LD string.
    /// Handles both multi-node (@graph array) and single-node (root object) compacted forms.
    let private writeCompactedGraph (jsonWriter: Utf8JsonWriter) (compactedJson: string) : unit =
        use doc = JsonDocument.Parse compactedJson
        let root = doc.RootElement
        let mutable graphEl = Unchecked.defaultof<JsonElement>

        if root.TryGetProperty("@graph", &graphEl) then
            graphEl.WriteTo jsonWriter
        else
            jsonWriter.WriteStartArray()
            jsonWriter.WriteStartObject()

            for prop in root.EnumerateObject() do
                if prop.Name <> "@context" then
                    jsonWriter.WritePropertyName prop.Name
                    prop.Value.WriteTo jsonWriter

            jsonWriter.WriteEndObject()
            jsonWriter.WriteEndArray()

    let buildJsonLdResponse (graph: IGraph) (externalContext: string) (base': string) : string =
        let prefixPairs = collectNamespacePairs graph

        let compactedJson =
            Frank.Semantic.RdfSerialization.compactGraphJsonLd graph prefixPairs base'

        let contextElement =
            use doc = JsonDocument.Parse externalContext
            doc.RootElement.GetProperty("@context").Clone()

        let opts = JsonWriterOptions(Indented = false)
        use outStream = new MemoryStream()
        use jsonWriter = new Utf8JsonWriter(outStream, opts)
        jsonWriter.WriteStartObject()
        jsonWriter.WritePropertyName "@context"
        jsonWriter.WriteStartArray()
        jsonWriter.WriteStartObject()
        jsonWriter.WriteString("@base", base')

        for prefix, iri in prefixPairs do
            jsonWriter.WriteString(prefix, iri)

        jsonWriter.WriteEndObject()

        match contextElement.ValueKind with
        | JsonValueKind.Array ->
            for el in contextElement.EnumerateArray() do
                el.WriteTo jsonWriter
        | _ -> contextElement.WriteTo jsonWriter

        jsonWriter.WriteEndArray()
        jsonWriter.WritePropertyName "@graph"
        writeCompactedGraph jsonWriter compactedJson
        jsonWriter.WriteEndObject()
        jsonWriter.Flush()
        Encoding.UTF8.GetString(outStream.ToArray())

    let respond406 (ctx: HttpContext) : Task =
        ctx.Response.Headers.Append("Vary", "Accept")
        ctx.Response.StatusCode <- 406
        ctx.Response.ContentType <- "text/plain"
        ctx.Response.WriteAsync(notAcceptableBody)

    let respondTurtle (graph: IGraph) (origin: string) (ctx: HttpContext) : Task =
        let serialized = serializeTurtle graph
        // When graph.BaseUri is set the writer already emitted @base; avoid duplicating it.
        let body =
            if isNull (box graph.BaseUri) then
                "@base <" + origin + "> .\n" + serialized
            else
                serialized

        ctx.Response.Headers.Append("Vary", "Accept")
        ctx.Response.StatusCode <- 200
        ctx.Response.ContentType <- "text/turtle"
        ctx.Response.WriteAsync(body)

    let respondRdfXml (graph: IGraph) (ctx: HttpContext) : Task =
        let body = serializeRdfXml graph
        ctx.Response.Headers.Append("Vary", "Accept")
        ctx.Response.StatusCode <- 200
        ctx.Response.ContentType <- "application/rdf+xml"
        ctx.Response.WriteAsync(body)

    let respondJsonLd (graph: IGraph) (externalContext: string) (base': string) (ctx: HttpContext) : Task =
        let body = buildJsonLdResponse graph externalContext base'
        ctx.Response.Headers.Append("Vary", "Accept")
        ctx.Response.StatusCode <- 200
        ctx.Response.ContentType <- "application/ld+json"
        ctx.Response.WriteAsync(body)

/// Content-negotiation middleware serving per-endpoint RDF graphs in multiple
/// representations: application/ld+json, text/turtle, application/rdf+xml.
/// Only fires for GET/HEAD (safe-method guard) on endpoints that carry a
/// LinkedDataConfig in their metadata. All other requests pass through.
type LinkedDataMiddleware(next: RequestDelegate, logger: ILogger<LinkedDataMiddleware>) =

    member private this.ServeRdf(ctx: HttpContext, mediaType: string, effective: LinkedDataConfig) : Task =
        match Frank.OriginValidation.tryValidateOrigin ctx.Request with
        | None ->
            logger.LogWarning(
                "LinkedDataMiddleware: malformed Host header '{Host}' — cannot mint resource IRIs, rejecting with 400",
                ctx.Request.Host.Value
            )

            ctx.Response.StatusCode <- 400
            Task.CompletedTask
        | Some origin ->
            logger.LogDebug("LinkedDataMiddleware: serving {MediaType}", mediaType)

            let effectiveGraph =
                match effective.GraphFactory with
                | Some factory -> factory ctx
                | None -> effective.Graph

            match mediaType with
            | "text/turtle" -> Serializers.respondTurtle effectiveGraph origin ctx
            | "application/rdf+xml" -> Serializers.respondRdfXml effectiveGraph ctx
            | "application/ld+json" -> Serializers.respondJsonLd effectiveGraph effective.JsonLdContext origin ctx
            | _ -> next.Invoke ctx

    member this.InvokeAsync(ctx: HttpContext) : Task =
        let method = ctx.Request.Method

        if not (HttpMethods.IsGet method || HttpMethods.IsHead method) then
            next.Invoke ctx
        else

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
                let endpointConfig =
                    match ctx.GetEndpoint() with
                    | null -> None
                    | ep ->
                        let meta = ep.Metadata.GetMetadata<LinkedDataConfig>()
                        if isNull (box meta) then None else Some meta

                match endpointConfig with
                | None -> next.Invoke ctx
                | Some effective -> this.ServeRdf(ctx, mediaType, effective)
