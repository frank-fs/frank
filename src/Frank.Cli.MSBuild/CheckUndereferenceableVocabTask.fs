namespace Frank.Cli.MSBuild

open System.IO
open Microsoft.Build.Framework
open Microsoft.Build.Utilities
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Text
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Semantic.VocabCheck

/// MSBuild task: reads the lock file and emits MSBuild Warning FRANK002 for each
/// vocabulary prefix that is neither dereferenceable (in lock.Vocabularies) nor
/// covered by a resource route found in the project's compiled source files.
/// No network I/O. Derives routes from actual source — not a hand-maintained list.
type CheckUndereferenceableVocabTask() =
    inherit Task()

    [<Required>]
    member val LockFilePath: string = "" with get, set

    /// Absolute paths of the project's @(Compile) items.
    member val SourceFiles: ITaskItem[] = [||] with get, set

    [<Output>]
    member val ProblematicPrefixes: ITaskItem[] = [||] with get, set

    /// Parse one source file and return its resource route literals.
    /// Returns [] on file-not-found or parse failure (defensive, not an error).
    member private this.ParseFileRoutes(checker: FSharpChecker, filePath: string) : string list =
        if not (File.Exists filePath) then
            []
        else
            try
                let sourceText = SourceText.ofString (File.ReadAllText filePath)

                let opts =
                    { FSharpParsingOptions.Default with
                        SourceFiles = [| filePath |] }

                let result = checker.ParseFile(filePath, sourceText, opts) |> Async.RunSynchronously

                if result.ParseHadErrors then
                    this.Log.LogMessage(MessageImportance.Low, $"FRANK002: skipping {filePath} (parse errors)")

                    []
                else
                    extractRoutes result.ParseTree
            with ex ->
                this.Log.LogMessage(MessageImportance.Low, $"FRANK002: skipped {filePath}: {ex.Message}")
                []

    override this.Execute() =
        if System.String.IsNullOrWhiteSpace this.LockFilePath then
            this.Log.LogError "CheckUndereferenceableVocabTask: LockFilePath must not be empty."
            false
        else

            match LockFile.read this.LockFilePath with
            | Error msg ->
                this.Log.LogError $"CheckUndereferenceableVocabTask: could not read lock file: {msg}"
                false
            | Ok lock ->
                let checker = FSharpChecker.Create()

                let routes =
                    this.SourceFiles
                    |> Array.toList
                    |> List.collect (fun item -> this.ParseFileRoutes(checker, item.ItemSpec))

                let referencedNs = lock.DeclaredPrefixes |> Map.toList |> List.map fst

                let problems = checkUndereferenceableVocab lock routes referencedNs

                this.ProblematicPrefixes <- problems |> List.map (fun p -> TaskItem(p) :> ITaskItem) |> Array.ofList

                true
