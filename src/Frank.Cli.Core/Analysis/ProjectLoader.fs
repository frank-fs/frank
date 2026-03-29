namespace Frank.Cli.Core.Analysis

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Text.Json
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open Frank.Cli.Core.Shared

type LoadedProject =
    { ProjectPath: string
      ParsedFiles: (string * ParsedInput) list
      CheckResults: FSharpCheckProjectResults }

module ProjectLoader =

    /// Singleton FSharpChecker shared across all loadProject calls.
    /// Creating a new checker per call is expensive (~5 min each); the checker
    /// caches project snapshots internally, so reuse is both safe and fast.
    let private checker = FSharpChecker.Create(keepAssemblyContents = true)

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

        let stderrBuf = StringBuilder()

        proc.ErrorDataReceived.Add(fun args ->
            if not (isNull args.Data) then
                stderrBuf.AppendLine(args.Data) |> ignore)

        proc.BeginErrorReadLine()

        let stdout = proc.StandardOutput.ReadToEnd()
        let exited = proc.WaitForExit(120_000)

        if not exited then
            proc.Kill()
            Error "dotnet msbuild timed out after 120 seconds. Ensure the project restores successfully: dotnet restore"
        else

            let stderr = stderrBuf.ToString()

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

                            let options =
                                buildFcsOptions checker fullPath sourceFiles references defines otherFlags

                            let! projectResults = checker.ParseAndCheckProject(options)

                            if projectResults.HasCriticalErrors then
                                let errors =
                                    projectResults.Diagnostics
                                    |> Array.filter (fun d ->
                                        d.Severity = FSharp.Compiler.Diagnostics.FSharpDiagnosticSeverity.Error)
                                    |> Array.map (fun d ->
                                        $"  {d.FileName}({d.StartLine},{d.StartColumn}): {d.Message}")
                                    |> String.concat "\n"

                                return Error $"Type-check errors:\n{errors}"
                            else
                                // Derive parsing options from project options to include
                                // conditional defines, language version, and other flags.
                                // Using Default would miss cross-file conditional compilation.
                                let parsingOptions, _diagnostics =
                                    checker.GetParsingOptionsFromProjectOptions(options)

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
            with ex ->
                return Error $"Failed to load project: {ex.Message}"
        }
