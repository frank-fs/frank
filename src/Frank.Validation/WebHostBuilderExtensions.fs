namespace Frank.Validation

open System
open System.Text
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging
open Frank.Builder
open Frank.Rdf

[<AutoOpen>]
module WebHostBuilderExtensions =
    let internal ValidatedGraphKey = "Frank.Validation.ParsedGraph"

    [<Literal>]
    let private MaxBodyBytes = 1_048_576L // 1 MiB

    [<Literal>]
    let private oversizedDetail =
        "The request body exceeds the 1 MiB limit this validating middleware buffers."

    let private isValidatedMethod (method: string) =
        HttpMethods.IsPost method
        || HttpMethods.IsPut method
        || HttpMethods.IsPatch method

    let private isLdJson (contentType: string) =
        not (isNull contentType)
        && contentType.StartsWith("application/ld+json", StringComparison.OrdinalIgnoreCase)

    let private parseGraph (bodyText: string) : Result<VDS.RDF.IGraph, string> =
        try
            let store = new VDS.RDF.TripleStore()
            use bodyReader = new System.IO.StringReader(bodyText)
            (new VDS.RDF.Parsing.JsonLdParser()).Load(store, bodyReader)
            let dataGraph = new VDS.RDF.Graph() :> VDS.RDF.IGraph

            for g in store.Graphs do
                dataGraph.Merge(g)

            Ok dataGraph
        with ex ->
            Error ex.Message

    let private writeProblemJson (ctx: HttpContext) (statusCode: int) (title: string) (detail: string) : Task =
        ctx.Response.StatusCode <- statusCode
        // WriteAsJsonAsync always sets its own Content-Type ("application/json; charset=utf-8" by
        // default), overwriting anything assigned to ctx.Response.ContentType beforehand -- verified
        // directly (a prior `ctx.Response.ContentType <- "application/problem+json"` here was silently
        // clobbered). The 4-arg overload's explicit `contentType` parameter is the only way to make
        // application/problem+json stick.
        ctx.Response.WriteAsJsonAsync(
            {| ``type`` = "about:blank"
               title = title
               status = statusCode
               detail = detail |},
            (null: System.Text.Json.JsonSerializerOptions),
            "application/problem+json"
        )

    let private writeViolationResponse (ctx: HttpContext) (violations: Violation list) : Task =
        task {
            ctx.Response.StatusCode <- 422

            let acceptsLdJson =
                ctx.Request.Headers.Accept.ToString().Contains("application/ld+json")

            if acceptsLdJson then
                ctx.Response.ContentType <- "application/ld+json"

                // Doc.writeJsonLd writes to a TextWriter via synchronous TextWriter.Write calls
                // (it's a plain synchronous API -- see Frank.Rdf's Rdf.fsi). Wrapping ctx.Response.Body
                // in a StreamWriter and calling it directly, as the task brief's snippet does, throws at
                // request time: "System.InvalidOperationException: Synchronous operations are
                // disallowed. Call WriteAsync or set AllowSynchronousIO to true." -- verified directly
                // against Kestrel/TestServer's default (AllowSynchronousIO = false). Buffering through
                // Doc.toJsonLd (a string) and writing it with the async HttpResponse.WriteAsync sidesteps
                // the disallowed synchronous write entirely; validation reports are small (one paragraph
                // per violation), so the "avoid materializing as a string" optimization Doc.writeJsonLd's
                // own doc comment recommends isn't worth reaching for AllowSynchronousIO (a discouraged,
                // thread-pool-starving escape hatch) here.
                do! ctx.Response.WriteAsync(Doc.toJsonLd (Shacl.reportToDoc violations))
            else
                let payload =
                    {| ``type`` = "https://www.w3.org/TR/shacl/#validation-report"
                       title = "SHACL validation failed"
                       status = 422
                       violations =
                        violations
                        |> List.map (fun v ->
                            {| focusNode =
                                (match v.FocusNode with
                                 | Value.Node(Node.Iri s) -> s
                                 | Value.Node(Node.Blank b) -> "_:" + b
                                 // A literal focus node -- sh:targetObjectsOf targets objects, which
                                 // are routinely literals. Rendered as its lexical form.
                                 | Value.Literal(Literal.String s) -> s
                                 | Value.Literal(Literal.LangString(s, lang)) -> s + "@" + lang
                                 | Value.Literal(Literal.Int i) -> string i
                                 | Value.Literal(Literal.Bool b) -> (if b then "true" else "false")
                                 | Value.Literal(Literal.DateTime dt) -> dt.ToString "o")
                               resultPath = v.ResultPath |> Option.map string
                               severity = string v.Severity
                               message = v.Message
                               constraintComponent = v.ConstraintComponent.AbsoluteUri |}) |}

                // Same WriteAsJsonAsync content-type-override caveat as writeProblemJson -- the
                // explicit contentType argument is required, an upfront ContentType assignment is not
                // enough.
                do!
                    ctx.Response.WriteAsJsonAsync(
                        payload,
                        (null: System.Text.Json.JsonSerializerOptions),
                        "application/problem+json"
                    )
        }
        :> Task

    /// Everything from "we decided to intercept this request" through "we answered or handed off to
    /// the handler". Split out of useValidationMiddleware so the whole RDF/SHACL block can sit behind
    /// one exception boundary (final-review findings C1/I1/I7): dotNetRDF raises from several places
    /// this code cannot pre-validate -- a fabricated IRI reaching Frank.Rdf's resolveIri while
    /// serializing a report, an RdfQueryTimeoutException out of the SPARQL engine under load, a
    /// malformed sh:sparql query that escaped toShapesGraph's build-time check. Any of those used to
    /// propagate as an unhandled exception (a bare 500 with no body, or a torn response); they now
    /// become a logged 500 application/problem+json.
    let private runValidation
        (ctx: HttpContext)
        (next: RequestDelegate)
        (shapesGraph: VDS.RDF.Shacl.ShapesGraph)
        : Task =
        task {
            if not (isValidatedMethod ctx.Request.Method && isLdJson ctx.Request.ContentType) then
                do! next.Invoke ctx
            elif
                ctx.Request.ContentLength.HasValue
                && ctx.Request.ContentLength.Value > MaxBodyBytes
            then
                do! writeProblemJson ctx 413 "Request body too large" oversizedDetail
            else
                // EnableBuffering(bufferThreshold, bufferLimit) -- not the parameterless
                // overload -- is what actually bounds memory use. The parameterless
                // EnableBuffering() defaults bufferLimit to null (unlimited), so a
                // chunked-transfer request with no Content-Length header sails past the
                // fast-path check above and gets fully materialized into a managed string
                // by reader.ReadToEndAsync() before the post-read byte-count check below
                // ever runs -- the ContentLength check is honest-header-only, not a real
                // bound (the design doc calls for checking "against a running byte count
                // while reading" for exactly this reason). Verified directly via a
                // `dotnet fsi` repro against the underlying
                // Microsoft.AspNetCore.WebUtilities.FileBufferingReadStream (what
                // EnableBuffering wires up internally): feeding it an unbounded source
                // stream with bufferLimit = MaxBodyBytes + 1L throws `IOException: Buffer
                // limit exceeded.` after reading right around that many bytes -- so the
                // read itself is capped, not merely checked afterward.
                ctx.Request.EnableBuffering(bufferThreshold = 32 * 1024, bufferLimit = MaxBodyBytes + 1L)

                use reader =
                    new System.IO.StreamReader(ctx.Request.Body, Encoding.UTF8, leaveOpen = true)

                let! bodyTextOrOversized =
                    task {
                        try
                            let! text = reader.ReadToEndAsync()
                            return Ok text
                        with :? System.IO.IOException ->
                            return Error()
                    }

                match bodyTextOrOversized with
                | Error() -> do! writeProblemJson ctx 413 "Request body too large" oversizedDetail
                | Ok bodyText ->
                    ctx.Request.Body.Position <- 0L

                    // Belt-and-braces: EnableBuffering's bufferLimit already caps the read
                    // itself (see above), but keep this as the final authoritative gate in
                    // case the buffered stream's throw boundary ever admits a body a few
                    // bytes over MaxBodyBytes before throwing.
                    if int64 (Encoding.UTF8.GetByteCount bodyText) > MaxBodyBytes then
                        do! writeProblemJson ctx 413 "Request body too large" oversizedDetail
                    else
                        match parseGraph bodyText with
                        | Error message -> do! writeProblemJson ctx 400 "Malformed JSON-LD" message
                        | Ok dataGraph ->
                            match Shacl.validate shapesGraph dataGraph with
                            | ValidationOutcome.Conforms ->
                                ctx.Items.[ValidatedGraphKey] <- box dataGraph
                                do! next.Invoke ctx
                            | ValidationOutcome.Violates violations -> do! writeViolationResponse ctx violations
        }
        :> Task

    let internal useValidationMiddleware (app: IApplicationBuilder) : IApplicationBuilder =
        app.Use(fun (ctx: HttpContext) (next: RequestDelegate) ->
            task {
                match ctx.GetEndpoint() with
                | null -> do! next.Invoke ctx
                | endpoint ->
                    let metadata = endpoint.Metadata.GetMetadata<ValidationMetadata>()

                    // GetMetadata<T>() returns default(T) (null, for a reference type like this
                    // single-case DU) when absent. F# doesn't allow matching a plain DU against a
                    // `null` pattern directly (FS0043 -- ValidationMetadata has no proper null
                    // value), so the null-ness has to be tested via `isNull (box metadata)` first,
                    // then the DU deconstructed only once known non-null. Verified directly: a
                    // `match ... with | null -> ... | ValidationMetadata x -> ...` shape (as
                    // sketched in the task brief) fails to compile for a non-[<AllowNullLiteral>]
                    // union type.
                    if isNull (box metadata) then
                        do! next.Invoke ctx
                    else
                        let (ValidationMetadata shapesGraph) = metadata

                        // The exception boundary. Everything the RDF/SHACL layer can raise --
                        // resolveIri on a value it can't render, RdfQueryTimeoutException out of the
                        // SPARQL engine under concurrency, anything else dotNetRDF throws -- becomes
                        // a logged 500 application/problem+json instead of an unhandled crash.
                        //
                        // It deliberately does NOT swallow the downstream handler's own exceptions:
                        // a handler failure is the application's to report (via its own exception
                        // handler / ProblemDetails middleware), not this middleware's to relabel as
                        // a validation error. runValidation invokes `next` itself on the conforming
                        // path, so the two are told apart by a flag set the moment control crosses
                        // into the handler -- the `when not handlerEntered` filter rethrows in that
                        // case, leaving the stack trace untouched (an exception FILTER, not a
                        // catch-and-reraise, so the original throw site is preserved).
                        let mutable handlerEntered = false

                        let trackedNext =
                            RequestDelegate(fun c ->
                                handlerEntered <- true
                                next.Invoke c)

                        try
                            do! runValidation ctx trackedNext shapesGraph
                        with ex when not handlerEntered ->
                            let logger =
                                ctx.RequestServices.GetService(typeof<ILoggerFactory>) :?> ILoggerFactory

                            if not (isNull (box logger)) then
                                logger
                                    .CreateLogger("Frank.Validation")
                                    .LogError(ex, "SHACL validation failed unexpectedly for {Path}", ctx.Request.Path)

                            if not ctx.Response.HasStarted then
                                ctx.Response.Clear()

                                do!
                                    writeProblemJson
                                        ctx
                                        500
                                        "Validation error"
                                        "The request body could not be validated because the validation engine failed unexpectedly."
            }
            :> Task)

    type WebHostBuilder with
        [<CustomOperation("useValidation")>]
        member _.UseValidation(spec: WebHostSpec) : WebHostSpec =
            { spec with
                Middleware = spec.Middleware >> useValidationMiddleware }
