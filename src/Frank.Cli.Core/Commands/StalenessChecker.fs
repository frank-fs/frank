namespace Frank.Cli.Core.Commands

open System
open System.IO
open System.Security.Cryptography
open Frank.Cli.Core.State

/// Result of checking whether extraction state is stale relative to source files.
type StalenessResult =
    /// Source files have not changed since extraction
    | Fresh
    /// Source files have changed since extraction
    | Stale
    /// No source files recorded in state (cannot determine staleness)
    | Indeterminate

/// Shared staleness detection logic used by ValidateCommand and StatusCommand.
module StalenessChecker =

    /// Compute a SHA-256 hash of a file's contents.
    let computeFileHash (filePath: string) : string =
        use sha256 = SHA256.Create()
        use stream = File.OpenRead(filePath)
        let hash = sha256.ComputeHash(stream)
        BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant()

    /// Check whether the extraction state is stale by comparing current source
    /// file hashes against the stored SourceHash.
    let checkStaleness (state: ExtractionState) : StalenessResult =
        let sourceFiles =
            state.SourceMap
            |> Map.values
            |> Seq.map (fun loc -> loc.File)
            |> Seq.distinct
            |> Seq.toList

        if sourceFiles.IsEmpty then
            Indeterminate
        else
            let currentHashes =
                sourceFiles
                |> List.choose (fun f ->
                    if File.Exists f then Some(computeFileHash f) else None)
                |> String.concat ""

            let currentCombinedHash =
                if currentHashes.Length > 0 then
                    use sha256 = SHA256.Create()
                    let bytes = System.Text.Encoding.UTF8.GetBytes(currentHashes)
                    BitConverter.ToString(sha256.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant()
                else
                    ""

            if currentCombinedHash <> state.Metadata.SourceHash
               && state.Metadata.SourceHash <> "" then
                Stale
            else
                Fresh
