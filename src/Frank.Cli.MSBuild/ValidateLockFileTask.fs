namespace Frank.Cli.MSBuild

open Microsoft.Build.Framework
open Microsoft.Build.Utilities
open Frank.Semantic
open Frank.Semantic.LockFile

/// MSBuild task: reads the semantic lock file and fails the build if any mapping
/// or field mapping has a status other than Confirmed.
/// Error code MS001 is emitted with the count of non-confirmed entries.
type ValidateLockFileTask() =
    inherit Task()

    [<Required>]
    member val LockFilePath: string = "" with get, set

    override this.Execute() =
        if System.String.IsNullOrWhiteSpace this.LockFilePath then
            this.Log.LogError("ValidateLockFileTask: LockFilePath must not be empty.")
            false
        else

            match LockFile.read this.LockFilePath with
            | Error msg ->
                this.Log.LogError($"ValidateLockFileTask: could not read lock file: {msg}")
                false
            | Ok lock ->
                let draftMappings = lock.Mappings |> List.filter (fun m -> not (isDecided m.Status))

                let liveMappings = lock.Mappings |> List.filter (fun m -> m.Status <> Excluded)

                let draftCases =
                    liveMappings
                    |> List.collect (fun m -> MappingShape.caseMappings m.Shape)
                    |> List.filter (fun c -> not (isDecided c.Status))

                let draftFields =
                    liveMappings
                    |> List.collect (fun m -> MappingShape.activePayloadFields m.Shape)
                    |> List.filter (fun f -> not (isDecided f.Status))

                let total = draftMappings.Length + draftCases.Length + draftFields.Length

                if total > 0 then
                    this.Log.LogError(
                        subcategory = null,
                        errorCode = "MS001",
                        helpKeyword = null,
                        file = this.LockFilePath,
                        lineNumber = 0,
                        columnNumber = 0,
                        endLineNumber = 0,
                        endColumnNumber = 0,
                        message =
                            $"Lock file has {total} undecided (proposed/unresolved) mapping(s); run 'frank semantic finalize' (zero-LLM) or 'frank semantic clarify' (LLM) to decide.",
                        messageArgs = [||]
                    )

                    false
                else
                    true
