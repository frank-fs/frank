namespace Frank.Cli.Core.Output

open System.IO
open System.Text
open System.Text.Json
open Frank.Cli.Core.Commands
open Frank.Cli.Core.Help
open Frank.Cli.Core.State

module JsonOutput =

    let private serializerOptions =
        JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true)

    let private writeString (w: Utf8JsonWriter) (name: string) (value: string) = w.WriteString(name, value)

    let private writeNumber (w: Utf8JsonWriter) (name: string) (value: int) = w.WriteNumber(name, value)

    let formatExtractResult (result: ExtractCommand.ExtractResult) : string =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))

        writer.WriteStartObject()
        writeString writer "status" "ok"

        writer.WriteStartObject("ontologySummary")
        writeNumber writer "classCount" result.OntologySummary.ClassCount
        writeNumber writer "propertyCount" result.OntologySummary.PropertyCount
        writeNumber writer "alignedCount" result.OntologySummary.AlignedCount
        writeNumber writer "unalignedCount" result.OntologySummary.UnalignedCount
        writer.WriteEndObject()

        writer.WriteStartObject("shapesSummary")
        writeNumber writer "shapeCount" result.ShapesSummary.ShapeCount
        writeNumber writer "constraintCount" result.ShapesSummary.ConstraintCount
        writer.WriteEndObject()

        writer.WriteStartArray("unmappedTypes")

        for ut in result.UnmappedTypes do
            writer.WriteStartObject()
            writeString writer "typeName" ut.TypeName
            writeString writer "reason" ut.Reason
            writeString writer "file" ut.Location.File
            writeNumber writer "line" ut.Location.Line
            writer.WriteEndObject()

        writer.WriteEndArray()

        writeString writer "stateFilePath" result.StateFilePath

        writer.WriteEndObject()
        writer.Flush()

        Encoding.UTF8.GetString(stream.ToArray())

    let formatClarifyResult (result: ClarifyCommand.ClarifyResult) : string =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))

        writer.WriteStartObject()
        writeString writer "status" "ok"

        writer.WriteStartArray("questions")

        for q in result.Questions do
            writer.WriteStartObject()
            writeString writer "id" q.Id
            writeString writer "category" q.Category
            writeString writer "questionText" q.QuestionText

            writer.WriteStartObject("context")
            writeString writer "sourceType" q.Context.SourceType

            match q.Context.Location with
            | Some loc -> writeString writer "location" loc
            | None -> writer.WriteNull("location")

            writer.WriteEndObject()

            writer.WriteStartArray("options")

            for opt in q.Options do
                writer.WriteStartObject()
                writeString writer "label" opt.Label
                writeString writer "impact" opt.Impact
                writer.WriteEndObject()

            writer.WriteEndArray()

            writer.WriteEndObject()

        writer.WriteEndArray()

        writeNumber writer "resolvedCount" result.ResolvedCount
        writeNumber writer "totalCount" result.TotalCount

        writer.WriteEndObject()
        writer.Flush()

        Encoding.UTF8.GetString(stream.ToArray())

    let formatValidateResult (result: ValidateCommand.ValidateResult) : string =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))

        writer.WriteStartObject()
        writeString writer "status" (if result.IsValid then "ok" else "invalid")
        writer.WriteBoolean("isValid", result.IsValid)
        writer.WriteNumber("coveragePercent", result.CoveragePercent)

        writer.WriteStartArray("issues")

        for issue in result.Issues do
            writer.WriteStartObject()
            writeString writer "severity" (ValidateCommand.Severity.toString issue.Severity)
            writeString writer "message" issue.Message

            match issue.Uri with
            | Some uri -> writeString writer "uri" uri.AbsoluteUri
            | None -> writer.WriteNull("uri")

            writer.WriteEndObject()

        writer.WriteEndArray()

        writer.WriteEndObject()
        writer.Flush()

        Encoding.UTF8.GetString(stream.ToArray())

    let formatDiffResult (result: DiffCommand.DiffCommandResult) : string =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))

        writer.WriteStartObject()
        writeString writer "status" "ok"

        writer.WriteStartArray("added")

        for entry in result.Diff.Added do
            writer.WriteStartObject()
            writeString writer "uri" entry.Uri.AbsoluteUri

            match entry.Field with
            | Some f -> writeString writer "field" f
            | None -> writer.WriteNull("field")

            match entry.To with
            | Some v -> writeString writer "value" v
            | None -> writer.WriteNull("value")

            writer.WriteEndObject()

        writer.WriteEndArray()

        writer.WriteStartArray("removed")

        for entry in result.Diff.Removed do
            writer.WriteStartObject()
            writeString writer "uri" entry.Uri.AbsoluteUri

            match entry.Field with
            | Some f -> writeString writer "field" f
            | None -> writer.WriteNull("field")

            match entry.From with
            | Some v -> writeString writer "value" v
            | None -> writer.WriteNull("value")

            writer.WriteEndObject()

        writer.WriteEndArray()

        writer.WriteStartArray("modified")

        for entry in result.Diff.Modified do
            writer.WriteStartObject()
            writeString writer "uri" entry.Uri.AbsoluteUri

            match entry.Field with
            | Some f -> writeString writer "field" f
            | None -> writer.WriteNull("field")

            match entry.From with
            | Some v -> writeString writer "from" v
            | None -> writer.WriteNull("from")

            match entry.To with
            | Some v -> writeString writer "to" v
            | None -> writer.WriteNull("to")

            writer.WriteEndObject()

        writer.WriteEndArray()

        writeNumber writer "addedCount" result.Diff.Added.Length
        writeNumber writer "removedCount" result.Diff.Removed.Length
        writeNumber writer "modifiedCount" result.Diff.Modified.Length

        writer.WriteEndObject()
        writer.Flush()

        Encoding.UTF8.GetString(stream.ToArray())

    let formatCompileResult (result: CompileCommand.CompileResult) : string =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))

        writer.WriteStartObject()
        writeString writer "status" "ok"
        writeString writer "ontologyPath" result.OntologyPath
        writeString writer "shapesPath" result.ShapesPath
        writeString writer "manifestPath" result.ManifestPath

        writer.WriteStartArray("embeddedResourceNames")

        for name in result.EmbeddedResourceNames do
            writer.WriteStringValue name

        writer.WriteEndArray()

        writer.WriteEndObject()
        writer.Flush()

        Encoding.UTF8.GetString(stream.ToArray())

    let formatStatusResult (result: ProjectStatus) : string =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))

        writer.WriteStartObject()
        writeString writer "status" "ok"
        writeString writer "projectPath" result.ProjectPath
        writeString writer "stateDirectory" result.StateDirectory

        writer.WriteStartObject("extraction")

        let extractionState =
            match result.Extraction with
            | ExtractionStatus.NotExtracted -> "not_extracted"
            | ExtractionStatus.Current -> "current"
            | ExtractionStatus.Stale -> "stale"
            | ExtractionStatus.Unreadable _ -> "unreadable"

        writeString writer "state" extractionState
        writer.WriteEndObject()

        writer.WriteStartObject("artifacts")

        let artifactState =
            match result.Artifacts with
            | ArtifactStatus.Present -> "present"
            | ArtifactStatus.Missing _ -> "missing"

        writeString writer "state" artifactState

        match result.Artifacts with
        | ArtifactStatus.Present ->
            writer.WriteStartArray("files")
            writer.WriteEndArray()
        | ArtifactStatus.Missing missing ->
            writer.WriteStartArray("missingFiles")

            for f in missing do
                writer.WriteStringValue(f)

            writer.WriteEndArray()

        writer.WriteEndObject()

        writer.WriteStartObject("recommendedAction")

        let (action, message) =
            match result.RecommendedAction with
            | RecommendedAction.RunExtract -> ("run_extract", "Run extract to begin")
            | RecommendedAction.ReExtract -> ("re_extract", "Re-run extract (source files changed)")
            | RecommendedAction.RunCompile -> ("run_compile", "Run compile to generate artifacts")
            | RecommendedAction.UpToDate -> ("up_to_date", "No action needed")
            | RecommendedAction.RecoverExtract reason -> ("recover_extract", $"Re-extract to recover: {reason}")

        writeString writer "action" action
        writeString writer "message" message
        writer.WriteEndObject()

        writer.WriteEndObject()
        writer.Flush()
        Encoding.UTF8.GetString(stream.ToArray())

    let formatHelpIndex (index: HelpSubcommand.HelpIndex) : string =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
        writer.WriteStartObject()
        writer.WriteStartArray("commands")

        for (name, summary) in index.Commands do
            writer.WriteStartObject()
            writeString writer "name" name
            writeString writer "summary" summary
            writer.WriteEndObject()

        writer.WriteEndArray()
        writer.WriteStartArray("topics")

        for (name, summary) in index.Topics do
            writer.WriteStartObject()
            writeString writer "name" name
            writeString writer "summary" summary
            writer.WriteEndObject()

        writer.WriteEndArray()
        writer.WriteEndObject()
        writer.Flush()
        Encoding.UTF8.GetString(stream.ToArray())

    let formatTopicJson (topic: HelpTopic) : string =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
        writer.WriteStartObject()
        writeString writer "name" topic.Name
        writeString writer "summary" topic.Summary
        writeString writer "content" topic.Content
        writer.WriteEndObject()
        writer.Flush()
        Encoding.UTF8.GetString(stream.ToArray())

    let formatNoMatch (query: string) (suggestions: string list) : string =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
        writer.WriteStartObject()
        writeString writer "status" "not_found"
        writeString writer "query" query
        writer.WriteStartArray("suggestions")

        for name in suggestions do
            writer.WriteStartObject()
            writeString writer "name" name
            // Look up summary and type
            match HelpContent.findCommand name with
            | Some cmd ->
                writeString writer "summary" cmd.Summary
                writeString writer "type" "command"
            | None ->
                match HelpContent.findTopic name with
                | Some topic ->
                    writeString writer "summary" topic.Summary
                    writeString writer "type" "topic"
                | None -> ()

            writer.WriteEndObject()

        writer.WriteEndArray()
        writer.WriteEndObject()
        writer.Flush()
        Encoding.UTF8.GetString(stream.ToArray())

    let formatOpenApiValidateResult (result: OpenApiValidateCommand.OpenApiValidateResult) : string =
        Frank.Cli.Core.Statechart.ValidationReportFormatter.formatJson result.Report

    let formatStatechartGenerateResult (result: StatechartGenerateCommand.GenerateResult) : string =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
        writer.WriteStartObject()
        writeString writer "status" "ok"
        writer.WriteStartArray("artifacts")

        for a in result.Artifacts do
            writer.WriteStartObject()
            writeString writer "resourceSlug" a.ResourceSlug
            writeString writer "routeTemplate" a.RouteTemplate
            writeString writer "format" (Frank.Cli.Core.Statechart.FormatDetector.FormatTag.toLower a.Format)

            match a.FilePath with
            | Some p -> writeString writer "filePath" p
            | None -> writer.WriteNull("filePath")

            writer.WriteEndObject()

        writer.WriteEndArray()

        if not result.GenerationErrors.IsEmpty then
            writer.WriteStartArray("generationErrors")

            for err in result.GenerationErrors do
                writer.WriteStringValue(Frank.Cli.Core.Statechart.StatechartError.formatError err)

            writer.WriteEndArray()

        writer.WriteEndObject()
        writer.Flush()
        Encoding.UTF8.GetString(stream.ToArray())

    let formatStatechartValidateResult (result: StatechartValidateCommand.ValidateResult) : string =
        Frank.Cli.Core.Statechart.ValidationReportFormatter.formatJson result.Report

    let formatStatechartParseResult (result: StatechartParseCommand.ParseCommandResult) : string =
        Frank.Cli.Core.Statechart.StatechartDocumentJson.serializeParseResult result.ParseResult

    let formatError (message: string) : string =
        let error =
            {| status = "error"
               message = message |}

        JsonSerializer.Serialize(error, serializerOptions)

    let formatStatechartError (error: Frank.Cli.Core.Statechart.StatechartError.StatechartError) : string =
        Frank.Cli.Core.Statechart.StatechartError.formatErrorJson error

    let formatStatechartExtractResult (result: StatechartExtractCommand.ExtractResult) : string =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
        writer.WriteStartObject()
        writeString writer "status" "ok"
        writer.WriteStartArray("stateMachines")

        for sm in result.StateMachines do
            writer.WriteStartObject()
            writeString writer "routeTemplate" sm.RouteTemplate
            writeString writer "initialState" sm.InitialStateKey
            writer.WriteStartArray("states")

            for s in sm.StateNames do
                writer.WriteStringValue(s)

            writer.WriteEndArray()
            writer.WriteStartArray("guards")

            for g in sm.GuardNames do
                writer.WriteStringValue(g)

            writer.WriteEndArray()
            writer.WriteStartObject("stateMetadata")

            for KeyValue(name, info) in sm.StateMetadata do
                writer.WriteStartObject(name)
                writer.WriteStartArray("allowedMethods")

                for m in info.AllowedMethods do
                    writer.WriteStringValue(m)

                writer.WriteEndArray()
                writer.WriteBoolean("isFinal", info.IsFinal)

                match info.Description with
                | Some d -> writeString writer "description" d
                | None -> writer.WriteNull("description")

                writer.WriteEndObject()

            writer.WriteEndObject()
            writer.WriteEndObject()

        writer.WriteEndArray()
        writer.WriteEndObject()
        writer.Flush()
        Encoding.UTF8.GetString(stream.ToArray())

    let formatUnifiedValidateResult (result: UnifiedValidateCommand.UnifiedValidateResult) : string =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
        writer.WriteStartObject()
        writeString writer "status" (if result.TotalErrors > 0 then "fail" else "ok")
        writer.WriteBoolean("fromCache", result.FromCache)
        writeNumber writer "resourcesChecked" result.ResourcesChecked
        writeNumber writer "totalIssues" result.TotalIssues
        writeNumber writer "totalErrors" result.TotalErrors
        writeNumber writer "totalWarnings" result.TotalWarnings
        writer.WriteStartArray("results")

        for r in result.ProjectionResults do
            writer.WriteStartObject()
            writeString writer "resourceRoute" r.ResourceRoute
            writeNumber writer "checksRun" r.ChecksRun
            writer.WriteStartArray("issues")

            for issue in r.Issues do
                writer.WriteStartObject()

                writeString
                    writer
                    "check"
                    (Frank.Statecharts.Analysis.ProjectionValidator.ProjectionCheckKind.toString issue.Check)

                let severity =
                    match issue.Severity with
                    | Frank.Statecharts.Analysis.ProjectionValidator.Severity.Error -> "error"
                    | Frank.Statecharts.Analysis.ProjectionValidator.Severity.Warning -> "warning"

                writeString writer "severity" severity
                writeString writer "message" issue.Message
                writer.WriteEndObject()

            writer.WriteEndArray()
            writer.WriteEndObject()

        writer.WriteEndArray()
        writer.WriteEndObject()
        writer.Flush()
        Encoding.UTF8.GetString(stream.ToArray())

    let formatUnifiedExtractResult (result: UnifiedExtractCommand.UnifiedExtractResult) : string =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
        writer.WriteStartObject()
        writeString writer "status" "ok"
        writer.WriteBoolean("fromCache", result.FromCache)
        writeNumber writer "resourceCount" result.ResourceCount
        writeNumber writer "statefulResourceCount" result.StatefulResourceCount
        writeNumber writer "plainResourceCount" result.PlainResourceCount
        writeNumber writer "typeCount" result.TypeCount
        writer.WriteStartArray("resources")

        for r in result.State.Resources do
            writer.WriteStartObject()
            writeString writer "routeTemplate" r.RouteTemplate
            writeString writer "resourceSlug" r.ResourceSlug
            writeNumber writer "typeCount" r.TypeInfo.Length

            match r.Statechart with
            | None -> writer.WriteNull("statechart")
            | Some sc ->
                writer.WriteStartObject("statechart")
                writeString writer "initialState" sc.InitialStateKey
                writer.WriteStartArray("states")

                for s in sc.StateNames do
                    writer.WriteStringValue(s)

                writer.WriteEndArray()
                writer.WriteStartArray("guards")

                for g in sc.GuardNames do
                    writer.WriteStringValue(g)

                writer.WriteEndArray()
                writer.WriteStartObject("stateMetadata")

                for kvp in sc.StateMetadata do
                    writer.WriteStartObject(kvp.Key)
                    writer.WriteStartArray("allowedMethods")

                    for m in kvp.Value.AllowedMethods do
                        writer.WriteStringValue(m)

                    writer.WriteEndArray()
                    writer.WriteBoolean("isFinal", kvp.Value.IsFinal)
                    writer.WriteEndObject()

                writer.WriteEndObject()
                writer.WriteEndObject()

            writer.WriteStartArray("httpCapabilities")

            for cap in r.HttpCapabilities do
                writer.WriteStartObject()
                writeString writer "method" cap.Method

                match cap.StateKey with
                | Some sk -> writeString writer "stateKey" sk
                | None -> writer.WriteNull("stateKey")

                writeString writer "linkRelation" cap.LinkRelation
                writer.WriteBoolean("isSafe", cap.IsSafe)
                writer.WriteEndObject()

            writer.WriteEndArray()

            writer.WriteStartObject("derivedFields")
            writer.WriteStartArray("orphanStates")

            for s in r.DerivedFields.OrphanStates do
                writer.WriteStringValue(s)

            writer.WriteEndArray()
            writer.WriteStartArray("unhandledCases")

            for c in r.DerivedFields.UnhandledCases do
                writer.WriteStringValue(c)

            writer.WriteEndArray()
            writer.WriteNumber("typeCoverage", r.DerivedFields.TypeCoverage)
            writer.WriteEndObject()
            writer.WriteEndObject()

        writer.WriteEndArray()
        writer.WriteStartArray("warnings")

        for w in result.Warnings do
            writer.WriteStringValue(w)

        writer.WriteEndArray()
        writeString writer "cacheFilePath" result.CacheFilePath
        writer.WriteEndObject()
        writer.Flush()
        Encoding.UTF8.GetString(stream.ToArray())
