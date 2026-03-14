namespace Frank.Validation

open System
open System.IO
open System.Text.Json
open Microsoft.AspNetCore.Http
open VDS.RDF
open Frank.LinkedData.Negotiation

/// Content-negotiated serialization of SHACL ValidationReports.
/// Semantic clients (Accept: application/ld+json, text/turtle, application/rdf+xml)
/// receive a SHACL ValidationReport graph; standard clients receive RFC 9457 Problem Details JSON.
module ReportSerializer =

    let private sh = "http://www.w3.org/ns/shacl#"

    /// SHACL severity URI strings.
    let private severityUri (severity: ValidationSeverity) =
        match severity with
        | Violation -> sh + "Violation"
        | Warning -> sh + "Warning"
        | Info -> sh + "Info"

    /// Convert a SHACL result path URI (urn:frank:property:FieldName) to dot-notation
    /// for Problem Details ($.FieldName). Handles nested paths by splitting on the
    /// property URI prefix.
    let private toDotPath (resultPath: string) =
        if String.IsNullOrEmpty(resultPath) then
            "$"
        else
            let prefix = "urn:frank:property:"

            if resultPath.StartsWith(prefix, StringComparison.Ordinal) then
                "$." + resultPath.Substring(prefix.Length)
            else
                "$." + resultPath

    /// Create a URI node in the given graph.
    let private uriNode (g: IGraph) (uri: string) = g.CreateUriNode(UriFactory.Create(uri))

    /// Build an IGraph containing a SHACL ValidationReport from our F# ValidationReport.
    let buildReportGraph (report: ValidationReport) : IGraph =
        let g = new Graph()
        let rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#"
        g.NamespaceMap.AddNamespace("sh", UriFactory.Create(sh))
        g.NamespaceMap.AddNamespace("rdf", UriFactory.Create(rdf))
        g.NamespaceMap.AddNamespace("xsd", UriFactory.Create("http://www.w3.org/2001/XMLSchema#"))

        // Create the report node (blank node)
        let reportNode = g.CreateBlankNode()
        let rdfType = uriNode g (rdf + "type")
        let reportType = uriNode g (sh + "ValidationReport")
        g.Assert(reportNode, rdfType, reportType :> INode)

        // sh:conforms
        let shConforms = uriNode g (sh + "conforms")

        let conformsLit =
            g.CreateLiteralNode(
                (if report.Conforms then "true" else "false"),
                UriFactory.Create("http://www.w3.org/2001/XMLSchema#boolean")
            )

        g.Assert(reportNode, shConforms, conformsLit)

        // sh:result entries
        let shResult = uriNode g (sh + "result")
        let shFocusNode = uriNode g (sh + "focusNode")
        let shResultPath = uriNode g (sh + "resultPath")
        let shValue = uriNode g (sh + "value")
        let shSourceConstraintComponent = uriNode g (sh + "sourceConstraintComponent")
        let shResultMessage = uriNode g (sh + "resultMessage")
        let shResultSeverity = uriNode g (sh + "resultSeverity")
        let validationResultType = uriNode g (sh + "ValidationResult")

        for r in report.Results do
            let resultNode = g.CreateBlankNode()
            g.Assert(reportNode, shResult, resultNode :> INode)

            // rdf:type sh:ValidationResult
            g.Assert(resultNode, rdfType, validationResultType :> INode)

            // sh:focusNode
            if not (String.IsNullOrEmpty(r.FocusNode)) then
                let focusUri = uriNode g r.FocusNode
                g.Assert(resultNode, shFocusNode, focusUri :> INode)

            // sh:resultPath
            if not (String.IsNullOrEmpty(r.ResultPath)) then
                let pathUri = uriNode g r.ResultPath
                g.Assert(resultNode, shResultPath, pathUri :> INode)

            // sh:value
            match r.Value with
            | Some v ->
                let valueLit = g.CreateLiteralNode(v.ToString())
                g.Assert(resultNode, shValue, valueLit :> INode)
            | None -> ()

            // sh:sourceConstraintComponent
            if not (String.IsNullOrEmpty(r.SourceConstraint)) then
                let constraintUri = uriNode g r.SourceConstraint
                g.Assert(resultNode, shSourceConstraintComponent, constraintUri :> INode)

            // sh:resultMessage
            if not (String.IsNullOrEmpty(r.Message)) then
                let msgLit = g.CreateLiteralNode(r.Message)
                g.Assert(resultNode, shResultMessage, msgLit :> INode)

            // sh:resultSeverity
            let sevUri = uriNode g (severityUri r.Severity)
            g.Assert(resultNode, shResultSeverity, sevUri :> INode)

        g :> IGraph

    /// Write a SHACL ValidationReport as RDF to the HttpResponse, serialized
    /// in the format indicated by contentType. Uses a MemoryStream buffer to
    /// avoid synchronous writes on the response stream (required by TestHost
    /// and Kestrel when AllowSynchronousIO is false).
    let writeShaclReport (ctx: HttpContext) (report: ValidationReport) (contentType: string) =
        task {
            use graph = buildReportGraph report
            ctx.Response.ContentType <- contentType

            use buffer = new MemoryStream()

            match contentType with
            | "application/ld+json" -> JsonLdFormatter.writeJsonLd graph buffer
            | "text/turtle" -> TurtleFormatter.writeTurtle graph buffer
            | "application/rdf+xml" -> RdfXmlFormatter.writeRdfXml graph buffer
            | _ -> JsonLdFormatter.writeJsonLd graph buffer

            buffer.Position <- 0L
            do! buffer.CopyToAsync(ctx.Response.Body)
        }

    /// Shared serializer options for Problem Details JSON.
    let private problemJsonOptions =
        JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

    /// Write an RFC 9457 Problem Details JSON response for validation failures.
    let writeProblemDetails (ctx: HttpContext) (report: ValidationReport) =
        task {
            ctx.Response.ContentType <- "application/problem+json"

            let errors =
                report.Results
                |> List.map (fun r ->
                    {| path = toDotPath r.ResultPath
                       ``constraint`` = r.SourceConstraint
                       message = r.Message
                       value =
                        match r.Value with
                        | Some v -> v.ToString()
                        | None -> null |})

            let problemDetails =
                {| ``type`` = "urn:frank:validation:shacl-violation"
                   title = "Validation Failed"
                   status = 422
                   detail =
                    sprintf
                        "The request data violates %d SHACL constraint(s) defined by shape <%s>."
                        report.Results.Length
                        (report.ShapeUri.ToString())
                   errors = errors |}

            do! JsonSerializer.SerializeAsync(ctx.Response.Body, problemDetails, problemJsonOptions)
        }

    /// Semantic media types that trigger SHACL graph serialization.
    let private semanticTypes =
        set [ "application/ld+json"; "text/turtle"; "application/rdf+xml" ]

    /// Parse the Accept header and return the best matching semantic media type, if any.
    let private findSemanticAccept (ctx: HttpContext) : string option =
        let accept = ctx.Request.Headers.Accept.ToString()

        if String.IsNullOrWhiteSpace(accept) || accept = "*/*" then
            None
        else
            // Split on comma, trim, and find the first semantic type
            accept.Split(',')
            |> Array.map (fun s ->
                // Strip quality parameters (e.g., ";q=0.9")
                let idx = s.IndexOf(';')
                if idx >= 0 then s.Substring(0, idx).Trim() else s.Trim())
            |> Array.tryFind (fun mt -> semanticTypes.Contains(mt))

    /// Content-negotiated write: inspect Accept header, dispatch to semantic or Problem Details.
    let writeNegotiated (ctx: HttpContext) (report: ValidationReport) =
        task {
            match findSemanticAccept ctx with
            | Some mediaType -> do! writeShaclReport ctx report mediaType
            | None -> do! writeProblemDetails ctx report
        }
