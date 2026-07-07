namespace Frank.Cli.MSBuild

open Microsoft.Build.Framework
open Microsoft.Build.Utilities
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Semantic.VocabCheck

/// MSBuild task: reads the lock file and emits an MSBuild Warning (code FRANK002)
/// for each vocabulary prefix that is neither dereferenceable (in lock.Vocabularies)
/// nor covered by a declared route. No network I/O.
type CheckUndereferenceableVocabTask() =
    inherit Task()

    [<Required>]
    member val LockFilePath: string = "" with get, set

    member val Routes: ITaskItem[] = [||] with get, set

    [<Output>]
    member val ProblematicPrefixes: ITaskItem[] = [||] with get, set

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
                let routes = this.Routes |> Array.map (fun item -> item.ItemSpec) |> Array.toList

                let referencedNs = lock.DeclaredPrefixes |> Map.toList |> List.map fst

                let problems = checkUndereferenceableVocab lock routes referencedNs

                this.ProblematicPrefixes <- problems |> List.map (fun p -> TaskItem(p) :> ITaskItem) |> Array.ofList

                true
