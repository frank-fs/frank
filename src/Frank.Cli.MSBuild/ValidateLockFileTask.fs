namespace Frank.Cli.MSBuild

open Microsoft.Build.Framework
open Microsoft.Build.Utilities
open Frank.Semantic

/// MSBuild task that validates the semantic lock file before compilation.
/// Fails the build if any mapping has status: "proposed" or "unresolved".
type ValidateLockFileTask() =
    inherit Task()

    [<Required>]
    member val LockFilePath: string = "" with get, set

    override this.Execute() =
        match LockFile.read this.LockFilePath with
        | Error msg ->
            this.Log.LogError($"frank semantic: failed to read lock file at '{this.LockFilePath}': {msg}")
            false
        | Ok lockFile ->
            let blocking =
                lockFile.Mappings
                |> List.filter (fun m -> m.Status = Proposed || m.Status = Unresolved)

            if blocking.IsEmpty then
                true
            else
                let typeNames = blocking |> List.map _.FsharpType |> String.concat ", "
                let count = blocking.Length

                this.Log.LogError(
                    $"frank semantic: {count} mapping(s) are proposed or unresolved ({typeNames}). "
                    + "Run 'frank semantic clarify' to resolve."
                )

                false
