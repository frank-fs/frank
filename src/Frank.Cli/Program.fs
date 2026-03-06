module Program

open System
open System.CommandLine
open Frank.Cli.Core.Commands
open Frank.Cli.Core.Output

[<EntryPoint>]
let main args =
    let root =
        RootCommand("frank-cli: Semantic resource extraction for Frank applications")

    // ── extract ──
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
    let scopeOpt = Option<string>("--scope")
    scopeOpt.Description <- "Extraction scope"
    scopeOpt.DefaultValueFactory <- (fun _ -> "full")
    let extractFormatOpt = Option<string>("--format")
    extractFormatOpt.Description <- "Output format (text|json)"
    extractFormatOpt.DefaultValueFactory <- (fun _ -> "text")
    extractCmd.Options.Add(extractProjectOpt)
    extractCmd.Options.Add(baseUriOpt)
    extractCmd.Options.Add(vocabOpt)
    extractCmd.Options.Add(scopeOpt)
    extractCmd.Options.Add(extractFormatOpt)

    extractCmd.SetAction(fun parseResult ->
        let project = parseResult.GetValue(extractProjectOpt)
        let baseUri = parseResult.GetValue(baseUriOpt)
        let vocabs = parseResult.GetValue(vocabOpt)
        let scope = parseResult.GetValue(scopeOpt)
        let format = parseResult.GetValue(extractFormatOpt)

        let result =
            ExtractCommand.execute project (Uri baseUri) (Array.toList vocabs) scope
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
            let output =
                if format = "json" then
                    JsonOutput.formatError e
                else
                    TextOutput.formatError e

            Console.Error.WriteLine(output))

    root.Subcommands.Add(extractCmd)

    // ── clarify ──
    let clarifyCmd = Command("clarify")
    clarifyCmd.Description <- "Identify ambiguities requiring human input"
    let clarifyProjectOpt = Option<string>("--project")
    clarifyProjectOpt.Description <- "Path to .fsproj file"
    clarifyProjectOpt.Required <- true
    let clarifyFormatOpt = Option<string>("--format")
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
            let output =
                if format = "json" then
                    JsonOutput.formatError e
                else
                    TextOutput.formatError e

            Console.Error.WriteLine(output))

    root.Subcommands.Add(clarifyCmd)

    // ── validate ──
    let validateCmd = Command("validate")
    validateCmd.Description <- "Validate completeness and consistency of extracted definitions"
    let validateProjectOpt = Option<string>("--project")
    validateProjectOpt.Description <- "Path to .fsproj file"
    validateProjectOpt.Required <- true
    let validateFormatOpt = Option<string>("--format")
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
            let output =
                if format = "json" then
                    JsonOutput.formatError e
                else
                    TextOutput.formatError e

            Console.Error.WriteLine(output))

    root.Subcommands.Add(validateCmd)

    // ── diff ──
    let diffCmd = Command("diff")
    diffCmd.Description <- "Compare current extraction state with a previous snapshot"
    let diffProjectOpt = Option<string>("--project")
    diffProjectOpt.Description <- "Path to .fsproj file"
    diffProjectOpt.Required <- true
    let previousOpt = Option<string>("--previous")
    previousOpt.Description <- "Path to previous state file (auto-detects latest backup if omitted)"
    let diffFormatOpt = Option<string>("--format")
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
            let output =
                if format = "json" then
                    JsonOutput.formatError e
                else
                    TextOutput.formatError e

            Console.Error.WriteLine(output))

    root.Subcommands.Add(diffCmd)

    // ── compile ──
    let compileCmd = Command("compile")
    compileCmd.Description <- "Generate OWL/XML and SHACL artifacts from extraction state"
    let compileProjectOpt = Option<string>("--project")
    compileProjectOpt.Description <- "Path to .fsproj file"
    compileProjectOpt.Required <- true
    let outputDirOpt = Option<string>("--output-dir")
    outputDirOpt.Description <- "Output directory for compiled artifacts"
    let compileFormatOpt = Option<string>("--format")
    compileFormatOpt.Description <- "Output format (text|json)"
    compileFormatOpt.DefaultValueFactory <- (fun _ -> "text")
    compileCmd.Options.Add(compileProjectOpt)
    compileCmd.Options.Add(outputDirOpt)
    compileCmd.Options.Add(compileFormatOpt)

    compileCmd.SetAction(fun parseResult ->
        let project = parseResult.GetValue(compileProjectOpt)
        let outputDir = parseResult.GetValue(outputDirOpt)
        let format = parseResult.GetValue(compileFormatOpt)

        let outDir =
            if String.IsNullOrEmpty outputDir then
                None
            else
                Some outputDir

        let result = CompileCommand.execute project outDir

        match result with
        | Ok r ->
            let output =
                if format = "json" then
                    JsonOutput.formatCompileResult r
                else
                    TextOutput.formatCompileResult r

            Console.WriteLine(output)
        | Error e ->
            let output =
                if format = "json" then
                    JsonOutput.formatError e
                else
                    TextOutput.formatError e

            Console.Error.WriteLine(output))

    root.Subcommands.Add(compileCmd)

    let parseResult = root.Parse(args)
    parseResult.Invoke()
