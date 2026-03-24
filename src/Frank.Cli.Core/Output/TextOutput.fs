namespace Frank.Cli.Core.Output

open System
open Frank.Cli.Core.Commands
open Frank.Cli.Core.Help

module TextOutput =

    open Frank.Statecharts.Analysis
    open Frank.Cli.Core.Output.AnsiColors

    let formatExtractResult (result: ExtractCommand.ExtractResult) : string =
        let sb = System.Text.StringBuilder()
        sb.AppendLine(bold "Extraction Summary") |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine(sprintf "%-18s| Count" "Category") |> ignore
        sb.AppendLine(sprintf "------------------+------") |> ignore

        sb.AppendLine(sprintf "%-18s| %5d" "Classes" result.OntologySummary.ClassCount)
        |> ignore

        sb.AppendLine(sprintf "%-18s| %5d" "Properties" result.OntologySummary.PropertyCount)
        |> ignore

        sb.AppendLine(sprintf "%-18s| %5d" "Aligned" result.OntologySummary.AlignedCount)
        |> ignore

        sb.AppendLine(sprintf "%-18s| %5d" "Unaligned" result.OntologySummary.UnalignedCount)
        |> ignore

        sb.AppendLine(sprintf "%-18s| %5d" "Shapes" result.ShapesSummary.ShapeCount)
        |> ignore

        sb.AppendLine(sprintf "%-18s| %5d" "Constraints" result.ShapesSummary.ConstraintCount)
        |> ignore

        sb.AppendLine(sprintf "%-18s| %5d" "Unmapped Types" (result.UnmappedTypes |> List.length))
        |> ignore

        sb.AppendLine() |> ignore
        sb.AppendLine(sprintf "State saved to: %s" result.StateFilePath) |> ignore

        if not result.UnmappedTypes.IsEmpty then
            sb.AppendLine() |> ignore

            sb.AppendLine(yellow (sprintf "Unmapped types (%d):" result.UnmappedTypes.Length))
            |> ignore

            for ut in result.UnmappedTypes do
                sb.AppendLine(sprintf "  - %s (%s) at %s:%d" ut.TypeName ut.Reason ut.Location.File ut.Location.Line)
                |> ignore

        sb.ToString()

    let formatClarifyResult (result: ClarifyCommand.ClarifyResult) : string =
        let sb = System.Text.StringBuilder()
        sb.AppendLine(bold "Clarification Questions") |> ignore

        sb.AppendLine(sprintf "Resolved: %d / %d" result.ResolvedCount result.TotalCount)
        |> ignore

        sb.AppendLine(String.replicate 40 "-") |> ignore

        result.Questions
        |> List.iteri (fun i q ->
            sb.AppendLine() |> ignore

            sb.AppendLine(bold (sprintf "%d. [%s] %s" (i + 1) q.Category q.QuestionText))
            |> ignore

            let locStr = q.Context.Location |> Option.defaultValue "unknown"

            sb.AppendLine(sprintf "   Context: %s at %s" q.Context.SourceType locStr)
            |> ignore

            q.Options
            |> List.iteri (fun j opt ->
                let letter = char (int 'a' + j)
                sb.AppendLine(sprintf "   %c) %s — %s" letter opt.Label opt.Impact) |> ignore))

        sb.ToString()

    let formatValidateResult (result: ValidateCommand.ValidateResult) : string =
        let sb = System.Text.StringBuilder()

        let statusText = if result.IsValid then green "VALID" else red "INVALID"

        sb.AppendLine(bold (sprintf "Validation: %s" statusText)) |> ignore
        sb.AppendLine(sprintf "Coverage: %.1f%%" result.CoveragePercent) |> ignore
        sb.AppendLine() |> ignore

        if result.Issues.IsEmpty then
            sb.AppendLine("No issues found.") |> ignore
        else
            sb.AppendLine(sprintf "Issues (%d):" result.Issues.Length) |> ignore

            for issue in result.Issues do
                let prefix =
                    match issue.Severity with
                    | ValidateCommand.Error -> red "ERROR"
                    | ValidateCommand.Warning -> yellow "WARN"
                    | ValidateCommand.Info -> "INFO"

                let uriStr =
                    issue.Uri
                    |> Option.map (fun u -> sprintf " (%s)" u.AbsoluteUri)
                    |> Option.defaultValue ""

                sb.AppendLine(sprintf "  [%s] %s%s" prefix issue.Message uriStr) |> ignore

        sb.ToString()

    let formatDiffResult (result: DiffCommand.DiffCommandResult) : string =
        let sb = System.Text.StringBuilder()
        sb.AppendLine(bold "Diff Summary") |> ignore

        sb.AppendLine(
            sprintf
                "Added: %d  Removed: %d  Modified: %d"
                result.Diff.Added.Length
                result.Diff.Removed.Length
                result.Diff.Modified.Length
        )
        |> ignore

        sb.AppendLine() |> ignore
        sb.AppendLine(result.FormattedDiff) |> ignore
        sb.ToString()

    let formatCompileResult (result: CompileCommand.CompileResult) : string =
        let sb = System.Text.StringBuilder()
        sb.AppendLine(bold "Compile Complete") |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine(sprintf "Ontology: %s" result.OntologyPath) |> ignore
        sb.AppendLine(sprintf "Shapes:   %s" result.ShapesPath) |> ignore
        sb.AppendLine(sprintf "Manifest: %s" result.ManifestPath) |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("Embed these resources in your project:") |> ignore

        for name in result.EmbeddedResourceNames do
            sb.AppendLine(sprintf "  <EmbeddedResource Include=\"...\" LogicalName=\"%s\" />" name)
            |> ignore

        sb.ToString()

    let formatStatusResult (result: ProjectStatus) : string =
        let sb = System.Text.StringBuilder()
        sb.AppendLine($"Project: {result.ProjectPath}") |> ignore
        sb.AppendLine($"State directory: {result.StateDirectory}") |> ignore
        sb.AppendLine() |> ignore

        let extractionText =
            match result.Extraction with
            | ExtractionStatus.NotExtracted -> "not performed"
            | ExtractionStatus.Current -> "current"
            | ExtractionStatus.Stale -> "stale (source files changed since extraction)"
            | ExtractionStatus.Unreadable reason -> $"unreadable ({reason})"

        sb.AppendLine($"Extraction: {extractionText}") |> ignore

        let artifactText =
            match result.Artifacts with
            | ArtifactStatus.Present ->
                match result.Extraction with
                | ExtractionStatus.Stale -> "present (may be outdated)"
                | _ -> "present"
            | ArtifactStatus.Missing _ -> "not present"

        sb.AppendLine($"Artifacts: {artifactText}") |> ignore

        let actionText =
            match result.RecommendedAction with
            | RecommendedAction.RunExtract -> $"run 'frank extract --project {result.ProjectPath} --base-uri <URI>'"
            | RecommendedAction.ReExtract -> $"run 'frank extract --project {result.ProjectPath} --base-uri <URI>'"
            | RecommendedAction.RunCompile -> $"run 'frank compile --project {result.ProjectPath}'"
            | RecommendedAction.UpToDate -> "up to date (no action needed)"
            | RecommendedAction.RecoverExtract _ ->
                $"run 'frank extract --project {result.ProjectPath} --base-uri <URI>' to recover"

        sb.AppendLine($"Recommended action: {actionText}") |> ignore

        sb.ToString()

    let formatHelpIndex (index: HelpSubcommand.HelpIndex) : string =
        let sb = System.Text.StringBuilder()

        sb.AppendLine("frank: Semantic resource extraction for Frank applications")
        |> ignore

        sb.AppendLine() |> ignore
        sb.AppendLine("COMMANDS") |> ignore

        for (name, summary) in index.Commands do
            sb.AppendLine(sprintf "  %-12s%s" name summary) |> ignore

        sb.AppendLine() |> ignore
        sb.AppendLine("TOPICS") |> ignore

        for (name, summary) in index.Topics do
            sb.AppendLine(sprintf "  %-12s%s" name summary) |> ignore

        sb.AppendLine() |> ignore

        sb.AppendLine("Use 'frank help <command>' for detailed help on a command.")
        |> ignore

        sb.AppendLine("Use 'frank help <topic>' for topic documentation.") |> ignore
        sb.ToString()

    let formatTopicText (topic: HelpTopic) : string =
        let sb = System.Text.StringBuilder()
        sb.AppendLine(topic.Name.ToUpperInvariant()) |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine(topic.Content) |> ignore
        sb.ToString()

    let formatNoMatch (query: string) (suggestions: string list) : string =
        let sb = System.Text.StringBuilder()
        sb.AppendLine($"Unknown command or topic: '{query}'") |> ignore

        if not suggestions.IsEmpty then
            sb.AppendLine() |> ignore
            sb.AppendLine("Did you mean?") |> ignore

            for name in suggestions do
                match HelpContent.findCommand name with
                | Some cmd -> sb.AppendLine(sprintf "  %-12s%s" name cmd.Summary) |> ignore
                | None ->
                    match HelpContent.findTopic name with
                    | Some topic -> sb.AppendLine(sprintf "  %-12s%s" name topic.Summary) |> ignore
                    | None -> sb.AppendLine($"  {name}") |> ignore

        sb.AppendLine() |> ignore
        sb.AppendLine("Use 'frank help' to see all commands and topics.") |> ignore
        sb.ToString()

    let formatOpenApiValidateResult (result: OpenApiValidateCommand.OpenApiValidateResult) : string =
        Frank.Cli.Core.Statechart.ValidationReportFormatter.formatText result.Report

    let formatStatechartGenerateResult (result: StatechartGenerateCommand.GenerateResult) : string =
        if result.Artifacts |> List.isEmpty && result.GenerationErrors |> List.isEmpty then
            "No state machines found in the assembly."
        else
            let sb = System.Text.StringBuilder()
            let groupedByResource = result.Artifacts |> List.groupBy (fun a -> a.RouteTemplate)

            for (route, arts) in groupedByResource do
                let slug = (List.head arts).ResourceSlug
                let header = bold (sprintf "=== Resource: %s (%s) ===" route slug)
                sb.AppendLine(header) |> ignore
                sb.AppendLine() |> ignore

                for a in arts do
                    let fmtName = Frank.Cli.Core.Statechart.FormatDetector.FormatTag.toString a.Format
                    sb.AppendLine(sprintf "--- %s ---" fmtName) |> ignore

                    match a.FilePath with
                    | Some p -> sb.AppendLine(sprintf "Written to: %s" p) |> ignore
                    | None -> sb.AppendLine(a.Content) |> ignore

                    sb.AppendLine() |> ignore

            if not result.GenerationErrors.IsEmpty then
                sb.AppendLine() |> ignore

                sb.AppendLine(red (sprintf "Generation errors (%d):" result.GenerationErrors.Length))
                |> ignore

                for err in result.GenerationErrors do
                    sb.AppendLine(sprintf "  - %s" (Frank.Cli.Core.Statechart.StatechartError.formatError err))
                    |> ignore

            sb.ToString()

    let formatStatechartValidateResult (result: StatechartValidateCommand.ValidateResult) : string =
        Frank.Cli.Core.Statechart.ValidationReportFormatter.formatText result.Report

    let formatStatechartParseResult (result: StatechartParseCommand.ParseCommandResult) : string =
        Frank.Cli.Core.Statechart.StatechartDocumentJson.serializeParseResult result.ParseResult

    let formatError (message: string) : string = red (sprintf "Error: %s" message)

    let formatStatechartError (error: Frank.Cli.Core.Statechart.StatechartError.StatechartError) : string =
        red (sprintf "Error: %s" (Frank.Cli.Core.Statechart.StatechartError.formatError error))

    let formatStatechartExtractResult (result: StatechartExtractCommand.ExtractResult) : string =
        if result.StateMachines |> List.isEmpty then
            "No state machines found in the assembly."
        else
            let sb = System.Text.StringBuilder()

            for sm in result.StateMachines do
                let header = bold (sprintf "Statechart: %s" sm.RouteTemplate)
                sb.AppendLine(header) |> ignore
                sb.AppendLine(sprintf "  Initial State: %s" sm.InitialStateKey) |> ignore
                let stateList = sm.StateNames |> String.concat ", "
                sb.AppendLine(sprintf "  States: %s" stateList) |> ignore

                if not sm.GuardNames.IsEmpty then
                    let guardList = sm.GuardNames |> String.concat ", "
                    sb.AppendLine(sprintf "  Guards: %s" guardList) |> ignore

                sb.AppendLine("  State Details:") |> ignore

                for KeyValue(name, info) in sm.StateMetadata do
                    sb.AppendLine(sprintf "    %s:" name) |> ignore
                    let methods = info.AllowedMethods |> String.concat ", "
                    sb.AppendLine(sprintf "      Methods: %s" methods) |> ignore
                    sb.AppendLine(sprintf "      Final: %b" info.IsFinal) |> ignore

                    match info.Description with
                    | Some desc -> sb.AppendLine(sprintf "      Description: %s" desc) |> ignore
                    | None -> ()

                sb.AppendLine() |> ignore

            sb.ToString()

    let formatProgressReport (report: Frank.Resources.Model.ProgressAnalysis.ProgressReport) : string =
        let sb = System.Text.StringBuilder()

        sb.AppendLine(
            bold (
                sprintf
                    "Progress analysis for %s (%d states, %d roles)"
                    report.Route
                    report.StatesAnalyzed
                    (List.length report.RolesAnalyzed)
            )
        )
        |> ignore

        if List.isEmpty report.Diagnostics then
            sb.AppendLine(green "  No progress issues detected") |> ignore
        else
            for diag in report.Diagnostics do
                match diag with
                | Frank.Resources.Model.ProgressAnalysis.Deadlock(state, selfLoops) ->
                    let loopInfo =
                        if List.isEmpty selfLoops then
                            ""
                        else
                            sprintf " (self-loops: %s)" (String.concat ", " selfLoops)

                    sb.AppendLine(
                        red (sprintf "  [error] Deadlock: state %s has no advancing transitions%s" state loopInfo)
                    )
                    |> ignore
                | Frank.Resources.Model.ProgressAnalysis.Starvation(role, excludedAfter, excludedStates) ->
                    let statesInfo =
                        if List.isEmpty excludedStates then
                            ""
                        else
                            sprintf " (excluded from: %s)" (String.concat ", " excludedStates)

                    sb.AppendLine(
                        yellow (
                            sprintf
                                "  [warn]  Starvation: role %s excluded after state %s%s"
                                role
                                excludedAfter
                                statesInfo
                        )
                    )
                    |> ignore
                | Frank.Resources.Model.ProgressAnalysis.ReadOnlyRole role ->
                    sb.AppendLine(sprintf "  [info]  Read-only role: %s (expected for observers)" role)
                    |> ignore

        sb.ToString()

    let formatResourceValidateResult (result: ValidateResourcesCommand.ValidateResult) : string =
        let sb = System.Text.StringBuilder()
        let appendLine (s: string) = sb.AppendLine(s) |> ignore

        let statusText = if result.TotalErrors > 0 then red "FAIL" else green "PASS"

        appendLine (bold (sprintf "Projection Validation: %s" statusText))
        appendLine ""
        appendLine $"Resources checked: {result.ResourcesChecked}"

        let totalChecks = result.ProjectionResults |> List.sumBy _.ChecksRun

        appendLine $"Checks run: {totalChecks}"
        appendLine $"Issues: {result.TotalIssues} ({result.TotalErrors} errors, {result.TotalWarnings} warnings)"

        for r in result.ProjectionResults do
            if not r.Issues.IsEmpty then
                appendLine ""
                appendLine $"  {r.ResourceRoute}:"

                for issue in r.Issues do
                    let prefix =
                        match issue.Severity with
                        | ProjectionValidator.Severity.Error -> red "ERROR"
                        | ProjectionValidator.Severity.Warning -> yellow "WARN"

                    let checkName = ProjectionValidator.ProjectionCheckKind.toString issue.Check
                    appendLine $"    [%s{prefix}] %s{checkName}: %s{issue.Message}"

        if not (List.isEmpty result.ProgressReports) then
            appendLine ""

            for report in result.ProgressReports do
                sb.Append(formatProgressReport report: string) |> ignore

        if result.FromCache then
            appendLine ""
            appendLine "(from cache)"

        sb.ToString()

    let formatResourceExtractResult (result: ExtractResourcesCommand.ExtractResult) : string =
        let sb = System.Text.StringBuilder()
        let appendLine (s: string) = sb.AppendLine(s) |> ignore

        if result.FromCache then
            appendLine "Loaded from cache (source unchanged)"
        else
            appendLine "Extracted from source"

        appendLine ""
        appendLine $"Resources: {result.ResourceCount}"
        appendLine $"  Stateful: {result.StatefulResourceCount}"
        appendLine $"  Plain: {result.PlainResourceCount}"
        appendLine $"Types analyzed: {result.TypeCount}"

        for resource in result.State.Resources do
            appendLine ""
            appendLine $"  {resource.RouteTemplate}"
            appendLine $"    Slug: {resource.ResourceSlug}"
            appendLine $"    Types: {resource.TypeInfo.Length}"

            match resource.Statechart with
            | None ->
                let methods = resource.HttpCapabilities |> List.map _.Method |> String.concat ", "

                appendLine $"    Methods: {methods}"
            | Some sc ->
                let stateList = sc.StateNames |> String.concat ", "
                appendLine $"    States: {stateList}"
                appendLine $"    Initial: {sc.InitialStateKey}"

                if not sc.GuardNames.IsEmpty then
                    let guardList = sc.GuardNames |> String.concat ", "
                    appendLine $"    Guards: {guardList}"

                for stateName in sc.StateNames do
                    let methods =
                        sc.StateMetadata
                        |> Map.tryFind stateName
                        |> Option.map (fun si -> si.AllowedMethods |> String.concat ", ")
                        |> Option.defaultValue ""

                    appendLine $"      {stateName}: {methods}"

        if not result.Warnings.IsEmpty then
            appendLine ""
            appendLine "Warnings:"

            for w in result.Warnings do
                appendLine $"  - {w}"

        appendLine ""
        appendLine $"Cache: {result.CacheFilePath}"
        sb.ToString()
