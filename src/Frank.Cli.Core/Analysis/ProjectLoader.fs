namespace Frank.Cli.Core.Analysis

open System
open System.Diagnostics
open System.IO
open System.Text.Json
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open Frank.Cli.Core.Shared

/// FSharpCheckProjectResults must be consumed within the same async scope where LoadedProject was created.
/// Do not cache or serialize across application sessions — the results reference internal compiler state
/// that becomes invalid if the FSharpChecker is disposed or recreated.
type LoadedProject =
    { ProjectPath: string
      ParsedFiles: (string * ParsedInput) list
      CheckResults: FSharpCheckProjectResults }

/// Pre-resolved MSBuild project options. JSON: { "sourceFiles", "references", "defines", "otherFlags" }
type ResolvedProjectOptions =
    { SourceFiles: string list
      References: string list
      Defines: string list
      OtherFlags: string list }

module ProjectLoader =

    /// Singleton FSharpChecker shared across all loadProject calls.
    /// Creating a new checker per call is expensive (~5 min each); the checker
    /// caches project snapshots internally, so reuse is both safe and fast.
    let private checker = FSharpChecker.Create(keepAssemblyContents = true)

    /// Checker for the MSBuild/extraction path — expression trees are not needed for
    /// affordance/type extraction, so keepAssemblyContents = false reduces peak memory.
    /// projectCacheSize = 1: the CLI processes one project per invocation, so there is
    /// nothing to gain from a larger project cache.
    let private extractionChecker =
        FSharpChecker.Create(keepAssemblyContents = false, projectCacheSize = 1)

    let private parseSourceFile (checker: FSharpChecker) (parsingOptions: FSharpParsingOptions) (sourceFile: string) =
        async {
            let sourceText = SourceText.ofString (File.ReadAllText sourceFile)

            let! parseResult = checker.ParseFile(sourceFile, sourceText, parsingOptions)

            if parseResult.ParseHadErrors then
                return None
            else
                return Some(sourceFile, parseResult.ParseTree)
        }

    /// Detect the first target framework for multi-targeted projects.
    let private detectTargetFramework (fsprojPath: string) : string option =
        let psi = ProcessStartInfo("dotnet")
        psi.Arguments <- $"msbuild \"{fsprojPath}\" -getProperty:TargetFrameworks -getProperty:TargetFramework"
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.UseShellExecute <- false
        psi.WorkingDirectory <- Path.GetDirectoryName fsprojPath

        use proc = Process.Start(psi)
        let stdout = proc.StandardOutput.ReadToEnd()
        proc.StandardError.ReadToEnd() |> ignore
        proc.WaitForExit()

        if proc.ExitCode <> 0 then
            None
        else
            tryFcs None (fun () ->
                let doc = JsonDocument.Parse(stdout)
                let props = doc.RootElement.GetProperty("Properties")

                match props.TryGetProperty("TargetFrameworks") with
                | true, v ->
                    let tfms = v.GetString()

                    if not (String.IsNullOrWhiteSpace tfms) then
                        let parts = tfms.Split(';', StringSplitOptions.RemoveEmptyEntries)

                        if parts.Length > 0 then
                            Some parts.[parts.Length - 1]
                        else
                            None
                    else
                        match props.TryGetProperty("TargetFramework") with
                        | true, v2 ->
                            let tfm = v2.GetString()
                            if String.IsNullOrWhiteSpace tfm then None else Some tfm
                        | _ -> None
                | _ ->
                    match props.TryGetProperty("TargetFramework") with
                    | true, v ->
                        let tfm = v.GetString()
                        if String.IsNullOrWhiteSpace tfm then None else Some tfm
                    | _ -> None)

    /// Run dotnet msbuild with structured JSON output to resolve project options.
    let private resolveProjectOptions
        (fsprojPath: string)
        : Result<string list * string list * string list * string list, string> =
        let tfmArg =
            match detectTargetFramework fsprojPath with
            | Some tfm -> $" /p:TargetFramework={tfm}"
            | None -> ""

        let psi = ProcessStartInfo("dotnet")

        psi.Arguments <-
            $"msbuild \"{fsprojPath}\" /t:ResolveAssemblyReferences /p:DesignTimeBuild=true{tfmArg} -getItem:Compile -getItem:ReferencePath -getProperty:DefineConstants -getProperty:OtherFlags"

        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.UseShellExecute <- false
        psi.WorkingDirectory <- Path.GetDirectoryName fsprojPath

        use proc = Process.Start(psi)

        // Read stderr asynchronously first to avoid a deadlock where the process blocks
        // trying to write to a full stderr pipe while we are blocking on stdout.
        // StringBuilder with ErrorDataReceived is not thread-safe; ReadToEndAsync is.
        let stderrTask = proc.StandardError.ReadToEndAsync()
        let stdout = proc.StandardOutput.ReadToEnd()
        let exited = proc.WaitForExit(120_000)

        if not exited then
            proc.Kill()
            Error "dotnet msbuild timed out after 120 seconds. Ensure the project restores successfully: dotnet restore"
        else

            let stderr = stderrTask.Result

            if proc.ExitCode <> 0 then
                Error $"dotnet msbuild failed (exit code {proc.ExitCode}):\n{stderr}"
            else

                try
                    use doc = JsonDocument.Parse(stdout)
                    let root = doc.RootElement

                    let props = root.GetProperty("Properties")
                    let items = root.GetProperty("Items")

                    let defines =
                        match props.TryGetProperty("DefineConstants") with
                        | true, v ->
                            v.GetString().Split([| ';' |], StringSplitOptions.RemoveEmptyEntries)
                            |> Array.toList
                        | _ -> []

                    let otherFlags =
                        match props.TryGetProperty("OtherFlags") with
                        | true, v ->
                            let s = v.GetString()

                            if String.IsNullOrWhiteSpace s then
                                []
                            else
                                // NOTE: Naive whitespace split; corrupts flags with quoted paths (e.g., --pathmap:"...").
                                // The MSBuild response file path (--project-options-file) receives pre-split arrays, avoiding this.
                                s.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries) |> Array.toList
                        | _ -> []

                    let sourceFiles =
                        [ for item in items.GetProperty("Compile").EnumerateArray() ->
                              item.GetProperty("FullPath").GetString() ]

                    let references =
                        [ for item in items.GetProperty("ReferencePath").EnumerateArray() ->
                              item.GetProperty("Identity").GetString() ]

                    if references.IsEmpty then
                        Error
                            $"No assembly references resolved for: {fsprojPath}\nThis usually means the project needs restoring. Run: dotnet restore \"{fsprojPath}\""
                    else
                        Ok(sourceFiles, references, defines, otherFlags)
                with ex ->
                    let preview = if stdout.Length > 500 then stdout.[..499] else stdout
                    Error $"Failed to parse MSBuild output: {ex.Message}\nOutput: {preview}"

    /// Build FSharpProjectOptions from resolved source files, references, and defines.
    let private buildFcsOptions
        (checker: FSharpChecker)
        (fsprojPath: string)
        (sourceFiles: string list)
        (references: string list)
        (defines: string list)
        (otherFlags: string list)
        : FSharpProjectOptions =
        let args =
            [| yield "--noframework"
               yield "--targetprofile:netcore"
               yield "--simpleresolution"
               for d in defines do
                   yield $"--define:{d}"
               for flag in otherFlags do
                   yield flag
               for r in references do
                   yield $"-r:{r}"
               yield! sourceFiles |]

        checker.GetProjectOptionsFromCommandLineArgs(fsprojPath, args)

    /// Check a project and parse all source files into LoadedProject.
    /// Shared by both loadProject and loadProjectFromOptions to avoid duplicated logic.
    let private checkAndParse
        (checker: FSharpChecker)
        (fullPath: string)
        (sourceFiles: string list)
        (fcsOptions: FSharpProjectOptions)
        : Async<Result<LoadedProject, string>> =
        async {
            let! projectResults = checker.ParseAndCheckProject(fcsOptions)

            if projectResults.HasCriticalErrors then
                let errors =
                    projectResults.Diagnostics
                    |> Array.filter (fun d -> d.Severity = FSharp.Compiler.Diagnostics.FSharpDiagnosticSeverity.Error)
                    |> Array.map (fun d -> $"  {d.FileName}({d.StartLine},{d.StartColumn}): {d.Message}")
                    |> String.concat "\n"

                return Error $"Type-check errors:\n{errors}"
            else
                // Re-parse each file individually to obtain ParsedInput (untyped AST) trees.
                // ParseAndCheckProject parses internally but does not expose per-file ParsedInput.
                // These syntax-only calls do not repeat type-checking. SourceText instances differ
                // from ParseAndCheckProject's, so FCS cache misses are expected on cold invocations.
                let parsingOptions, _diagnostics =
                    checker.GetParsingOptionsFromProjectOptions(fcsOptions)

                let! parsedFiles =
                    sourceFiles
                    |> List.toArray
                    |> Array.map (parseSourceFile checker parsingOptions)
                    |> Async.Sequential

                let parsedFiles = parsedFiles |> Array.choose id |> Array.toList

                return
                    Ok
                        { ProjectPath = fullPath
                          ParsedFiles = parsedFiles
                          CheckResults = projectResults }
        }

    /// Read a pre-resolved project options JSON file written by the MSBuild target.
    let readResolvedOptions (path: string) : Result<ResolvedProjectOptions, string> =
        if not (File.Exists path) then
            Error $"Project options file not found: {path}"
        else
            try
                use stream = File.OpenRead(path)
                use doc = JsonDocument.Parse(stream)
                let root = doc.RootElement

                let getStringArray (propName: string) =
                    match root.TryGetProperty(propName) with
                    | true, v -> [ for elem in v.EnumerateArray() -> elem.GetString() ]
                    | _ -> []

                let sourceFiles = getStringArray "sourceFiles"
                let references = getStringArray "references"
                let defines = getStringArray "defines"
                let otherFlags = getStringArray "otherFlags"

                if references.IsEmpty then
                    Error
                        $"No assembly references in options file: {path}\nThis usually means the project needs restoring. Run: dotnet restore"
                else
                    Ok
                        { SourceFiles = sourceFiles
                          References = references
                          Defines = defines
                          OtherFlags = otherFlags }
            with ex ->
                Error $"Failed to parse project options file '{path}': {ex.Message}"

    /// Load an F# project using pre-resolved MSBuild options, bypassing subprocess spawning.
    /// Uses extractionChecker (keepAssemblyContents = false, projectCacheSize = 1) to reduce
    /// peak memory on the MSBuild invocation path where expression trees are not needed.
    let loadProjectFromOptions
        (fsprojPath: string)
        (options: ResolvedProjectOptions)
        : Async<Result<LoadedProject, string>> =
        async {
            try
                let fullPath = Path.GetFullPath fsprojPath

                if options.SourceFiles.IsEmpty then
                    return Error $"No source files in resolved options for project: {fullPath}"
                else

                    let fcsOptions =
                        buildFcsOptions
                            extractionChecker
                            fullPath
                            options.SourceFiles
                            options.References
                            options.Defines
                            options.OtherFlags

                    return! checkAndParse extractionChecker fullPath options.SourceFiles fcsOptions
            with ex ->
                return Error $"Failed to load project from options: {ex.Message}"
        }

    /// Load an F# project, parse and type-check all files.
    /// Uses dotnet msbuild structured output (no Ionide.ProjInfo dependency).
    let loadProject (fsprojPath: string) : Async<Result<LoadedProject, string>> =
        async {
            try
                if not (File.Exists fsprojPath) then
                    return Error $"Project file not found: {fsprojPath}"
                else
                    let fullPath = Path.GetFullPath fsprojPath

                    match resolveProjectOptions fullPath with
                    | Error e -> return Error e
                    | Ok(sourceFiles, references, defines, otherFlags) ->

                        if sourceFiles.IsEmpty then
                            return Error $"No source files found in project: {fullPath}"
                        else

                            let fcsOptions =
                                buildFcsOptions checker fullPath sourceFiles references defines otherFlags

                            return! checkAndParse checker fullPath sourceFiles fcsOptions
            with ex ->
                return Error $"Failed to load project: {ex.Message}"
        }
