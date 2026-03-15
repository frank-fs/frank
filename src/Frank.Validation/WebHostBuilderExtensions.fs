namespace Frank.Validation

open System
open System.Reflection
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Frank.Builder

/// Configuration options for the SHACL validation middleware.
type ValidationOptions =
    {
        /// When true, handler responses are validated against the registered shape and
        /// violations are logged at Warning level.  Responses are never blocked — this is
        /// a diagnostic aid only.  Default: false.
        EnableResponseValidation: bool
    }

    static member Default = { EnableResponseValidation = false }

[<AutoOpen>]
module WebHostBuilderExtensions =

    /// Apply any CustomConstraints from the ValidationMarker to the pre-loaded shape,
    /// then reload the merged result into the cache.
    let private applyCustomConstraints (cache: ShapeCache) (marker: ValidationMarker) =
        if marker.CustomConstraints.IsEmpty then
            ()
        else
            match cache.TryGet(marker.ShapeUri) with
            | ValueNone ->
                // Shape was not pre-loaded; custom constraints cannot be applied.
                // ValidationMiddleware will surface this as a runtime error.
                ()
            | ValueSome(struct (_, baseShape)) ->
                let merged = ShapeMerger.mergeConstraints baseShape marker.CustomConstraints
                cache.LoadAll [ merged ]

    /// Walk all endpoint metadata to find ValidationMarkers and apply custom constraints.
    let private initCustomConstraints (cache: ShapeCache) (app: IApplicationBuilder) =
        let endpointSources =
            app.ApplicationServices.GetService<
                System.Collections.Generic.IEnumerable<Microsoft.AspNetCore.Routing.EndpointDataSource>
             >()

        if not (isNull endpointSources) then
            for source in endpointSources do
                for endpoint in source.Endpoints do
                    for item in endpoint.Metadata do
                        match item with
                        | :? ValidationMarker as marker -> applyCustomConstraints cache marker
                        | _ -> ()

    /// Response validation diagnostic wrapper.
    /// Intercepts responses for validated endpoints, validates the body against the
    /// registered shape, and logs any violations.  Never blocks the response.
    let private responseValidationMiddleware
        (logger: ILogger)
        (cache: ShapeCache)
        (ctx: HttpContext)
        (next: RequestDelegate)
        =
        let endpoint = ctx.GetEndpoint()

        let markerOpt =
            if isNull endpoint then
                None
            else
                endpoint.Metadata
                |> Seq.tryPick (fun item ->
                    match item with
                    | :? ValidationMarker as m -> Some m
                    | _ -> None)

        match markerOpt with
        | None -> next.Invoke(ctx)
        | Some marker ->
            // Wrap the response stream to capture the body for post-response validation.
            let originalBody = ctx.Response.Body

            task {
                use buffer = new System.IO.MemoryStream()
                ctx.Response.Body <- buffer

                try
                    do! next.Invoke(ctx)
                finally
                    // Always restore the original stream.
                    ctx.Response.Body <- originalBody

                // Copy buffered response to the real stream.
                let responseBytes = buffer.ToArray()

                if responseBytes.Length > 0 then
                    do! originalBody.WriteAsync(responseBytes, 0, responseBytes.Length)

                    // Attempt to parse and validate the response body (diagnostic only).
                    let contentType = ctx.Response.ContentType

                    if
                        not (String.IsNullOrEmpty contentType)
                        && contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase)
                    then
                        try
                            use doc = System.Text.Json.JsonDocument.Parse(responseBytes)
                            let json = doc.RootElement.Clone()

                            match cache.TryGet(marker.ShapeUri) with
                            | ValueNone ->
                                logger.LogDebug(
                                    "Response validation skipped for {Path}: shape {Uri} not in cache",
                                    ctx.Request.Path,
                                    marker.ShapeUri
                                )
                            | ValueSome(struct (shapesGraph, shape)) ->
                                use dataGraph = DataGraphBuilder.buildFromJsonBody shape json

                                let report = Validator.validate shapesGraph shape.NodeShapeUri dataGraph

                                if not report.Conforms then
                                    for result in report.Results do
                                        logger.LogWarning(
                                            "Response validation violation on {Method} {Path} [{Constraint}]: {Message}",
                                            ctx.Request.Method,
                                            ctx.Request.Path,
                                            result.SourceConstraint,
                                            result.Message
                                        )
                        with ex ->
                            logger.LogDebug(
                                ex,
                                "Response validation parse error for {Path} (diagnostic only)",
                                ctx.Request.Path
                            )
            }
            :> System.Threading.Tasks.Task

    type WebHostBuilder with

        /// Register the Frank.Validation middleware and shape cache.
        ///
        /// At startup:
        ///   1. Creates a singleton ShapeCache.
        ///   2. Loads all SHACL shapes from the embedded "Frank.Semantic.shapes.shacl.ttl"
        ///      resource in the entry assembly (eager — surfaces missing-resource errors early).
        ///   3. Registers ValidationMiddleware in the ASP.NET Core pipeline.
        ///   4. After app build, applies any per-endpoint CustomConstraints via ShapeMerger.
        ///
        /// Applications that do not call this operation experience zero behavioral changes.
        [<CustomOperation("useValidation")>]
        member _.UseValidation(spec: WebHostSpec) : WebHostSpec =
            let options = ValidationOptions.Default

            spec
            |> fun s ->
                { s with
                    Services =
                        s.Services
                        >> fun services ->
                            services.AddSingleton<ShapeCache>() |> ignore
                            services.AddSingleton<ValidationOptions>(options) |> ignore
                            services

                    Middleware =
                        s.Middleware
                        >> fun app ->
                            let cache = app.ApplicationServices.GetRequiredService<ShapeCache>()

                            // Eager shape loading from embedded resource.
                            let assembly =
                                match Assembly.GetEntryAssembly() with
                                | null ->
                                    raise (
                                        InvalidOperationException(
                                            "Assembly.GetEntryAssembly() returned null. Use useValidationWith to supply the assembly explicitly."
                                        )
                                    )
                                | asm -> asm

                            try
                                let shapes = ShapeLoader.loadFromAssembly assembly
                                cache.LoadAll shapes
                            with :? InvalidOperationException as ex ->
                                // Re-raise with context — missing embedded resource is a
                                // configuration error that must be surfaced at startup.
                                raise (
                                    InvalidOperationException(
                                        sprintf "Frank.Validation startup error: %s" ex.Message,
                                        ex
                                    )
                                )

                            // Apply per-endpoint custom constraints after all shapes are loaded.
                            initCustomConstraints cache app

                            // Register the validation middleware.
                            app.UseMiddleware<ValidationMiddleware>() |> ignore

                            app }

        /// Register the Frank.Validation middleware and shape cache with explicit options.
        ///
        /// <paramref name="configure"/> receives a ValidationOptions and returns a modified
        /// copy.  Use this overload to enable diagnostic response validation or customise
        /// other settings.
        [<CustomOperation("useValidationWith")>]
        member _.UseValidationWith(spec: WebHostSpec, configure: ValidationOptions -> ValidationOptions) : WebHostSpec =
            let options = configure ValidationOptions.Default

            spec
            |> fun s ->
                { s with
                    Services =
                        s.Services
                        >> fun services ->
                            services.AddSingleton<ShapeCache>() |> ignore
                            services.AddSingleton<ValidationOptions>(options) |> ignore
                            services

                    Middleware =
                        s.Middleware
                        >> fun app ->
                            let cache = app.ApplicationServices.GetRequiredService<ShapeCache>()

                            // Eager shape loading from embedded resource.
                            let assembly =
                                match Assembly.GetEntryAssembly() with
                                | null ->
                                    raise (
                                        InvalidOperationException(
                                            "Assembly.GetEntryAssembly() returned null. Use useValidationAssembly to supply the assembly explicitly."
                                        )
                                    )
                                | asm -> asm

                            try
                                let shapes = ShapeLoader.loadFromAssembly assembly
                                cache.LoadAll shapes
                            with :? InvalidOperationException as ex ->
                                raise (
                                    InvalidOperationException(
                                        sprintf "Frank.Validation startup error: %s" ex.Message,
                                        ex
                                    )
                                )

                            initCustomConstraints cache app

                            // Response validation diagnostic middleware wraps the full pipeline
                            // (including ValidationMiddleware) so it sees the final response.
                            // Register it BEFORE UseMiddleware<ValidationMiddleware> so it is
                            // outermost and can capture the response body.
                            if options.EnableResponseValidation then
                                let logger =
                                    app.ApplicationServices
                                        .GetRequiredService<ILoggerFactory>()
                                        .CreateLogger("Frank.Validation.ResponseDiagnostics")

                                app.Use(
                                    System.Func<HttpContext, RequestDelegate, System.Threading.Tasks.Task>(
                                        responseValidationMiddleware logger cache
                                    )
                                )
                                |> ignore

                            app.UseMiddleware<ValidationMiddleware>() |> ignore

                            app }

        /// Register the Frank.Validation middleware with shapes loaded from a specific assembly.
        ///
        /// Use this overload in test hosts or scenarios where Assembly.GetEntryAssembly()
        /// does not return the application assembly.
        [<CustomOperation("useValidationAssembly")>]
        member _.UseValidationAssembly(spec: WebHostSpec, assembly: Assembly) : WebHostSpec =
            spec
            |> fun s ->
                { s with
                    Services =
                        s.Services
                        >> fun services ->
                            services.AddSingleton<ShapeCache>() |> ignore
                            services.AddSingleton<ValidationOptions>(ValidationOptions.Default) |> ignore
                            services

                    Middleware =
                        s.Middleware
                        >> fun app ->
                            let cache = app.ApplicationServices.GetRequiredService<ShapeCache>()

                            try
                                let shapes = ShapeLoader.loadFromAssembly assembly
                                cache.LoadAll shapes
                            with :? InvalidOperationException as ex ->
                                raise (
                                    InvalidOperationException(
                                        sprintf "Frank.Validation startup error: %s" ex.Message,
                                        ex
                                    )
                                )

                            initCustomConstraints cache app
                            app.UseMiddleware<ValidationMiddleware>() |> ignore
                            app }
