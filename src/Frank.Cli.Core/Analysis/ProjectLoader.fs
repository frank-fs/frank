namespace Frank.Cli.Core.Analysis

open System
open System.IO
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text

type LoadedProject = {
    ProjectPath: string
    ParsedFiles: (string * ParsedInput) list
    CheckResults: FSharpCheckProjectResults
}

module ProjectLoader =

    let private parseSourceFile (checker: FSharpChecker) (sourceFile: string) =
        async {
            let sourceText = SourceText.ofString (File.ReadAllText sourceFile)
            let parsingOptions = { FSharpParsingOptions.Default with SourceFiles = [| sourceFile |] }
            let! parseResult = checker.ParseFile(sourceFile, sourceText, parsingOptions)
            if parseResult.ParseHadErrors then return None
            else return Some (sourceFile, parseResult.ParseTree)
        }

    /// Load an F# project, parse and type-check all files
    let loadProject (fsprojPath: string) : Async<Result<LoadedProject, string>> =
        async {
            try
                if not (File.Exists fsprojPath) then
                    return Error $"Project file not found: {fsprojPath}"
                else
                    let toolsPath = Ionide.ProjInfo.Init.init (DirectoryInfo(Path.GetDirectoryName fsprojPath)) None
                    let loader = Ionide.ProjInfo.WorkspaceLoader.Create(toolsPath, [])
                    let projects =
                        loader.LoadProjects [ fsprojPath ]
                        |> Seq.toList

                    match projects with
                    | [] ->
                        return Error $"No project data returned for: {fsprojPath}"
                    | projectInfo :: _ ->
                        let checker = FSharpChecker.Create()

                        let fcsOptions =
                            Ionide.ProjInfo.FCS.mapToFSharpProjectOptions projectInfo projects

                        let! projectResults = checker.ParseAndCheckProject(fcsOptions)

                        if projectResults.HasCriticalErrors then
                            let errors =
                                projectResults.Diagnostics
                                |> Array.filter (fun d -> d.Severity = FSharp.Compiler.Diagnostics.FSharpDiagnosticSeverity.Error)
                                |> Array.map (fun d -> $"  {d.FileName}({d.StartLine},{d.StartColumn}): {d.Message}")
                                |> String.concat "\n"
                            return Error $"Type-check errors:\n{errors}"
                        else
                            let! parsedFiles =
                                fcsOptions.SourceFiles
                                |> Array.map (parseSourceFile checker)
                                |> Async.Sequential
                            let parsedFiles = parsedFiles |> Array.choose id |> Array.toList

                            return Ok {
                                ProjectPath = fsprojPath
                                ParsedFiles = parsedFiles
                                CheckResults = projectResults
                            }
            with ex ->
                return Error $"Failed to load project: {ex.Message}"
        }
