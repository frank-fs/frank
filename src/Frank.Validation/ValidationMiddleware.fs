namespace Frank.Validation

open System.IO
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging

/// ASP.NET Core middleware that intercepts requests to validated endpoints,
/// runs SHACL validation against derived shapes, and short-circuits with
/// 422 Unprocessable Entity on validation failure.
///
/// Pipeline ordering: after useAuth, before handler dispatch.
/// Endpoints without a ValidationMarker in metadata are passed through
/// with zero overhead (null metadata check only).
type ValidationMiddleware(next: RequestDelegate, shapeCache: ShapeCache, logger: ILogger<ValidationMiddleware>) =

    /// HTTP methods that carry a request body for validation.
    static let bodyMethods = set [ "POST"; "PUT"; "PATCH" ]

    /// HTTP methods where query parameter validation applies.
    static let queryMethods = set [ "GET" ]

    /// Shared serializer options for writing validation reports.
    static let jsonOptions =
        JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

    /// Serialize a ValidationReport as a JSON response body (placeholder until WP04).
    static let writeReport (ctx: HttpContext) (report: ValidationReport) =
        task {
            ctx.Response.ContentType <- "application/json"

            let results =
                report.Results
                |> List.map (fun r ->
                    {| focusNode = r.FocusNode
                       resultPath = r.ResultPath
                       message = r.Message
                       sourceConstraint = r.SourceConstraint
                       severity =
                        match r.Severity with
                        | Violation -> "Violation"
                        | Warning -> "Warning"
                        | Info -> "Info" |})

            let body =
                {| conforms = report.Conforms
                   shapeUri = report.ShapeUri.ToString()
                   results = results |}

            do! JsonSerializer.SerializeAsync(ctx.Response.Body, body, jsonOptions)
        }

    /// Read the request body as a JsonElement. Returns None if body is empty or unreadable.
    let readJsonBody (ctx: HttpContext) =
        task {
            ctx.Request.EnableBuffering()

            if ctx.Request.ContentLength.HasValue && ctx.Request.ContentLength.Value = 0L then
                return None
            else
                try
                    use reader = new StreamReader(ctx.Request.Body, leaveOpen = true)
                    let! bodyText = reader.ReadToEndAsync()
                    ctx.Request.Body.Position <- 0L

                    if System.String.IsNullOrWhiteSpace(bodyText) then
                        return None
                    else
                        use doc = JsonDocument.Parse(bodyText)
                        let cloned = doc.RootElement.Clone()
                        return Some cloned
                with ex ->
                    logger.LogWarning(ex, "Failed to read JSON request body for {Path}", ctx.Request.Path)
                    ctx.Request.Body.Position <- 0L
                    return None
        }

    member _.InvokeAsync(ctx: HttpContext) : Task =
        task {
            let endpoint = ctx.GetEndpoint()

            match endpoint with
            | null -> do! next.Invoke(ctx)
            | ep ->
                let marker = ep.Metadata.GetMetadata<ValidationMarker>()

                match box marker with
                | null ->
                    // No validation configured for this endpoint: pass through with zero overhead.
                    do! next.Invoke(ctx)
                | _ ->
                    let method = ctx.Request.Method.ToUpperInvariant()
                    let struct (shapesGraph, shape) = shapeCache.GetOrAdd(marker.ShapeType)

                    if bodyMethods.Contains(method) then
                        // POST/PUT/PATCH: validate request body
                        let! jsonOpt = readJsonBody ctx

                        match jsonOpt with
                        | None ->
                            // Missing body on a validated endpoint: validation failure (not deserialization error)
                            logger.LogDebug(
                                "Validation failure: missing request body for {Method} {Path}",
                                method,
                                ctx.Request.Path
                            )

                            ctx.Response.StatusCode <- 422

                            let report =
                                { Conforms = false
                                  Results =
                                    [ { FocusNode = ""
                                        ResultPath = ""
                                        Value = None
                                        SourceConstraint = "http://www.w3.org/ns/shacl#minCount"
                                        Message = "Request body is required but was empty or missing."
                                        Severity = Violation } ]
                                  ShapeUri = shape.NodeShapeUri }

                            do! writeReport ctx report
                        | Some json ->
                            use dataGraph = DataGraphBuilder.buildFromJsonBody shape json

                            let report = Validator.validate shapesGraph shape.NodeShapeUri dataGraph

                            if report.Conforms then
                                do! next.Invoke(ctx)
                            else
                                logger.LogDebug(
                                    "Validation failure on {Method} {Path}: {Count} violation(s)",
                                    method,
                                    ctx.Request.Path,
                                    report.Results.Length
                                )

                                ctx.Response.StatusCode <- 422
                                do! writeReport ctx report
                    elif queryMethods.Contains(method) then
                        // GET: validate query parameters
                        use dataGraph = DataGraphBuilder.buildFromQueryParams shape ctx.Request.Query

                        let report = Validator.validate shapesGraph shape.NodeShapeUri dataGraph

                        if report.Conforms then
                            do! next.Invoke(ctx)
                        else
                            logger.LogDebug(
                                "Validation failure on GET {Path}: {Count} violation(s)",
                                ctx.Request.Path,
                                report.Results.Length
                            )

                            ctx.Response.StatusCode <- 422
                            do! writeReport ctx report
                    else
                        // DELETE/HEAD/OPTIONS: skip validation
                        do! next.Invoke(ctx)
        }
