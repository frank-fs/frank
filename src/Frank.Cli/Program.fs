module Program

open System
open System.CommandLine
open Frank.Cli.Core.Commands
open Frank.Cli.Core.Output
open Frank.Cli.Core.Help
open Frank.Cli.Core.Statechart
open Frank.Cli.Core.Unified

[<EntryPoint>]
let main args =
    let root =
        RootCommand("frank-cli: Semantic resource extraction for Frank applications")

    // ── semantic (parent) ──
    let semanticCmd = Command("semantic")
    semanticCmd.Description <- "Semantic resource extraction pipeline: OWL ontology and SHACL shapes from F# types"
    root.Subcommands.Add(semanticCmd)

    // ── semantic extract ──
    let extractCmd = Command("extract")
    extractCmd.Description <- "Extract semantic definitions from F# source"
    let extractProjectOpt = Option<string>("--project")
    extractProjectOpt.Description <- "Path to .fsproj file"
    extractProjectOpt.Required <- true
    let baseUriOpt = Option<string>("--base-uri")
    baseUriOpt.Description <- "Base URI for the ontology"
    baseUriOpt.Required <- true
    let vocabOpt = Option<string array>("--vocabularies")
    vocabOpt.Description <- "Vocabulary namespaces to align"
    vocabOpt.DefaultValueFactory <- (fun _ -> [| "schema.org" |])
    let extractFormatOpt = Option<string>("--output-format")
    extractFormatOpt.Description <- "Output format (text|json)"
    extractFormatOpt.DefaultValueFactory <- (fun _ -> "text")
    extractCmd.Options.Add(extractProjectOpt)
    extractCmd.Options.Add(baseUriOpt)
    extractCmd.Options.Add(vocabOpt)
    extractCmd.Options.Add(extractFormatOpt)

    extractCmd.SetAction(fun parseResult ->
        let project = parseResult.GetValue(extractProjectOpt)
        let baseUri = parseResult.GetValue(baseUriOpt)
        let vocabs = parseResult.GetValue(vocabOpt)
        let format = parseResult.GetValue(extractFormatOpt)

        let result =
            ExtractCommand.execute project (Uri baseUri) (Array.toList vocabs)
            |> Async.RunSynchronously

        match result with
        | Ok r ->
            let output =
                if format = "json" then
                    JsonOutput.formatExtractResult r
                else
                    TextOutput.formatExtractResult r

            Console.WriteLine(output)
        | Error e ->
            Environment.ExitCode <- 1

            let output =
                if format = "json" then
                    JsonOutput.formatError e
                else
                    TextOutput.formatError e

            Console.Error.WriteLine(output))

    semanticCmd.Subcommands.Add(extractCmd)

    // ── semantic clarify ──
    let clarifyCmd = Command("clarify")
    clarifyCmd.Description <- "Identify ambiguities requiring human input"
    let clarifyProjectOpt = Option<string>("--project")
    clarifyProjectOpt.Description <- "Path to .fsproj file"
    clarifyProjectOpt.Required <- true
    let clarifyFormatOpt = Option<string>("--output-format")
    clarifyFormatOpt.Description <- "Output format (text|json)"
    clarifyFormatOpt.DefaultValueFactory <- (fun _ -> "text")
    clarifyCmd.Options.Add(clarifyProjectOpt)
    clarifyCmd.Options.Add(clarifyFormatOpt)

    clarifyCmd.SetAction(fun parseResult ->
        let project = parseResult.GetValue(clarifyProjectOpt)
        let format = parseResult.GetValue(clarifyFormatOpt)

        let result = ClarifyCommand.execute project

        match result with
        | Ok r ->
            let output =
                if format = "json" then
                    JsonOutput.formatClarifyResult r
                else
                    TextOutput.formatClarifyResult r

            Console.WriteLine(output)
        | Error e ->
            Environment.ExitCode <- 1

            let output =
                if format = "json" then
                    JsonOutput.formatError e
                else
                    TextOutput.formatError e

            Console.Error.WriteLine(output))

    semanticCmd.Subcommands.Add(clarifyCmd)

    // ── semantic validate ──
    let validateCmd = Command("validate")
    validateCmd.Description <- "Validate completeness and consistency of extracted definitions"
    let validateProjectOpt = Option<string>("--project")
    validateProjectOpt.Description <- "Path to .fsproj file"
    validateProjectOpt.Required <- true
    let validateFormatOpt = Option<string>("--output-format")
    validateFormatOpt.Description <- "Output format (text|json)"
    validateFormatOpt.DefaultValueFactory <- (fun _ -> "text")
    validateCmd.Options.Add(validateProjectOpt)
    validateCmd.Options.Add(validateFormatOpt)

    validateCmd.SetAction(fun parseResult ->
        let project = parseResult.GetValue(validateProjectOpt)
        let format = parseResult.GetValue(validateFormatOpt)

        let result = ValidateCommand.execute project

        match result with
        | Ok r ->
            let output =
                if format = "json" then
                    JsonOutput.formatValidateResult r
                else
                    TextOutput.formatValidateResult r

            Console.WriteLine(output)
        | Error e ->
            Environment.ExitCode <- 1

            let output =
                if format = "json" then
                    JsonOutput.formatError e
                else
                    TextOutput.formatError e

            Console.Error.WriteLine(output))

    semanticCmd.Subcommands.Add(validateCmd)

    // ── semantic diff ──
    let diffCmd = Command("diff")
    diffCmd.Description <- "Compare current extraction state with a previous snapshot"
    let diffProjectOpt = Option<string>("--project")
    diffProjectOpt.Description <- "Path to .fsproj file"
    diffProjectOpt.Required <- true
    let previousOpt = Option<string>("--previous")
    previousOpt.Description <- "Path to previous state file (auto-detects latest backup if omitted)"
    let diffFormatOpt = Option<string>("--output-format")
    diffFormatOpt.Description <- "Output format (text|json)"
    diffFormatOpt.DefaultValueFactory <- (fun _ -> "text")
    diffCmd.Options.Add(diffProjectOpt)
    diffCmd.Options.Add(previousOpt)
    diffCmd.Options.Add(diffFormatOpt)

    diffCmd.SetAction(fun parseResult ->
        let project = parseResult.GetValue(diffProjectOpt)
        let previous = parseResult.GetValue(previousOpt)
        let format = parseResult.GetValue(diffFormatOpt)

        let prevPath =
            if String.IsNullOrEmpty previous then
                None
            else
                Some previous

        let result = DiffCommand.execute project prevPath

        match result with
        | Ok r ->
            let output =
                if format = "json" then
                    JsonOutput.formatDiffResult r
                else
                    TextOutput.formatDiffResult r

            Console.WriteLine(output)
        | Error e ->
            Environment.ExitCode <- 1

            let output =
                if format = "json" then
                    JsonOutput.formatError e
                else
                    TextOutput.formatError e

            Console.Error.WriteLine(output))

    semanticCmd.Subcommands.Add(diffCmd)

    // ── semantic compile ──
    let compileCmd = Command("compile")

    compileCmd.Description <-
        "Generate OWL/XML and SHACL artifacts from extraction state, or run extraction + emit in one shot via --project"

    let compileProjectOpt = Option<string>("--project")

    compileProjectOpt.Description <-
        "Path to .fsproj file. When provided together with --base-uri, runs extraction and artifact emission in one shot (used by MSBuild auto-invoke)."

    compileProjectOpt.Required <- true
    let compileBaseUriOpt = Option<string>("--base-uri")

    compileBaseUriOpt.Description <-
        "Base URI for the ontology (required when running unified extract+compile via --project)"

    let compileVocabOpt = Option<string array>("--vocabularies")
    compileVocabOpt.Description <- "Vocabulary namespaces to align (used with unified extract+compile path)"
    compileVocabOpt.DefaultValueFactory <- (fun _ -> [| "schema.org" |])
    let compileOutputOpt = Option<string>("--output")
    compileOutputOpt.Description <- "Output directory for compiled artifacts"
    let compileFormatOpt = Option<string>("--output-format")
    compileFormatOpt.Description <- "Output format (text|json)"
    compileFormatOpt.DefaultValueFactory <- (fun _ -> "text")
    compileCmd.Options.Add(compileProjectOpt)
    compileCmd.Options.Add(compileBaseUriOpt)
    compileCmd.Options.Add(compileVocabOpt)
    compileCmd.Options.Add(compileOutputOpt)
    compileCmd.Options.Add(compileFormatOpt)

    compileCmd.SetAction(fun parseResult ->
        let project = parseResult.GetValue(compileProjectOpt)
        let baseUri = parseResult.GetValue(compileBaseUriOpt)
        let vocabs = parseResult.GetValue(compileVocabOpt)
        let outputDir = parseResult.GetValue(compileOutputOpt)
        let format = parseResult.GetValue(compileFormatOpt)

        let outDir =
            if String.IsNullOrEmpty outputDir then
                None
            else
                Some outputDir

        // When --base-uri is supplied, run the unified extract+compile path.
        // Otherwise fall back to the state-file-only compile path.
        let result =
            if not (String.IsNullOrEmpty baseUri) then
                CompileCommand.compileFromProject project (Uri baseUri) (Array.toList vocabs) outDir
                |> Async.RunSynchronously
            else
                CompileCommand.execute project outDir

        match result with
        | Ok r ->
            let output =
                if format = "json" then
                    JsonOutput.formatCompileResult r
                else
                    TextOutput.formatCompileResult r

            Console.WriteLine(output)
        | Error e ->
            Environment.ExitCode <- 1

            let output =
                if format = "json" then
                    JsonOutput.formatError e
                else
                    TextOutput.formatError e

            Console.Error.WriteLine(output))

    semanticCmd.Subcommands.Add(compileCmd)

    // ── semantic openapi-validate ──
    let openApiValCmd = Command("openapi-validate")
    openApiValCmd.Description <- "Validate consistency between F# types and OpenAPI schema components"
    let oavProjectOpt = Option<string>("--project")
    oavProjectOpt.Description <- "Path to .fsproj file"
    oavProjectOpt.Required <- true
    let oavOpenApiOpt = Option<string>("--openapi")
    oavOpenApiOpt.Description <- "Path to OpenAPI JSON document"
    oavOpenApiOpt.Required <- true
    let oavFormatOpt = Option<string>("--output-format")
    oavFormatOpt.Description <- "Output format (text|json)"
    oavFormatOpt.DefaultValueFactory <- (fun _ -> "text")
    openApiValCmd.Options.Add(oavProjectOpt)
    openApiValCmd.Options.Add(oavOpenApiOpt)
    openApiValCmd.Options.Add(oavFormatOpt)

    openApiValCmd.SetAction(fun parseResult ->
        let project = parseResult.GetValue(oavProjectOpt)
        let openApiPath = parseResult.GetValue(oavOpenApiOpt)
        let format = parseResult.GetValue(oavFormatOpt)

        let result = OpenApiValidateCommand.execute project openApiPath

        match result with
        | Ok r ->
            let output =
                if format = "json" then
                    JsonOutput.formatOpenApiValidateResult r
                else
                    TextOutput.formatOpenApiValidateResult r

            Console.WriteLine(output)

            if r.Report.TotalFailures > 0 then
                Environment.ExitCode <- 1
        | Error e ->
            Environment.ExitCode <- 1

            let output =
                if format = "json" then
                    JsonOutput.formatError e
                else
                    TextOutput.formatError e

            Console.Error.WriteLine(output))

    semanticCmd.Subcommands.Add(openApiValCmd)

    // ── statechart (parent) ──
    let statechartCmd = Command("statechart")
    statechartCmd.Description <- "Statechart pipeline: extract, generate, validate, and parse state machine artifacts"
    root.Subcommands.Add(statechartCmd)

    // ── statechart extract ──
    let scExtractCmd = Command("extract")
    scExtractCmd.Description <- "Extract state machine metadata from F# source using the compiler"

    let scExtractProjectOpt = Option<string>("--project")
    scExtractProjectOpt.Description <- "Path to .fsproj file"
    scExtractProjectOpt.Required <- true
    scExtractCmd.Options.Add(scExtractProjectOpt)

    let scExtractFormatOpt = Option<string>("--output-format")
    scExtractFormatOpt.Description <- "Output format (text|json)"
    scExtractFormatOpt.DefaultValueFactory <- (fun _ -> "text")
    scExtractCmd.Options.Add(scExtractFormatOpt)

    scExtractCmd.SetAction(fun parseResult ->
        let project = parseResult.GetValue(scExtractProjectOpt)
        let format = parseResult.GetValue(scExtractFormatOpt)

        let result = StatechartExtractCommand.execute project |> Async.RunSynchronously

        match result with
        | Ok r ->
            let output =
                if format = "json" then
                    JsonOutput.formatStatechartExtractResult r
                else
                    TextOutput.formatStatechartExtractResult r

            Console.WriteLine(output)
        | Error e ->
            Environment.ExitCode <- 1

            let output =
                if format = "json" then JsonOutput.formatStatechartError e
                else TextOutput.formatStatechartError e

            Console.Error.WriteLine(output))

    statechartCmd.Subcommands.Add(scExtractCmd)

    // ── statechart generate ──
    let scGenerateCmd = Command("generate")
    scGenerateCmd.Description <- "Generate statechart spec artifacts from F# source"

    let scGenProjectOpt = Option<string>("--project")
    scGenProjectOpt.Description <- "Path to .fsproj file"
    scGenProjectOpt.Required <- true
    scGenerateCmd.Options.Add(scGenProjectOpt)

    let scGenFormatOpt = Option<string>("--format")
    scGenFormatOpt.Description <- "Target notation format (wsd|alps|scxml|smcat|xstate|affordance-map|all)"
    scGenFormatOpt.Required <- true
    scGenerateCmd.Options.Add(scGenFormatOpt)

    let scGenBaseUriOpt = Option<string>("--base-uri")
    scGenBaseUriOpt.Description <- "Base URI for ALPS profile namespace (required for affordance-map format)"
    scGenerateCmd.Options.Add(scGenBaseUriOpt)

    let scGenOutputOpt = Option<string>("--output")
    scGenOutputOpt.Description <- "Output directory for generated artifacts"
    scGenerateCmd.Options.Add(scGenOutputOpt)

    let scGenResourceOpt = Option<string>("--resource")
    scGenResourceOpt.Description <- "Generate for a specific resource only"
    scGenerateCmd.Options.Add(scGenResourceOpt)

    let scGenOutputFormatOpt = Option<string>("--output-format")
    scGenOutputFormatOpt.Description <- "Format for status messages (text|json)"
    scGenOutputFormatOpt.DefaultValueFactory <- (fun _ -> "text")
    scGenerateCmd.Options.Add(scGenOutputFormatOpt)

    scGenerateCmd.SetAction(fun parseResult ->
        let project = parseResult.GetValue(scGenProjectOpt)
        let format = parseResult.GetValue(scGenFormatOpt)

        let outputDir =
            let v = parseResult.GetValue(scGenOutputOpt)
            if String.IsNullOrEmpty v then None else Some v

        let resource =
            let v = parseResult.GetValue(scGenResourceOpt)
            if String.IsNullOrEmpty v then None else Some v

        let genBaseUri =
            let v = parseResult.GetValue(scGenBaseUriOpt)
            if String.IsNullOrEmpty v then "http://localhost:5000/alps" else v

        let outputFormat = parseResult.GetValue(scGenOutputFormatOpt)

        let result =
            StatechartGenerateCommand.executeWithBaseUri project format genBaseUri outputDir resource
            |> Async.RunSynchronously

        match result with
        | Ok r ->
            // If affordance-map JSON is present, output it directly
            match r.AffordanceMapJson with
            | Some json ->
                Console.WriteLine(json)
            | None ->
                if outputDir.IsNone then
                    for artifact in r.Artifacts do
                        let fmtName = FormatDetector.FormatTag.toString artifact.Format
                        Console.WriteLine($"=== {artifact.RouteTemplate} ({fmtName}) ===")
                        Console.WriteLine(artifact.Content)
                        Console.WriteLine()
                else
                    let output =
                        if outputFormat = "json" then
                            JsonOutput.formatStatechartGenerateResult r
                        else
                            TextOutput.formatStatechartGenerateResult r

                    Console.WriteLine(output)

            // Report generation errors
            if not r.GenerationErrors.IsEmpty then
                Environment.ExitCode <- 1
                for err in r.GenerationErrors do
                    Console.Error.WriteLine(StatechartError.formatError err)
        | Error e ->
            Environment.ExitCode <- 1

            let output =
                if outputFormat = "json" then JsonOutput.formatStatechartError e
                else TextOutput.formatStatechartError e

            Console.Error.WriteLine(output))

    statechartCmd.Subcommands.Add(scGenerateCmd)

    // ── statechart validate ──
    let scValidateCmd = Command("validate")
    scValidateCmd.Description <- "Validate statechart spec files for cross-format consistency"

    let scValSpecFilesArg = Argument<string array>("spec-files")
    scValSpecFilesArg.Description <- "One or more spec files to validate"
    scValSpecFilesArg.Arity <- ArgumentArity.OneOrMore
    scValidateCmd.Arguments.Add(scValSpecFilesArg)

    let scValFormatOpt = Option<string>("--output-format")
    scValFormatOpt.Description <- "Output format (text|json)"
    scValFormatOpt.DefaultValueFactory <- (fun _ -> "text")
    scValidateCmd.Options.Add(scValFormatOpt)

    scValidateCmd.SetAction(fun parseResult ->
        let specFiles = parseResult.GetValue(scValSpecFilesArg) |> Array.toList
        let format = parseResult.GetValue(scValFormatOpt)

        let result = StatechartValidateCommand.execute specFiles

        match result with
        | Ok r ->
            let output =
                if format = "json" then
                    JsonOutput.formatStatechartValidateResult r
                else
                    TextOutput.formatStatechartValidateResult r

            Console.WriteLine(output)

            if r.HasFailures then
                Environment.ExitCode <- 1
        | Error e ->
            Environment.ExitCode <- 1

            let output =
                if format = "json" then JsonOutput.formatStatechartError e
                else TextOutput.formatStatechartError e

            Console.Error.WriteLine(output))

    statechartCmd.Subcommands.Add(scValidateCmd)

    // ── statechart parse ──
    let scParseCmd = Command("parse")
    scParseCmd.Description <- "Parse a spec file and output the StatechartDocument as JSON"

    let scParseFileArg = Argument<string>("spec-file")
    scParseFileArg.Description <- "Path to the spec file to parse"
    scParseCmd.Arguments.Add(scParseFileArg)

    let scParseFormatOpt = Option<string>("--format")
    scParseFormatOpt.Description <- "Notation format override (wsd|alps|scxml|smcat|xstate) for ambiguous file extensions"
    scParseCmd.Options.Add(scParseFormatOpt)

    let scParseOutputFormatOpt = Option<string>("--output-format")
    scParseOutputFormatOpt.Description <- "Output format (text|json)"
    scParseOutputFormatOpt.DefaultValueFactory <- (fun _ -> "json")
    scParseCmd.Options.Add(scParseOutputFormatOpt)

    scParseCmd.SetAction(fun parseResult ->
        let specFile = parseResult.GetValue(scParseFileArg)
        let outputFormat = parseResult.GetValue(scParseOutputFormatOpt)

        let formatOverride =
            let v = parseResult.GetValue(scParseFormatOpt)
            if String.IsNullOrEmpty v then None else Some v

        let result = StatechartParseCommand.execute specFile formatOverride

        match result with
        | Ok r ->
            // Parse output is always JSON (the document IS the content)
            let output = StatechartDocumentJson.serializeParseResultWithFormat r.ParseResult r.Format
            Console.WriteLine(output)

            if r.HasErrors then
                Environment.ExitCode <- 1
        | Error e ->
            Environment.ExitCode <- 1

            let output =
                if outputFormat = "json" then JsonOutput.formatStatechartError e
                else TextOutput.formatStatechartError e

            Console.Error.WriteLine(output))

    statechartCmd.Subcommands.Add(scParseCmd)

    // ── extract (top-level, unified) ──
    let uniExtractCmd = Command("extract")
    uniExtractCmd.Description <- "Extract unified resource descriptions from F# source (replaces semantic extract + statechart extract)"

    let uniExtractProjectOpt = Option<string>("--project")
    uniExtractProjectOpt.Description <- "Path to .fsproj file"
    uniExtractProjectOpt.Required <- true
    let uniExtractBaseUriOpt = Option<string>("--base-uri")
    uniExtractBaseUriOpt.Description <- "Base URI for ALPS profiles"
    uniExtractBaseUriOpt.DefaultValueFactory <- (fun _ -> "http://example.org/")
    let uniExtractVocabOpt = Option<string array>("--vocabularies")
    uniExtractVocabOpt.Description <- "Vocabulary namespaces to align"
    uniExtractVocabOpt.DefaultValueFactory <- (fun _ -> [| "schema.org" |])
    let uniExtractForceOpt = Option<bool>("--force")
    uniExtractForceOpt.Description <- "Force re-extraction, bypassing cache"
    uniExtractForceOpt.DefaultValueFactory <- (fun _ -> false)
    let uniExtractFormatOpt = Option<string>("--output-format")
    uniExtractFormatOpt.Description <- "Output format (text|json)"
    uniExtractFormatOpt.DefaultValueFactory <- (fun _ -> "text")
    uniExtractCmd.Options.Add(uniExtractProjectOpt)
    uniExtractCmd.Options.Add(uniExtractBaseUriOpt)
    uniExtractCmd.Options.Add(uniExtractVocabOpt)
    uniExtractCmd.Options.Add(uniExtractForceOpt)
    uniExtractCmd.Options.Add(uniExtractFormatOpt)

    uniExtractCmd.SetAction(fun parseResult ->
        let project = parseResult.GetValue(uniExtractProjectOpt)
        let baseUri = parseResult.GetValue(uniExtractBaseUriOpt)
        let vocabs = parseResult.GetValue(uniExtractVocabOpt) |> Array.toList
        let force = parseResult.GetValue(uniExtractForceOpt)
        let format = parseResult.GetValue(uniExtractFormatOpt)

        let result =
            UnifiedExtractCommand.execute project baseUri vocabs force
            |> Async.RunSynchronously

        match result with
        | Ok extractResult ->
            let output =
                if format = "json" then
                    JsonOutput.formatUnifiedExtractResult extractResult
                else
                    TextOutput.formatUnifiedExtractResult extractResult

            Console.WriteLine(output)
        | Error e ->
            Environment.ExitCode <- 1
            Console.Error.WriteLine(StatechartError.formatError e))

    root.Subcommands.Add(uniExtractCmd)

    // ── generate (top-level, unified) ──
    let uniGenCmd = Command("generate")
    uniGenCmd.Description <- "Generate format artifacts from unified extraction (wsd, alps, scxml, smcat, xstate, affordance-map, all)"

    let uniGenProjectOpt = Option<string>("--project")
    uniGenProjectOpt.Description <- "Path to .fsproj file"
    uniGenProjectOpt.Required <- true
    let uniGenFormatOpt = Option<string>("--format")
    uniGenFormatOpt.Description <- "Target format (wsd|alps|alps-xml|scxml|smcat|xstate|affordance-map|all)"
    uniGenFormatOpt.Required <- true
    let uniGenOutputOpt = Option<string>("--output")
    uniGenOutputOpt.Description <- "Output directory for generated artifacts"
    let uniGenResourceOpt = Option<string>("--resource")
    uniGenResourceOpt.Description <- "Generate for a specific resource only"
    let uniGenForceOpt = Option<bool>("--force")
    uniGenForceOpt.Description <- "Force re-extraction, bypassing cache"
    uniGenForceOpt.DefaultValueFactory <- (fun _ -> false)
    uniGenCmd.Options.Add(uniGenProjectOpt)
    uniGenCmd.Options.Add(uniGenFormatOpt)
    uniGenCmd.Options.Add(uniGenOutputOpt)
    uniGenCmd.Options.Add(uniGenResourceOpt)
    uniGenCmd.Options.Add(uniGenForceOpt)

    uniGenCmd.SetAction(fun parseResult ->
        let project = parseResult.GetValue(uniGenProjectOpt)
        let format = parseResult.GetValue(uniGenFormatOpt)

        let outputDir =
            let v = parseResult.GetValue(uniGenOutputOpt)
            if String.IsNullOrEmpty v then None else Some v

        let resource =
            let v = parseResult.GetValue(uniGenResourceOpt)
            if String.IsNullOrEmpty v then None else Some v

        let force = parseResult.GetValue(uniGenForceOpt)

        let result =
            UnifiedGenerateCommand.execute project format outputDir resource force
            |> Async.RunSynchronously

        match result with
        | Ok genResult ->
            for artifact in genResult.Artifacts do
                match artifact.FilePath with
                | Some fp -> Console.WriteLine($"Generated: {fp}")
                | None -> Console.WriteLine(artifact.Content)
        | Error e ->
            Environment.ExitCode <- 1
            Console.Error.WriteLine(StatechartError.formatError e))

    root.Subcommands.Add(uniGenCmd)

    // ── status (top-level) ──
    let statusCmd = Command("status")
    statusCmd.Description <- "Show project extraction and compilation status"
    let statusProjectOpt = Option<string>("--project")
    statusProjectOpt.Description <- "Path to .fsproj file"
    statusProjectOpt.Required <- true
    let statusFormatOpt = Option<string>("--output-format")
    statusFormatOpt.Description <- "Output format (text|json)"
    statusFormatOpt.DefaultValueFactory <- (fun _ -> "text")
    statusCmd.Options.Add(statusProjectOpt)
    statusCmd.Options.Add(statusFormatOpt)

    statusCmd.SetAction(fun parseResult ->
        let project = parseResult.GetValue(statusProjectOpt)
        let format = parseResult.GetValue(statusFormatOpt)

        let result = StatusCommand.execute project

        match result with
        | Ok r ->
            let output =
                if format = "json" then
                    JsonOutput.formatStatusResult r
                else
                    TextOutput.formatStatusResult r

            Console.WriteLine(output)
        | Error e ->
            Environment.ExitCode <- 1

            let output =
                if format = "json" then
                    JsonOutput.formatError e
                else
                    TextOutput.formatError e

            Console.Error.WriteLine(output))

    root.Subcommands.Add(statusCmd)

    // ── help (top-level) ──
    let helpCmd = Command("help")
    helpCmd.Description <- "Show help topics and command documentation"

    let helpArgArg = Argument<string array>("args")
    helpArgArg.Description <- "Command or topic name (e.g. 'extract', 'statechart extract', 'workflows')"
    helpArgArg.Arity <- ArgumentArity.ZeroOrMore
    helpCmd.Arguments.Add(helpArgArg)

    let helpFormatOpt = Option<string>("--output-format")
    helpFormatOpt.Description <- "Output format (text|json)"
    helpFormatOpt.DefaultValueFactory <- (fun _ -> "text")
    helpCmd.Options.Add(helpFormatOpt)

    helpCmd.SetAction(fun parseResult ->
        let args = parseResult.GetValue(helpArgArg)
        let format = parseResult.GetValue(helpFormatOpt)

        if args = null || args.Length = 0 then
            let index = HelpSubcommand.listAll ()

            let output =
                if format = "json" then
                    JsonOutput.formatHelpIndex index
                else
                    TextOutput.formatHelpIndex index

            Console.WriteLine(output)
        else
            // Join multiple args as a single qualified name (e.g. ["statechart"; "extract"] -> "statechart extract")
            let query = args |> String.concat " "

            match HelpSubcommand.resolve query with
            | CommandMatch cmd ->
                let output =
                    if format = "json" then
                        HelpRenderer.renderCommandJson cmd HelpContent.pipelineStepCount
                    else
                        HelpRenderer.renderFullCommandText cmd HelpContent.pipelineStepCount

                Console.WriteLine(output)
            | TopicMatch topic ->
                let output =
                    if format = "json" then
                        JsonOutput.formatTopicJson topic
                    else
                        TextOutput.formatTopicText topic

                Console.WriteLine(output)
            | NoMatch suggestions ->
                Environment.ExitCode <- 1

                let output =
                    if format = "json" then
                        JsonOutput.formatNoMatch query suggestions
                    else
                        TextOutput.formatNoMatch query suggestions

                Console.Error.WriteLine(output))

    root.Subcommands.Add(helpCmd)

    let parseResult = root.Parse(args)
    let exitCode = parseResult.Invoke()

    if exitCode <> 0 then exitCode else Environment.ExitCode
