namespace Frank.Cli.MSBuild

open System
open System.IO
open Microsoft.Build.Framework
open Microsoft.Build.Utilities
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Cli.Core

/// MSBuild task: reads the semantic lock file, builds a minimal VocabularyRegistry
/// from the lock's Vocabularies map, and calls DiscoveryEmitter to write GeneratedDiscovery.fs
/// to the intermediate output directory.
type GenerateDiscoveryTask() =
    inherit Task()

    [<Required>]
    member val LockFilePath: string = "" with get, set

    [<Required>]
    member val OutputPath: string = "" with get, set

    [<Required>]
    member val ModuleName: string = "" with get, set

    member val ProfileUri: string = "/alps/tictactoe" with get, set

    [<Output>]
    member val GeneratedFile: string = "" with get, set

    override this.Execute() =
        if String.IsNullOrWhiteSpace this.LockFilePath then
            this.Log.LogError("GenerateDiscoveryTask: LockFilePath must not be empty.")
            false
        elif String.IsNullOrWhiteSpace this.OutputPath then
            this.Log.LogError("GenerateDiscoveryTask: OutputPath must not be empty.")
            false
        elif String.IsNullOrWhiteSpace this.ModuleName then
            this.Log.LogError("GenerateDiscoveryTask: ModuleName must not be empty.")
            false
        else

            match LockFile.read this.LockFilePath with
            | Error msg ->
                this.Log.LogError($"GenerateDiscoveryTask: could not read lock file: {msg}")
                false
            | Ok lock ->
                let prefixes = lock.Vocabularies |> Map.map (fun _ v -> Uri(v.Uri))

                let registry =
                    { VocabularyRegistry.empty with
                        Prefixes = prefixes }

                match DiscoveryEmitter.emit this.ModuleName this.ProfileUri registry lock with
                | Error msg ->
                    this.Log.LogError($"GenerateDiscoveryTask: code generation failed: {msg}")
                    false
                | Ok source ->
                    let outPath = Path.Combine(this.OutputPath, "GeneratedDiscovery.fs")

                    try
                        Directory.CreateDirectory(this.OutputPath) |> ignore
                        File.WriteAllText(outPath, source)
                        this.GeneratedFile <- outPath
                        true
                    with ex ->
                        this.Log.LogError($"GenerateDiscoveryTask: could not write '{outPath}': {ex.Message}")
                        false
