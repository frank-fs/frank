namespace Frank.Validation

open System
open System.Collections.Concurrent
open System.IO
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Features
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Primitives
open Microsoft.Net.Http.Headers
open VDS.RDF
open VDS.RDF.JsonLd
open VDS.RDF.Parsing
open VDS.RDF.Shacl.Validation
open Frank.Semantic

module private JsonLdBody =

    let isLdJson (ctx: HttpContext) =
        let ct = ctx.Request.ContentType

        match MediaTypeHeaderValue.TryParse(ct) with
        | true, parsed -> parsed.MediaType.Equals("application/ld+json", StringComparison.OrdinalIgnoreCase)
        | _ -> false

    let mergeGraphs (store: TripleStore) : IGraph =
        let merged = new Graph() :> IGraph

        for g in store.Graphs do
            merged.Merge(g) |> ignore

        merged

    /// Parses ld+json directly from the (already-buffered, seekable) request body stream —
    /// avoids allocating an intermediate string sized to body length. Caller MUST rewind
    /// the stream to position 0 AFTER this returns (not before) so the downstream handler
    /// still sees the full original body.
    let parseToGraph (loader: JsonLdDocumentLoader) (stream: Stream) : Result<IGraph, exn> =
        let options = JsonLdProcessorOptions()
        options.DocumentLoader <- loader
        let parser = JsonLdParser(options)

        try
            use store = new TripleStore()
            use reader = new StreamReader(stream, Encoding.UTF8, leaveOpen = true)
            parser.Load(store :> ITripleStore, reader)
            Ok(mergeGraphs store)
        with ex ->
            Error ex

    let private shaclContext = """{"@context":{"sh":"http://www.w3.org/ns/shacl#"}}"""

    let serializeReportJsonLd (graph: IGraph) : string =
        Frank.Semantic.RdfSerialization.serializeGraphJsonLdWithContext graph shaclContext

module private ValidationRespond =

    let respond400 (detail: string) (ctx: HttpContext) : Task =
        Frank.ProblemJson.write ctx 400 "about:blank" "Bad Request" detail

    let respond422 (reportJsonLd: string) (ctx: HttpContext) : Task =
        ctx.Response.StatusCode <- 422
        ctx.Response.ContentType <- "application/ld+json; profile=\"http://www.w3.org/ns/shacl#\""
        let linkValue = "<http://www.w3.org/ns/shacl#>; rel=\"describedby\""
        ctx.Response.Headers.Append("Link", StringValues(linkValue))
        ctx.Response.WriteAsync(reportJsonLd)

module private HostRelative =

    let private resolveProps (props: (Uri * string * string option) list) (origin: string) : ShapeDecl list =
        props
        |> List.map (fun (classUri, relPath, pattern) ->
            RecordShape(
                classUri,
                [ { Path = Uri(origin + relPath)
                    Datatype = None
                    MinCount = 1
                    MaxCount = Some 1
                    Pattern = pattern } ]
            ))

    /// Builds (or reuses) the origin-keyed host-relative ShapesGraph. `cache`/`onBuild` are
    /// owned by the calling ValidationMiddleware instance (#382) so this function stays free of
    /// module-level mutable state — the caller supplies its caching policy explicitly (Rule 13).
    /// The `Lazy` value under each dictionary key guarantees `Shapes.toShapesGraph` runs at most
    /// once per origin, even if two requests race on a brand-new origin simultaneously.
    let private getOrBuildShapesGraph
        (cache: ConcurrentDictionary<string, Lazy<VDS.RDF.Shacl.ShapesGraph>>)
        (onBuild: unit -> unit)
        (props: (Uri * string * string option) list)
        (origin: string)
        : VDS.RDF.Shacl.ShapesGraph =
        cache
            .GetOrAdd(
                origin,
                (fun o ->
                    Lazy<VDS.RDF.Shacl.ShapesGraph>(fun () ->
                        onBuild ()
                        Shapes.toShapesGraph (resolveProps props o)))
            )
            .Value

    let validateDynamic
        (cache: ConcurrentDictionary<string, Lazy<VDS.RDF.Shacl.ShapesGraph>>)
        (onBuild: unit -> unit)
        (props: (Uri * string * string option) list)
        (origin: string)
        (data: IGraph)
        : Report option =
        if props.IsEmpty then
            None
        else
            let sg = getOrBuildShapesGraph cache onBuild props origin
            Some(Validator.validate sg data)

type ValidationMiddleware(next: RequestDelegate, config: ValidationConfig, logger: ILogger<ValidationMiddleware>) =

    do
        if isNull (box config.Shapes) then
            invalidArg (nameof config) "ValidationConfig.Shapes must not be null"

        if isNull (box config.ContextLoader) then
            invalidArg (nameof config) "ValidationConfig.ContextLoader must not be null"

        if config.MaxBodyBytes <= 0L then
            invalidArg (nameof config) "ValidationConfig.MaxBodyBytes must be positive"

    /// Host-relative ShapesGraph cache, one entry per distinct request origin. Bounded in
    /// practice: the host set behind a single app is tiny (issue #382), and entries live for the
    /// process lifetime of this (singleton, per-pipeline) middleware instance — no manual
    /// eviction needed.
    let hostRelativeShapesCache =
        ConcurrentDictionary<string, Lazy<VDS.RDF.Shacl.ShapesGraph>>()

    let mutable hostRelativeShapesBuildCount = 0

    let validateAndRespond (origin: string) (ctx: HttpContext) (data: IGraph) : Task =
        use _ = data
        let staticReport = Validator.validate config.Shapes data

        if not staticReport.Conforms then
            logger.LogDebug("ValidationMiddleware: static shapes reject body, returning 422")
            ValidationRespond.respond422 (JsonLdBody.serializeReportJsonLd staticReport.Normalised) ctx
        else
            let dynReport =
                HostRelative.validateDynamic
                    hostRelativeShapesCache
                    (fun () -> System.Threading.Interlocked.Increment(&hostRelativeShapesBuildCount) |> ignore)
                    config.HostRelativeProperties
                    origin
                    data

            match dynReport with
            | None ->
                logger.LogDebug("ValidationMiddleware: body conforms, passing through")
                next.Invoke ctx
            | Some r when r.Conforms ->
                logger.LogDebug("ValidationMiddleware: body conforms, passing through")
                next.Invoke ctx
            | Some r ->
                logger.LogDebug("ValidationMiddleware: host-relative shapes reject body, returning 422")
                ValidationRespond.respond422 (JsonLdBody.serializeReportJsonLd r.Normalised) ctx

    /// Test-only visibility (internal + InternalsVisibleTo, #392 pattern): number of times the
    /// host-relative ShapesGraph was actually rebuilt — proves build-once-per-origin under
    /// repeated requests to the same host (issue #382).
    member internal _.HostRelativeShapesBuildCount = hostRelativeShapesBuildCount

    member private _.InvokeCore(ctx: HttpContext, origin: string) : Task =
        if not (JsonLdBody.isLdJson ctx) then
            next.Invoke ctx
        else
            task {
                Frank.RequestBodyBuffer.enable config.MaxBodyBytes ctx.Request

                // JsonLdParser.Load reads the TextReader synchronously (no async API in
                // dotNetRdf) — Kestrel/TestServer disallow synchronous body reads by default.
                match ctx.Features.Get<IHttpBodyControlFeature>() with
                | null -> ()
                | feature -> feature.AllowSynchronousIO <- true

                match JsonLdBody.parseToGraph config.ContextLoader ctx.Request.Body with
                | Error(:? IOException as ex) ->
                    // Buffer-limit-exceeded disposes the underlying buffering stream — do not
                    // attempt to rewind it (the stream is already gone at this point).
                    logger.LogDebug(ex, "ValidationMiddleware: body exceeded MaxBodyBytes limit")
                    do! Frank.RequestBodyBuffer.respond413 ctx
                | Error ex ->
                    ctx.Request.Body.Position <- 0L
                    logger.LogDebug(ex, "ValidationMiddleware: failed to parse ld+json body")
                    do! ValidationRespond.respond400 ex.Message ctx
                | Ok data ->
                    ctx.Request.Body.Position <- 0L
                    do! validateAndRespond origin ctx data
            }

    member this.InvokeAsync(ctx: HttpContext) : Task =
        match Frank.OriginValidation.tryValidateOrigin ctx.Request with
        | None ->
            logger.LogWarning(
                "ValidationMiddleware: malformed Host header '{Host}' — cannot mint host-relative IRIs, rejecting with 400",
                ctx.Request.Host.Value
            )

            ctx.Response.StatusCode <- 400
            Task.CompletedTask
        | Some origin -> this.InvokeCore(ctx, origin)
