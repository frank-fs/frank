namespace Frank.Cli.MSBuild

open System
open System.IO
open Microsoft.Build.Framework
open Microsoft.Build.Utilities
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Cli.Core

/// MSBuild task: evaluates the project's vocabulary CE IN-PROCESS via FCS typed-AST
/// reconstruction, extracts type metadata, then calls ValidationEmitter to write
/// GeneratedValidation.fs to the intermediate output directory.
/// No child dotnet/msbuild processes are spawned.
type GenerateValidationTask() =
    inherit Task()

    [<Required>]
    member val LockFilePath: string = "" with get, set

    [<Required>]
    member val OutputPath: string = "" with get, set

    [<Required>]
    member val ModuleName: string = "" with get, set

    [<Required>]
    member val SourceFiles: ITaskItem[] = [||] with get, set

    [<Required>]
    member val AssemblyRefs: ITaskItem[] = [||] with get, set

    member val VocabularyBinding: string = "registry" with get, set

    [<Output>]
    member val GeneratedFile: string = "" with get, set

    override this.Execute() =
        if String.IsNullOrWhiteSpace this.LockFilePath then
            this.Log.LogError("GenerateValidationTask: LockFilePath must not be empty.")
            false
        elif String.IsNullOrWhiteSpace this.OutputPath then
            this.Log.LogError("GenerateValidationTask: OutputPath must not be empty.")
            false
        elif String.IsNullOrWhiteSpace this.ModuleName then
            this.Log.LogError("GenerateValidationTask: ModuleName must not be empty.")
            false
        elif this.SourceFiles.Length = 0 then
            this.Log.LogError("GenerateValidationTask: SourceFiles must not be empty.")
            false
        elif this.AssemblyRefs.Length = 0 then
            this.Log.LogError("GenerateValidationTask: AssemblyRefs must not be empty.")
            false
        else
            this.RunGenerate()

    member private this.RunGenerate() =
        match LockFile.read this.LockFilePath with
        | Error msg ->
            this.Log.LogError($"GenerateValidationTask: could not read lock file: {msg}")
            false
        | Ok lock ->
            let refs = this.AssemblyRefs |> Array.map (fun i -> i.ItemSpec) |> Array.toList
            let sources = this.SourceFiles |> Array.map (fun i -> i.ItemSpec) |> Array.toList
            let binding = this.VocabularyBinding

            match VocabularyEvaluator.evalRegistry refs sources binding with
            | Error msg ->
                this.Log.LogError($"GenerateValidationTask: FCS evaluation failed: {msg}")
                false
            | Ok registry ->
                match Extractor.extractTypeInfosFromSources (List.toArray sources) (List.toArray refs) with
                | Error msg ->
                    this.Log.LogError($"GenerateValidationTask: type extraction failed: {msg}")
                    false
                | Ok typeInfos ->
                    let typesByName = typeInfos |> List.map (fun ti -> ti.FullName, ti) |> Map.ofList

                    match ValidationEmitter.emit this.ModuleName registry lock typesByName with
                    | Error msg ->
                        this.Log.LogError($"GenerateValidationTask: code generation failed: {msg}")
                        false
                    | Ok source -> this.WriteOutput source

    member private this.WriteOutput(source: string) =
        let outPath = Path.Combine(this.OutputPath, "GeneratedValidation.fs")

        try
            Directory.CreateDirectory(this.OutputPath) |> ignore
            File.WriteAllText(outPath, source)
            this.GeneratedFile <- outPath
            true
        with ex ->
            this.Log.LogError($"GenerateValidationTask: could not write '{outPath}': {ex.Message}")
            false
