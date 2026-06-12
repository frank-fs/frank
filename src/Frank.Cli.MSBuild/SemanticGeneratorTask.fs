namespace Frank.Cli.MSBuild

open Microsoft.Build.Framework
open Microsoft.Build.Utilities
open Frank.Semantic

/// Abstract base class for all semantic code generators (B11-B14).
/// Subclasses implement GenerateFiles to produce source files from confirmed mappings.
[<AbstractClass>]
type SemanticGeneratorTask() =
    inherit Task()

    [<Required>]
    member val LockFilePath: string = "" with get, set

    [<Required>]
    member val OutputDirectory: string = "" with get, set

    abstract member GenerateFiles: lockFile: LockFile -> unit

    override this.Execute() =
        match LockFile.read this.LockFilePath with
        | Error msg ->
            this.Log.LogError($"frank semantic: failed to read lock file at '{this.LockFilePath}': {msg}")
            false
        | Ok lockFile ->
            this.GenerateFiles lockFile
            true
