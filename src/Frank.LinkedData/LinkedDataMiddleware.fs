namespace Frank.LinkedData

open System
open System.IO
open System.Runtime.CompilerServices
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Caching.Memory
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

        // Only prefixes whose namespace IRI is under the response's own origin belong in the
        // inline @context[0] object — an external prefix (e.g. schema.org's own domain) must
        // resolve solely via the remote @context array element, not a locally-declared shortcut
        // that would make it always resolvable offline (#394). Precondition on the caller: every
        // off-origin namespace prefix registered on the graph must be covered by a document
        // referenced in externalContext's @context array, or its compact IRIs will be served
        // with no definition anywhere (neither inline nor remote) — see linkedDataGraphWith's
        // JsonLdContext field.
        let localPrefixPairs =
            prefixPairs
            |> List.filter (fun (_, iri) -> Frank.Semantic.VocabClassifier.isOwnedByAuthority base' iri)

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

        for prefix, iri in localPrefixPairs do
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
        Frank.AcceptNegotiation.appendVaryAccept ctx.Response
        Frank.ProblemJson.write ctx 406 "about:blank" "Not Acceptable" notAcceptableBody

    /// When graph.BaseUri is set the writer already emitted @base; avoid duplicating it.
    let private turtleBody (graph: IGraph) (origin: string) : string =
        let serialized = serializeTurtle graph

        if isNull (box graph.BaseUri) then
            "@base <" + origin + "> .\n" + serialized
        else
            serialized

    /// Pure per-media-type body builder — deterministic given (mediaType, graph, jsonLdContext,
    /// origin). Callers decide whether the result is cacheable (static Graph) or must be
    /// recomputed every call (dynamic GraphFactory) — see LinkedDataMiddleware.ServeRdf (#382).
    let bodyFor (mediaType: string) (graph: IGraph) (jsonLdContext: string) (origin: string) : string =
        match mediaType with
        | "text/turtle" -> turtleBody graph origin
        | "application/rdf+xml" -> serializeRdfXml graph
        | "application/ld+json" -> buildJsonLdResponse graph jsonLdContext origin
        | other -> invalidArg (nameof mediaType) $"unsupported media type: {other}"

    let respondWith (mediaType: string) (body: string) (ctx: HttpContext) : Task =
        Frank.AcceptNegotiation.appendVaryAccept ctx.Response
        ctx.Response.StatusCode <- 200
        ctx.Response.ContentType <- mediaType
        ctx.Response.WriteAsync(body)

/// #468: staticBodyCache's key. Serialized static-graph bodies for the GraphFactory=None
/// branch are keyed by the OWNING LinkedDataConfig's REFERENCE identity (endpoints are
/// configured once at startup and live for the app's lifetime — the prior
/// ConditionalWeakTable partitioned by this same identity) crossed with (origin, mediaType)
/// — `origin` is derived from the request's own `Host` header
/// (Frank.OriginValidation.tryValidateOrigin validates only SYNTACTIC well-formedness,
/// never an allowlist), so an unauthenticated client varying Host must not be able to mint
/// unbounded permanent entries. LinkedDataConfig is an F# record carrying a function-typed
/// field (GraphFactory), so it does NOT support F#'s structural equality at all (verified:
/// `config1 = config2` fails to compile with FS0001) — this key type never invokes
/// LinkedDataConfig's own equality, only Config's REFERENCE identity (mirroring
/// ConditionalWeakTable's own reference-equality partitioning) combined with structural
/// equality on Origin/MediaType. NoComparison is required alongside CustomEquality: without
/// it F# would try to auto-derive IComparable too, which fails to compile for the same
/// function-field reason.
///
/// ACCEPTED TRADE-OFF (#468 expert-review Fowler-minor): staticBodyCache is now ONE keyed
/// IMemoryCache region (SizeLimit = Frank.Builder.CacheCapacity) SHARED by every distinct
/// LinkedDataConfig a LinkedDataMiddleware instance serves — before #468, ConditionalWeakTable
/// gave EACH LinkedDataConfig its OWN inner BoundedCache, an independent per-config budget.
/// Folding Config identity into the key (above) instead of keeping per-config partitioning
/// was deliberate: dynamic per-config keyed-DI registration isn't feasible — LinkedDataConfig
/// instances aren't known until each `resource {}` CE block registers an endpoint, which
/// happens AFTER Builder.fs's static AddKeyedSingleton registrations already ran. The
/// consequence, stated plainly: a flood against ONE config's origin-space CAN evict entries
/// belonging to a DIFFERENT config once the app's combined distinct-key count (summed across
/// every LinkedDataConfig) exceeds the one shared capacity — apps with many distinct
/// LinkedDataConfigs (many `resource {}` blocks each with their own graph) share a single
/// 1000-entry budget, not 1000 per config. What is NOT compromised: VALUE isolation — the
/// ReferenceEquals-on-Config check above means config A's cached body for origin X can never
/// be returned for config B's request to the same origin X, regardless of budget sharing.
/// Proven by test/Frank.LinkedData.Tests/SharedCacheBudgetTests.fs (both halves: shared-budget
/// eviction across configs, and per-config value correctness under that shared budget).
[<Struct; CustomEquality; NoComparison>]
type private StaticBodyCacheKey =
    { Config: LinkedDataConfig
      Origin: string
      MediaType: string }

    override this.Equals(other) =
        match other with
        | :? StaticBodyCacheKey as o ->
            obj.ReferenceEquals(this.Config, o.Config)
            && this.Origin = o.Origin
            && this.MediaType = o.MediaType
        | _ -> false

    /// Combines the three components directly (no intermediate tuple allocation) — this
    /// runs on every cachedStaticBody call, hit or miss, so avoiding the extra heap
    /// allocation a tuple would need matters on this hot path (#468 /simplify finding).
    override this.GetHashCode() =
        let mutable h = RuntimeHelpers.GetHashCode this.Config
        h <- h * 397 ^^^ this.Origin.GetHashCode()
        h <- h * 397 ^^^ this.MediaType.GetHashCode()
        h

/// Content-negotiation middleware serving per-endpoint RDF graphs in multiple
/// representations: application/ld+json, text/turtle, application/rdf+xml.
/// Only fires for GET/HEAD (safe-method guard) on endpoints that carry a
/// LinkedDataConfig in their metadata. All other requests pass through.
type LinkedDataMiddleware
    (
        next: RequestDelegate,
        logger: ILogger<LinkedDataMiddleware>,
        vocabularyConfig: LinkedDataVocabularyConfig,
        [<Microsoft.Extensions.DependencyInjection.FromKeyedServices("linkeddata:staticbody")>] staticBodyCache:
            IMemoryCache
    ) =

    let staticBodyLocks = Frank.StripedLocks(Frank.CacheStriping.DefaultStripeCount)
    let mutable staticBodyBuildCount = 0

    let cachedStaticBody
        (config: LinkedDataConfig)
        (origin: string)
        (mediaType: string)
        (build: unit -> string)
        : string =
        let key =
            { Config = config
              Origin = origin
              MediaType = mediaType }

        Frank.CacheStriping.getOrBuild staticBodyLocks staticBodyCache key (fun () ->
            System.Threading.Interlocked.Increment(&staticBodyBuildCount) |> ignore
            build ())

    let computeBody (mediaType: string) (origin: string) (effective: LinkedDataConfig) (ctx: HttpContext) : string =
        match effective.GraphFactory with
        | Some factory -> Serializers.bodyFor mediaType (factory ctx) effective.JsonLdContext origin
        | None ->
            cachedStaticBody effective origin mediaType (fun () ->
                Serializers.bodyFor mediaType effective.Graph effective.JsonLdContext origin)

    /// Test-only visibility (internal + InternalsVisibleTo, #392 pattern): number of times a
    /// static-graph body was actually (re)built — proves build-once-per-(origin,mediaType) for
    /// the GraphFactory=None branch (issue #382).
    member internal _.StaticBodyBuildCount = staticBodyBuildCount

    /// Test-only visibility (internal + InternalsVisibleTo, #392 pattern): number of distinct
    /// (config, origin, mediaType) entries currently retained across this middleware's ONE
    /// shared static-body cache — proves the Host-header-flood cache-DoS fix (#468,
    /// originally #405): bounded at Frank.Builder.CacheCapacity regardless of how many
    /// distinct Host header values a client sends. Reads the concrete MemoryCache's real
    /// Count. #468 folds LinkedDataConfig identity INTO the cache key (StaticBodyCacheKey)
    /// rather than partitioning into one inner cache per config (ConditionalWeakTable's old
    /// shape) — so this reports the WHOLE cache's size, not one config's slice; every
    /// existing call site only ever exercises a single config, where the two numbers
    /// coincide.
    member internal _.StaticBodyCacheSize = (staticBodyCache :?> MemoryCache).Count

    /// Test-only visibility (internal + InternalsVisibleTo, #392 pattern): drives the
    /// GraphFactory=None static-body hot path directly (bypassing HTTP request/response
    /// plumbing — ctx is never touched on this branch) so an allocation-delta test can
    /// isolate exactly what cachedStaticBody's cache-HIT path itself allocates, without the
    /// unrelated noise of DefaultHttpContext/response-writing (#468 Fowler-important
    /// finding: CacheStriping.getOrBuild's `cache.TryGetValue(box key)` boxes the
    /// [<Struct>] StaticBodyCacheKey on every call, hit or miss — inherent to
    /// IMemoryCache's `object`-keyed API, not eliminable without diverging from
    /// constructor-injected IMemoryCache).
    member internal _.ComputeStaticBodyForTest(mediaType: string, origin: string, config: LinkedDataConfig) : string =
        computeBody mediaType origin config Unchecked.defaultof<HttpContext>

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

            match mediaType with
            | "text/turtle"
            | "application/rdf+xml"
            | "application/ld+json" ->
                Serializers.respondWith mediaType (computeBody mediaType origin effective ctx) ctx
            | _ -> next.Invoke ctx

    member this.InvokeAsync(ctx: HttpContext) : Task =
        let method = ctx.Request.Method

        if not (HttpMethods.IsGet method || HttpMethods.IsHead method) then
            next.Invoke ctx
        else

            let endpointConfig =
                match ctx.GetEndpoint() with
                | null -> None
                | ep ->
                    let meta = ep.Metadata.GetMetadata<LinkedDataConfig>()
                    if isNull (box meta) then None else Some meta

            match endpointConfig with
            | None -> next.Invoke ctx
            | Some effective ->
                // #420 expert-review follow-up: emitted for every safe-method response on any
                // endpoint carrying LinkedDataConfig metadata, BEFORE representation negotiation,
                // so it appears on Serve/PassThrough/NotAcceptable alike — including the naive
                // plain-JSON/no-Accept client the #420 thesis targets (finding 3).
                vocabularyConfig.VocabularyRoute
                |> Option.iter (fun route ->
                    ctx.Response.Headers.Append(
                        "Link",
                        $"<{route}>; rel=\"describedby\"; type=\"application/ld+json\""
                    ))

                let acceptHeader =
                    match ctx.Request.Headers.TryGetValue "Accept" with
                    | true, v -> v.ToString()
                    | _ -> ""

                match AcceptNegotiation.negotiate acceptHeader with
                | AcceptNegotiation.PassThrough -> next.Invoke ctx
                | AcceptNegotiation.NotAcceptable ->
                    logger.LogDebug("LinkedDataMiddleware: 406 for Accept: {Accept}", acceptHeader)
                    Serializers.respond406 ctx
                | AcceptNegotiation.Serve mediaType -> this.ServeRdf(ctx, mediaType, effective)
